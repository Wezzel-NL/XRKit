using System;
using UnityEngine;
using UnityEngine.InputSystem;
using WezzelNL.XRKit.Runtime.Data;

namespace WezzelNL.XRKit.Runtime.Rig.Devices
{
	//TODO test on device
	
	[AddComponentMenu("WezzelNL/XR/XREyeTracker")]
	public class XREyeTracker : XRDevice
	{
		public InputAction GazePositionAction { get; } = new("Gaze Position", InputActionType.Value, "<EyeGaze>/pose/position");
		public InputAction GazeRotationAction { get; } = new("Gaze Rotation", InputActionType.Value, "<EyeGaze>/pose/rotation");
		public InputAction GazeTrackedAction { get; } = new("Gaze Tracked", InputActionType.Value, "<EyeGaze>/pose/trackingState");

		public Vector3 GazePosition { get; private set; } = Vector3.zero;
		public Quaternion GazeRotation { get; private set; } = Quaternion.identity;
	
		protected override void OnEnable()
		{
			base.OnEnable();
			GazeRotationAction.performed += OnTrackEyeRotation;
			GazeRotationAction.canceled += OnTrackEyeRotation;
			GazePositionAction.performed += OnTrackEyePosition;
			GazePositionAction.canceled += OnTrackEyePosition;
			GazeTrackedAction.performed += OnTrackingStateChange;
			GazeTrackedAction.canceled += OnTrackingStateChange;
			GazePositionAction.Enable();
			GazeRotationAction.Enable();
			GazeTrackedAction.Enable();
		}
		

		protected override void OnDisable()
		{
			base.OnDisable();
			GazePositionAction.Disable();
			GazeRotationAction.Disable();
			GazeTrackedAction.Disable();
			GazeRotationAction.performed -= OnTrackEyeRotation;
			GazeRotationAction.canceled -= OnTrackEyeRotation;
			GazePositionAction.performed -= OnTrackEyePosition;
			GazePositionAction.canceled -= OnTrackEyePosition;
			GazeTrackedAction.performed -= OnTrackingStateChange;
			GazeTrackedAction.canceled -= OnTrackingStateChange;
			GazePositionAction.Dispose();
			GazeRotationAction.Dispose();
			GazeTrackedAction.Dispose();
		}
		
		private void OnTrackingStateChange(InputAction.CallbackContext ctx)
		{
			if (!ctx.performed)
			{
				TrackingState = XRDeviceTrackingState.None;
				return;
			}

			TrackingState = (XRDeviceTrackingState)ctx.ReadValue<int>();
		}
		
		private void OnTrackEyePosition(InputAction.CallbackContext ctx)
		{
			if (ctx.performed) GazePosition = ctx.ReadValue<Vector3>();
			else GazePosition = Vector3.zero;
		}
		
		private void OnTrackEyeRotation(InputAction.CallbackContext ctx)
		{
			if (ctx.performed) GazeRotation = ctx.ReadValue<Quaternion>();
			else transform.localRotation = Quaternion.identity;
		}
	}
}