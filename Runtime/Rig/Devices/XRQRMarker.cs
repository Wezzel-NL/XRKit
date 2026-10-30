using System;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using WezzelNL.XRKit.Runtime.Data;

namespace WezzelNL.XRKit.Runtime.Rig.Devices
{
	public class XRQRMarker : XRTracker
	{
		public TrackableId Id { get; private set; }
		public XRMarkerTracker Tracker { get; private set; }
		public string StringData => TryReadDataString(out var data) ? data : string.Empty;
		public byte[] BytesData => TryReadDataBytes(out var data) ? data : Array.Empty<byte>();
		
		private bool initialized = false;
		public void Initialize(TrackableId id, XRMarkerTracker tracker)
		{
			if(initialized) throw new InvalidOperationException("Marker already initialized");
			Id = id;
			Tracker = tracker;
			initialized = true;
		}
		
		public void UpdateMarker(Pose pose, TrackingState markerTrackingState)
		{
			TrackingState = markerTrackingState switch
			{
				UnityEngine.XR.ARSubsystems.TrackingState.None => XRDeviceTrackingState.None,
				UnityEngine.XR.ARSubsystems.TrackingState.Limited => XRDeviceTrackingState.None,
				UnityEngine.XR.ARSubsystems.TrackingState.Tracking => XRDeviceTrackingState.Position | XRDeviceTrackingState.Rotation,
				_ => throw new ArgumentOutOfRangeException(nameof(markerTrackingState), markerTrackingState, null)
			};
			transform.localPosition = pose.position;
			transform.localRotation = pose.rotation;
		}
		
		public bool TryReadDataString(out string data) => Tracker.TryGetStringData(Id, out data);
		public bool TryReadDataBytes(out byte[] data) => Tracker.TryGetBytesData(Id, out data);
	}
}