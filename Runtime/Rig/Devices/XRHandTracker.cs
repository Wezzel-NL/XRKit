using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using WezzelNL.XRKit.Runtime.Data;
using XRHand = WezzelNL.XRKit.Runtime.Data.XRHand;

namespace WezzelNL.XRKit.Runtime.Rig.Devices
{
	[AddComponentMenu("WezzelNL/XR/XRHandTracker")]
	public class XRHandTracker : XRDevice
	{
		public XRTrackedHand LeftHand { get; private set; }
		public XRTrackedHand RightHand { get; private set; }

		public XRHandSubsystem SubSystem { get; private set; }
		public UnityEngine.XR.Hands.XRHand LeftHandData { get; private set; }
		public UnityEngine.XR.Hands.XRHand RightHandData { get; private set; }
		
		public void AddHand(XRTrackedHand hand)
		{
			if(!hand) throw new ArgumentNullException(nameof(hand));
			switch (hand.Hand)
			{
				case XRHand.LeftHand:
					if(LeftHand) throw new InvalidOperationException("Hand tracker can only output to one tracked hand (left)");
					LeftHand = hand;
					InitialSetTrackingState(SubSystem, LeftHand);
					break;
				case XRHand.RightHand:
					if(RightHand) throw new InvalidOperationException("Hand tracker can only output to one tracked hand (right)");
					RightHand = hand;
					InitialSetTrackingState(SubSystem, RightHand);
					break;
				default: throw new ArgumentOutOfRangeException();
			}
		}

		public void RemoveHand(XRTrackedHand hand)
		{
			if (!hand) throw new ArgumentNullException(nameof(hand));
			switch (hand.Hand)
			{
				case XRHand.LeftHand:
					if(LeftHand == hand) LeftHand = null;
					break;
				case XRHand.RightHand:
					if(RightHand == hand) RightHand = null;
					break;
				default: throw new ArgumentOutOfRangeException();
			}
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			if (SubSystem != null) DisconnectSubsystem();
		}

		private void Update()
		{
			UpdateSubsystem();
		}

		private static readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
		private void UpdateSubsystem()
		{
			if (SubSystem != null && SubSystem.running) return;

			SubsystemManager.GetSubsystems(handSubsystems);
			foreach (var subsystem in handSubsystems)
			{
				if (!subsystem.running) continue;
				SetSubSystem(subsystem);
				return;
			}
		}

		private void SetSubSystem(XRHandSubsystem subsystem)
		{
			if(SubSystem != null) DisconnectSubsystem();
			SubSystem = subsystem;
			ConnectSubsystem();
		}

		private void ConnectSubsystem()
		{
			LeftHandData = SubSystem.leftHand;
			RightHandData = SubSystem.rightHand;

			SubSystem.trackingAcquired += OnTrackingAcquired;
			SubSystem.trackingLost += OnTrackingLost;
			SubSystem.updatedHands += OnUpdateHands;

			InitialSetTrackingState(SubSystem, LeftHand);
			InitialSetTrackingState(SubSystem, RightHand);
		}

		private void DisconnectSubsystem()
		{
			LeftHandData = default;
			RightHandData = default;
			
			SubSystem.trackingAcquired -= OnTrackingAcquired;
			SubSystem.trackingLost -= OnTrackingLost;
			SubSystem.updatedHands -= OnUpdateHands;
			
			if(LeftHand) LeftHand.ApplyTrackingState(XRDeviceTrackingState.None);
			if(RightHand) RightHand.ApplyTrackingState(XRDeviceTrackingState.None);
			SubSystem = null;
		}
		
		private void InitialSetTrackingState(XRHandSubsystem subsystem, XRTrackedHand hand)
		{
			if(subsystem == null) return;
			if(!hand) return;
			switch (hand.Hand)
			{
				case XRHand.LeftHand:
					hand.ApplyTrackingState(subsystem.leftHand.isTracked ? XRDeviceTrackingState.Position | XRDeviceTrackingState.Rotation : XRDeviceTrackingState.None);
					break;
				case XRHand.RightHand:
					hand.ApplyTrackingState(subsystem.rightHand.isTracked ? XRDeviceTrackingState.Position | XRDeviceTrackingState.Rotation : XRDeviceTrackingState.None);
					break;
				default: throw new ArgumentOutOfRangeException();
			}
		}

		private void OnTrackingAcquired(UnityEngine.XR.Hands.XRHand hand)
		{
			switch (hand.handedness)
			{
				case Handedness.Invalid: break;
				case Handedness.Left:
					if (LeftHand) LeftHand.ApplyTrackingState(XRDeviceTrackingState.Position | XRDeviceTrackingState.Rotation);
					break;
				case Handedness.Right:
					if (RightHand) RightHand.ApplyTrackingState(XRDeviceTrackingState.Position | XRDeviceTrackingState.Rotation);
					break;
				default: throw new ArgumentOutOfRangeException();
			}
		}

		private void OnTrackingLost(UnityEngine.XR.Hands.XRHand hand)
		{
			switch (hand.handedness)
			{
				case Handedness.Invalid: break;
				case Handedness.Left:
					if (LeftHand) LeftHand.ApplyTrackingState(XRDeviceTrackingState.None);
					break;
				case Handedness.Right:
					if (RightHand) RightHand.ApplyTrackingState(XRDeviceTrackingState.None);
					break;
				default: throw new ArgumentOutOfRangeException();
			}
		}

		private void OnUpdateHands(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags, XRHandSubsystem.UpdateType updateEventType)
		{
			LeftHandData = subsystem.leftHand;
			RightHandData = subsystem.rightHand;

			UpdateHand(LeftHand, LeftHandData, updateSuccessFlags, XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose, XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints);
			UpdateHand(RightHand, RightHandData, updateSuccessFlags, XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose, XRHandSubsystem.UpdateSuccessFlags.RightHandJoints);
		}

		private static void UpdateHand(XRTrackedHand hand, UnityEngine.XR.Hands.XRHand handData, XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags, XRHandSubsystem.UpdateSuccessFlags rootPoseFlag, XRHandSubsystem.UpdateSuccessFlags jointsFlag)
		{
			if (!hand) return;
			if ((updateSuccessFlags & rootPoseFlag) != 0) hand.ApplyRootPose(handData.rootPose);
			if ((updateSuccessFlags & jointsFlag) != 0) hand.ApplyJoints(handData);
		}
	}
}