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
		
		private SpatialMarkerSubsystem subsystem;
		private bool startedByTracker;
		private bool starting;
		private bool requestingPermission;
		private CancellationTokenSource cts;
		private bool wasRunning;
		private float nextStatusLogTime;
		private readonly Dictionary<TrackableId, XRQRMarker> xrKitMarkers = new();
		private readonly Dictionary<TrackableId, XRMarker> markers = new();
		private static List<SpatialMarkerSubsystem> SubsystemBuffer { get; } = new();
		private static string[] RequiredExtensions { get; } = { "XR_EXT_future", "XR_EXT_spatial_entity", "XR_EXT_spatial_marker_tracking" };

		protected override void OnEnable()
		{
			base.OnEnable();
			ExtensionErrors();

			var permission = GetPermission();
			if (Permission.HasUserAuthorizedPermission(permission))
			{
				StartSubsystem();
				return;
			}

			RequestPermission(permission);
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			ClearMarkers();

			cts?.Cancel();
			cts?.Dispose();
			cts = null;

			if (startedByTracker && subsystem is { running: true })
			{
				subsystem.Stop();
			}
			startedByTracker = false;
			wasRunning = false;
			subsystem = null;
		}

		private void Update()
		{
			if (subsystem is not { running: true })
			{
				if (wasRunning) Debug.LogError($"Marker subsystem stopped running unexpectedly.");
				wasRunning = false;
				TrackingState = XRDeviceTrackingState.None;
				return;
			}
			
			wasRunning = true;
			TrackingState = XRDeviceTrackingState.Position | XRDeviceTrackingState.Rotation;

			using var changes = subsystem.GetChanges(Allocator.Temp);
			foreach (var marker in changes.added) OnTrackMarker(marker);
			foreach (var marker in changes.updated) OnTrackMarker(marker);
			foreach (var trackableId in changes.removed) OnMarkerLost(trackableId);
		}

		private void ExtensionErrors()
		{
			foreach (var extension in RequiredExtensions)
			{
				if (!OpenXRRuntime.IsExtensionEnabled(extension)) Debug.LogError($"Extension '{extension}' is NOT enabled. The runtime likely doesn't support it. Please make sure your headset supports maker tracking.");
			}
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

			var callbacks = new PermissionCallbacks();
			callbacks.PermissionGranted += p =>
			{
				requestingPermission = false;
				if (isActiveAndEnabled) StartSubsystem();
			};
			callbacks.PermissionDenied += p =>
			{
				requestingPermission = false;
				enabled = false;
				Debug.LogError($"Permission {p} is required for Marker Tracking!");
			};
			Permission.RequestUserPermission(permission, callbacks);
		}

		private async void StartSubsystem()
		{
			await Awaitable.MainThreadAsync();
			if (starting) return;

			subsystem = FindSubsystem();
			if (subsystem == null) return;
			if (subsystem.running) return;
			
			starting = true;
			cts?.Dispose();
			cts = new CancellationTokenSource();
			var target = subsystem;

			try
			{
				var status = await target.TryStartAsync(cts.Token);
				if (!status.IsSuccess()) return;
				if (subsystem != target)
				{
					if (target.running) target.Stop();
					return;
				}

				startedByTracker = true;
			}
			catch (Exception exception)
			{
				Debug.LogError($"Could not start XR Marker Subsystem due to exception");
				Debug.LogException(exception);
			}
			finally
			{
				starting = false;
			}
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
			if (marker.dataBuffer.bufferType is not XRSpatialBufferType.String) return false;

			var result = subsystem.TryGetStringData(trackableId, marker.dataBuffer);
			if (!result.status.IsSuccess()) return false;

			data = result.value;
			return true;
		}

		public bool TryGetBytesData(TrackableId trackableId, out byte[] data)
		{
			data = null;
			if (subsystem is not { running: true }) return false;
			if (!markers.TryGetValue(trackableId, out var marker)) return false;
			if (marker.dataBuffer.bufferType is not XRSpatialBufferType.Uint8) return false;
			
			var result = subsystem.TryGetBytesData(trackableId, marker.dataBuffer);
			if (!result.status.IsSuccess()) return false;

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