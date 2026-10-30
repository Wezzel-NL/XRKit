using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.SpatialEntities;
using WezzelNL.XRKit.Runtime.Data;

namespace WezzelNL.XRKit.Runtime.Rig.Devices
{
	public class XRMarkerTracker : XRDevice
	{
		public event Action<XRQRMarker> OnQRMarkerFound;
		public event Action<XRQRMarker> OnQRMarkerLost;
		private SpatialMarkerSubsystem subsystem;
		private readonly Dictionary<TrackableId, XRQRMarker> xrKitMarkers = new();
		private readonly Dictionary<TrackableId, XRMarker> markers = new();
		private static List<SpatialMarkerSubsystem> SubsystemBuffer { get; } = new();

		protected override void OnEnable()
		{
			base.OnEnable();
			subsystem = FindRunningSubsystem();
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			ClearMarkers();
			subsystem = null;
		}

		private void Update()
		{
			if (subsystem is { running: true }) TrackingState = XRDeviceTrackingState.Position | XRDeviceTrackingState.Rotation;
			else TrackingState = XRDeviceTrackingState.None;
			
			if (!EnsureSubsystem()) return;

			using var changes = subsystem.GetChanges(Allocator.Temp);
			foreach (var marker in changes.added) OnTrackMarker(marker);
			foreach (var marker in changes.updated) OnTrackMarker(marker);
			foreach (var trackableId in changes.removed) OnMarkerLost(trackableId);
		}

		private bool EnsureSubsystem()
		{
			if (TrackingState != XRDeviceTrackingState.None) return true;
			if (subsystem != null) ClearMarkers();

			subsystem = FindRunningSubsystem();
			return subsystem != null;
		}

		private static SpatialMarkerSubsystem FindRunningSubsystem()
		{
			SubsystemManager.GetSubsystems(SubsystemBuffer);
			foreach (var system in SubsystemBuffer) if (system.running) return system;
			return null;
		}
		
		public bool TryGetMarker(TrackableId trackableId, out XRQRMarker marker) => xrKitMarkers.TryGetValue(trackableId, out marker);
		
		public bool TryGetStringData(TrackableId trackableId, out string data)
		{
			data = null;
			if (TrackingState == XRDeviceTrackingState.None) return false;
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
			if (TrackingState == XRDeviceTrackingState.None) return false;
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
			if(xrKitMarkers.TryGetValue(id, out var xrKitMarker)) xrKitMarker.UpdateMarker(marker.pose, marker.trackingState);
			else
			{
				var go = new GameObject("Tracked Marker");
				var mark = go.AddComponent<XRQRMarker>();
				go.transform.SetParent(Rig.transform);
				mark.Initialize(marker.trackableId, this);
				mark.UpdateMarker(marker.pose, marker.trackingState);
				xrKitMarkers.Add(id, mark);
				OnQRMarkerFound?.Invoke(mark);
			}
		}

		private void DestroyMarker(XRMarker marker)
		{
			if (!xrKitMarkers.Remove(marker.trackableId, out var kitMarker)) return;
			OnQRMarkerLost?.Invoke(kitMarker);
			Destroy(kitMarker.gameObject);
		}
	}
}