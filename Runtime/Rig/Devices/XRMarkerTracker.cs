using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.SpatialEntities;
using WezzelNL.XRKit.Runtime.Data;

namespace WezzelNL.XRKit.Runtime.Rig.Devices
{
	public class XRMarkerTracker : XRDevice
	{
		public event Action<XRQRMarker> OnQRMarkerFound;
		public event Action<XRQRMarker> OnQRMarkerLost;
		private const string LogPrefix = "[XRMarkerTracker]";
		private const float StatusLogInterval = 5f;
		private SpatialMarkerSubsystem subsystem;
		private bool startedByTracker;
		private bool starting;
		private bool requestingPermission;
		private CancellationTokenSource startCancellation;
		private bool wasRunning;
		private float nextStatusLogTime;
		private readonly Dictionary<TrackableId, XRQRMarker> xrKitMarkers = new();
		private readonly Dictionary<TrackableId, XRMarker> markers = new();
		private static List<SpatialMarkerSubsystem> SubsystemBuffer { get; } = new();
		private static List<XRMarkerSubsystem> AnyMarkerSubsystemBuffer { get; } = new();
		private static string[] RequiredExtensions { get; } = { "XR_EXT_future", "XR_EXT_spatial_entity", "XR_EXT_spatial_marker_tracking" };

		protected override void OnEnable()
		{
			base.OnEnable();
			LogEnvironment();

			var permission = GetPermission();
			if (Permission.HasUserAuthorizedPermission(permission))
			{
				Debug.Log($"{LogPrefix} Permission '{permission}' already granted.");
				StartSubsystem();
				return;
			}

			RequestPermission(permission);
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			ClearMarkers();

			startCancellation?.Cancel();
			startCancellation?.Dispose();
			startCancellation = null;

			if (startedByTracker && subsystem is { running: true })
			{
				subsystem.Stop();
				Debug.Log($"{LogPrefix} Stopped marker subsystem.");
			}
			startedByTracker = false;
			wasRunning = false;
			subsystem = null;
		}

		private void Update()
		{
			LogStatus();

			if (subsystem is not { running: true })
			{
				if (wasRunning) Debug.LogError($"{LogPrefix} Marker subsystem stopped running unexpectedly.");
				wasRunning = false;
				TrackingState = XRDeviceTrackingState.None;
				return;
			}

			if (!wasRunning) Debug.Log($"{LogPrefix} Marker subsystem is running. Waiting for markers...");
			wasRunning = true;
			TrackingState = XRDeviceTrackingState.Position | XRDeviceTrackingState.Rotation;

			using var changes = subsystem.GetChanges(Allocator.Temp);
			foreach (var marker in changes.added)
			{
				Debug.Log($"{LogPrefix} Marker added: {marker.trackableId}, tracking {marker.trackingState}, data {marker.dataBuffer.bufferType}, pose {marker.pose.position}");
				OnTrackMarker(marker);
			}
			foreach (var marker in changes.updated) OnTrackMarker(marker);
			foreach (var trackableId in changes.removed)
			{
				Debug.Log($"{LogPrefix} Marker removed: {trackableId}");
				OnMarkerLost(trackableId);
			}
		}

		private void LogEnvironment()
		{
			Debug.Log($"{LogPrefix} Device: '{SystemInfo.deviceModel}', OpenXR runtime: '{OpenXRRuntime.name}' {OpenXRRuntime.version}");

			foreach (var extension in RequiredExtensions)
			{
				if (OpenXRRuntime.IsExtensionEnabled(extension)) Debug.Log($"{LogPrefix} Extension '{extension}' is enabled.");
				else Debug.LogError($"{LogPrefix} Extension '{extension}' is NOT enabled. The runtime doesn't support it or the Spatial Entities feature didn't request it.");
			}

			var settings = OpenXRSettings.Instance;
			if (!settings)
			{
				Debug.LogError($"{LogPrefix} OpenXRSettings.Instance is null. Is OpenXR enabled as plug-in provider for this platform?");
				return;
			}

			var feature = settings.GetFeature<SpatialMarkerFeature>();
			if (!feature) Debug.LogError($"{LogPrefix} SpatialMarkerFeature not found in OpenXR settings for this platform.");
			else if (!feature.enabled) Debug.LogError($"{LogPrefix} 'Spatial Entities: Markers' is disabled. Enable it under XR Plug-in Management > OpenXR > Spatial Entities.");
			else Debug.Log($"{LogPrefix} 'Spatial Entities: Markers' feature is enabled.");
		}

