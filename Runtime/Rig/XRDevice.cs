using System;
using UnityEngine;
using WezzelNL.XRKit.Runtime.Data;

namespace WezzelNL.XRKit.Runtime.Rig
{
	public abstract class XRDevice : MonoBehaviour
	{
		public XRRig Rig { get; private set; }
		
		private XRDeviceTrackingState trackingState = XRDeviceTrackingState.None;
		public XRDeviceTrackingState TrackingState
		{
			get => trackingState;
			protected set
			{
				var old = trackingState;
				trackingState = value;
				if (old != trackingState) OnTrackingStateChanged?.Invoke(trackingState);
			} 
		}
		public event Action<XRDeviceTrackingState> OnTrackingStateChanged; 

		protected virtual void OnEnable()
		{
			Rig = GetComponentInParent<XRRig>();
			Rig.AddDevice(this);
		}

		protected virtual void OnDisable()
		{
			Rig?.RemoveDevice(this);
			Rig = null;
		}
	}
}