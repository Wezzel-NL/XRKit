using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using WezzelNL.XRKit.Runtime.Data;

namespace WezzelNL.XRKit.Runtime.Rig.Devices
{
	[AddComponentMenu("WezzelNL/XR/XRHeadset")]
	[RequireComponent(typeof(Camera))]
	public class XRHeadset : XRDevice
	{
		[field: SerializeField, Range(0f,1f)] public float FoveatedRenderingLevel { get; private set; } = 1.0f;
		
		public InputAction HeadsetPositionAction { get; } = new("Headset Position", InputActionType.Value, "<XRHMD>/centerEyePosition");
		public InputAction HeadsetRotationAction { get; } = new("Headset Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation");
		public InputAction HeadsetTrackingStateAction { get; } = new("Headset Tracking State", InputActionType.Value, "<XRHMD>/trackingState");
		
		protected override void OnEnable()
		{
			base.OnEnable();
			HeadsetPositionAction.performed += OnTrackPosition;
			HeadsetPositionAction.canceled += OnTrackPosition;
			HeadsetRotationAction.performed += OnTrackRotation;
			HeadsetRotationAction.canceled += OnTrackRotation;
			HeadsetTrackingStateAction.performed += OnTrackingStateChange;
			HeadsetTrackingStateAction.canceled += OnTrackingStateChange;
			HeadsetPositionAction.Enable();
			HeadsetRotationAction.Enable();
			HeadsetTrackingStateAction.Enable();
		}
		
		protected override void OnDisable()
		{
			base.OnDisable();
			HeadsetPositionAction.Disable();
			HeadsetRotationAction.Disable();
			HeadsetTrackingStateAction.Disable();
			HeadsetPositionAction.performed -= OnTrackPosition;
			HeadsetPositionAction.canceled -= OnTrackPosition;
			HeadsetRotationAction.performed -= OnTrackRotation;
			HeadsetRotationAction.canceled -= OnTrackRotation;
			HeadsetTrackingStateAction.performed -= OnTrackingStateChange;
			HeadsetTrackingStateAction.canceled -= OnTrackingStateChange;
			HeadsetPositionAction.Dispose();
			HeadsetRotationAction.Dispose();
			HeadsetTrackingStateAction.Dispose();
		}
		
		private void Start()
		{
			SetupFoveatedRendering();
		}

		private void SetupFoveatedRendering()
		{
			var xrDisplaySubsystem = default(XRDisplaySubsystem);
			var xrDisplaySubsystems = new List<XRDisplaySubsystem>();
			SubsystemManager.GetSubsystems(xrDisplaySubsystems);
    
			if (xrDisplaySubsystems.Count < 1)
			{
				Debug.LogError("No XR display subsystems found.");
				return;
			}
			
			foreach(var subsystem in xrDisplaySubsystems)
			{
				if (!subsystem.running) continue;
				xrDisplaySubsystem = subsystem;
				break;
			}

			if (xrDisplaySubsystem == null) return;
			
			xrDisplaySubsystem.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
			xrDisplaySubsystem.foveatedRenderingLevel = FoveatedRenderingLevel;
		}

		
		private void OnTrackPosition(InputAction.CallbackContext ctx)
		{
			if (ctx.performed)
			{
				transform.localPosition = ctx.ReadValue<Vector3>();
			} else transform.localPosition = Vector3.zero;
		}
		
		private void OnTrackRotation(InputAction.CallbackContext ctx)
		{
			if (ctx.performed)
			{
				transform.localRotation = ctx.ReadValue<Quaternion>();
			} else transform.localRotation = Quaternion.identity;
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
	}
}