		private void LogStatus()
		{
			if (Time.unscaledTime < nextStatusLogTime) return;
			nextStatusLogTime = Time.unscaledTime + StatusLogInterval;

			if (subsystem == null) Debug.LogWarning($"{LogPrefix} Status: no subsystem. Permission granted: {Permission.HasUserAuthorizedPermission(GetPermission())}, requesting: {requestingPermission}");
			else Debug.Log($"{LogPrefix} Status: subsystem running {subsystem.running}, starting {starting}, started by tracker {startedByTracker}, tracked markers {markers.Count}");
		}

		private static string GetPermission()
		{
			if (SystemInfo.deviceModel.Contains("Oculus") || SystemInfo.deviceModel.Contains("Quest")) return "com.oculus.permission.USE_SCENE";
			return "android.permission.SCENE_UNDERSTANDING_COARSE";
		}

		private void RequestPermission(string permission)
		{
			if (requestingPermission) return;
			requestingPermission = true;
			Debug.Log($"{LogPrefix} Requesting permission '{permission}'...");

			var callbacks = new PermissionCallbacks();
			callbacks.PermissionGranted += p =>
			{
				requestingPermission = false;
				Debug.Log($"{LogPrefix} Permission '{p}' granted.");
				if (isActiveAndEnabled) StartSubsystem();
				else Debug.LogWarning($"{LogPrefix} Permission granted while tracker was disabled. Subsystem will start on next enable.");
			};
			callbacks.PermissionDenied += p =>
			{
				requestingPermission = false;
				Debug.LogError($"{LogPrefix} Permission '{p}' denied. Marker tracking is disabled.");
				enabled = false;
			};
			callbacks.PermissionDeniedAndDontAskAgain += p =>
			{
				requestingPermission = false;
				Debug.LogError($"{LogPrefix} Permission '{p}' denied permanently. Grant it in the headset's app settings or reinstall the app.");
				enabled = false;
			};
			Permission.RequestUserPermission(permission, callbacks);
		}

		private async void StartSubsystem()
		{
			await Awaitable.MainThreadAsync();
			if (starting) return;
			LogAllMarkerSubsystems();

			subsystem = FindSubsystem();
			if (subsystem == null)
			{
				Debug.LogError($"{LogPrefix} No SpatialMarkerSubsystem found. Is 'Spatial Entities: Markers' enabled for this platform, and is OpenXR initialized?");
				return;
			}

			if (subsystem.running)
			{
				Debug.Log($"{LogPrefix} SpatialMarkerSubsystem was already running (started by something else).");
				return;
			}

			Debug.Log($"{LogPrefix} Subsystem permission state before start: {subsystem.GetPermissionState()}");
			Debug.Log($"{LogPrefix} Starting SpatialMarkerSubsystem with TryStartAsync...");

			starting = true;
			startCancellation?.Dispose();
			startCancellation = new CancellationTokenSource();
			var target = subsystem;

			try
			{
				var status = await target.TryStartAsync(startCancellation.Token);
				if (!status.IsSuccess())
				{
					Debug.LogError($"{LogPrefix} TryStartAsync failed: {status} (statusCode {status.statusCode}, nativeStatusCode {status.nativeStatusCode}). Permission state: {target.GetPermissionState()}");
					return;
				}

				if (subsystem != target)
				{
					Debug.LogWarning($"{LogPrefix} Tracker was disabled while starting. Stopping the subsystem again.");
					if (target.running) target.Stop();
					return;
				}

				startedByTracker = true;
				if (target.running) Debug.Log($"{LogPrefix} TryStartAsync succeeded. Subsystem is running.");
				else Debug.LogWarning($"{LogPrefix} TryStartAsync reported success but the subsystem is not running. Watch the status logs.");
			}
			catch (OperationCanceledException)
			{
				Debug.LogWarning($"{LogPrefix} TryStartAsync was canceled because the tracker was disabled.");
			}
			catch (Exception exception)
			{
				Debug.LogError($"{LogPrefix} TryStartAsync threw {exception.GetType().Name}: {exception.Message}");
				Debug.LogException(exception);
			}
			finally
			{
				starting = false;
			}
		}

