using System;
using UnityEngine;
using UnityEngine.InputSystem;
using WezzelNL.XRKit.Runtime.Data;

namespace WezzelNL.XRKit.Runtime.Rig.Devices
{
	public enum XRInputActionTrackerMode
	{
		Local,
		Rig,
		World
	}
	public class XRInputActionTracker : XRTracker
	{
		[field: SerializeField] public XRInputActionTrackerMode Mode { get; private set; } = XRInputActionTrackerMode.Local;
		[field: SerializeField] public InputAction PositionAction { get; private set; }
		[field: SerializeField] public InputAction RotationAction { get; private set; }
		[field: SerializeField] public InputAction TrackingStateAction { get; private set; }

		protected override void OnEnable()
		{
			base.OnEnable();
			
			PositionAction.performed += OnReceivePosition;
			RotationAction.performed += OnReceiveRotation;
			TrackingStateAction.performed += OnReceiveTrackingState;
			
			PositionAction.Enable();
			RotationAction.Enable();
			TrackingStateAction.Enable();
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			
			PositionAction.performed -= OnReceivePosition;
			RotationAction.performed -= OnReceiveRotation;
			TrackingStateAction.performed -= OnReceiveTrackingState;
			
			PositionAction.Disable();
			RotationAction.Disable();
			TrackingStateAction.Disable();
		}
		
		private void OnReceivePosition(InputAction.CallbackContext ctx)
		{
			if (!ctx.performed) return;
			var pos = ctx.ReadValue<Vector3>();
			switch (Mode)
			{
				case XRInputActionTrackerMode.Local: transform.localPosition = pos; break;
				case XRInputActionTrackerMode.Rig: transform.position = Rig.transform.TransformPoint(pos); break;
				case XRInputActionTrackerMode.World: transform.position = pos; break;
				default: throw new ArgumentOutOfRangeException();
			}
		}
		
		private void OnReceiveRotation(InputAction.CallbackContext ctx)
		{
			if (!ctx.performed) return;
			var rot = ctx.ReadValue<Quaternion>();
			switch (Mode)
			{
				case XRInputActionTrackerMode.Local: transform.localRotation = rot; break;
				case XRInputActionTrackerMode.Rig: transform.rotation = Rig.transform.rotation * rot; break;
				case XRInputActionTrackerMode.World: transform.rotation = rot; break;
				default: throw new ArgumentOutOfRangeException();
			}
		}
		
		private void OnReceiveTrackingState(InputAction.CallbackContext ctx)
		{
			if(!ctx.performed)
			{
				TrackingState = XRDeviceTrackingState.None;
				return;
			}

			TrackingState = (XRDeviceTrackingState)ctx.ReadValue<int>();
		}
	}
}