using UnityEngine;
using UnityEngine.InputSystem;
using WezzelNL.XRKit.Runtime.Data;

namespace WezzelNL.XRKit.Runtime.Rig.Devices
{
	[AddComponentMenu("WezzelNL/XR/XRController")]
	public class XRController : XRDevice
	{
		[field: SerializeField] public XRHand Hand { get; private set; }
		
		public InputAction PositionAction { get; private set; }
		public InputAction RotationAction { get; private set; }
		public InputAction IsTrackedAction { get; private set; }

		protected override void OnEnable()
		{
			base.OnEnable();
			var basePath = $"<XRController>{{{Hand.ToString()}}}";
			PositionAction = new("ControllerPosition", InputActionType.Value, $"{basePath}/devicePosition");
			RotationAction = new("ControllerRotation", InputActionType.Value, $"{basePath}/deviceRotation");
			IsTrackedAction = new("IsTracked", InputActionType.Value, $"{basePath}/isTracked");
			PositionAction.performed += OnPositionTracked;
			PositionAction.canceled += OnPositionTracked;
			RotationAction.performed += OnRotationTracked;
			RotationAction.canceled += OnRotationTracked;
			IsTrackedAction.performed += OnTrackingStateChange;
			IsTrackedAction.canceled += OnTrackingStateChange;
			PositionAction.Enable();
			RotationAction.Enable();
			IsTrackedAction.Enable();
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			PositionAction.Disable();
			RotationAction.Disable();
			IsTrackedAction.Disable();
			PositionAction.performed -= OnPositionTracked;
			PositionAction.canceled -= OnPositionTracked;
			RotationAction.performed -= OnRotationTracked;
			RotationAction.canceled -= OnRotationTracked;
			IsTrackedAction.performed -= OnTrackingStateChange;
			IsTrackedAction.canceled -= OnTrackingStateChange;
			PositionAction.Dispose();
			RotationAction.Dispose();
			IsTrackedAction.Dispose();
			PositionAction = null;
			RotationAction = null;
			IsTrackedAction = null;
		}
		
		private void OnPositionTracked(InputAction.CallbackContext ctx)
		{
			if (ctx.performed)
			{
				transform.localPosition = ctx.ReadValue<Vector3>();
			} else transform.localPosition = Vector3.zero;
		}
		
		private void OnRotationTracked(InputAction.CallbackContext ctx)
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