		private static void LogAllMarkerSubsystems()
		{
			SubsystemManager.GetSubsystems(AnyMarkerSubsystemBuffer);
			if (AnyMarkerSubsystemBuffer.Count == 0)
			{
				Debug.LogError($"{LogPrefix} No XRMarkerSubsystem of any provider exists. OpenXR may not have initialized.");
				return;
			}

			foreach (var system in AnyMarkerSubsystemBuffer) Debug.Log($"{LogPrefix} Found marker subsystem '{system.GetType().FullName}', id '{system.subsystemDescriptor.id}', running {system.running}");
			if (AnyMarkerSubsystemBuffer.Count > 1) Debug.LogWarning($"{LogPrefix} Multiple marker subsystems exist. AR Foundation supports one provider per feature; disable the other provider's marker feature.");
		}

		private static SpatialMarkerSubsystem FindSubsystem()
		{
			SubsystemManager.GetSubsystems(SubsystemBuffer);
			return SubsystemBuffer.Count > 0 ? SubsystemBuffer[0] : null;
		}

		public bool TryGetMarker(TrackableId trackableId, out XRQRMarker marker) => xrKitMarkers.TryGetValue(trackableId, out marker);

		public bool TryGetStringData(TrackableId trackableId, out string data)
		{
			data = null;
			if (subsystem is not { running: true }) return false;
			if (!markers.TryGetValue(trackableId, out var marker)) return false;
			if (marker.dataBuffer.bufferType is not XRSpatialBufferType.String)
			{
				Debug.LogWarning($"{LogPrefix} Marker {trackableId} has data type {marker.dataBuffer.bufferType}, not String.");
				return false;
			}

			var result = subsystem.TryGetStringData(trackableId, marker.dataBuffer);
			if (!result.status.IsSuccess())
			{
				Debug.LogError($"{LogPrefix} TryGetStringData failed for {trackableId}: {result.status}");
				return false;
			}

			data = result.value;
			return true;
		}

		public bool TryGetBytesData(TrackableId trackableId, out byte[] data)
		{
			data = null;
			if (subsystem is not { running: true }) return false;
			if (!markers.TryGetValue(trackableId, out var marker)) return false;
			if (marker.dataBuffer.bufferType is not XRSpatialBufferType.Uint8)
			{
				Debug.LogWarning($"{LogPrefix} Marker {trackableId} has data type {marker.dataBuffer.bufferType}, not Uint8.");
				return false;
			}

			var result = subsystem.TryGetBytesData(trackableId, marker.dataBuffer);
			if (!result.status.IsSuccess())
			{
				Debug.LogError($"{LogPrefix} TryGetBytesData failed for {trackableId}: {result.status}");
				return false;
			}

			data = result.value;
			return true;
		}

		private void OnTrackMarker(XRMarker marker)
		{
			markers[marker.trackableId] = marker;
			CreateOrUpdateMarker(marker);
		}

		private void OnMarkerLost(TrackableId trackableId)
		{
			if (!markers.Remove(trackableId, out var marker)) return;
			DestroyMarker(marker);
		}

		private void ClearMarkers()
		{
			foreach (var marker in markers.Values) DestroyMarker(marker);
			markers.Clear();
		}

		private void CreateOrUpdateMarker(XRMarker marker)
		{
			var id = marker.trackableId;
			if (xrKitMarkers.TryGetValue(id, out var xrKitMarker))
			{
				xrKitMarker.UpdateMarker(marker.pose, marker.trackingState);
				return;
			}

			if (!Rig) Debug.LogError($"{LogPrefix} Rig is null. Marker {id} will be created at the scene root.");

			var go = new GameObject("Tracked Marker");
			var mark = go.AddComponent<XRQRMarker>();
			if (Rig) go.transform.SetParent(Rig.transform);
			mark.Initialize(id, this);
			mark.UpdateMarker(marker.pose, marker.trackingState);
			xrKitMarkers.Add(id, mark);
			OnQRMarkerFound?.Invoke(mark);
		}

		private void DestroyMarker(XRMarker marker)
		{
			if (!xrKitMarkers.Remove(marker.trackableId, out var kitMarker)) return;
			OnQRMarkerLost?.Invoke(kitMarker);
			Destroy(kitMarker.gameObject);
		}
	}
}