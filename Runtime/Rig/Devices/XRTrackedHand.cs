using System;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.Hands;
using WezzelNL.XRKit.Runtime.Data;
using XRHand = WezzelNL.XRKit.Runtime.Data.XRHand;

namespace WezzelNL.XRKit.Runtime.Rig.Devices
{
	[BurstCompile]
	[AddComponentMenu("WezzelNL/XR/XRTrackedHand")]
	public class XRTrackedHand : XRDevice
	{
		[field: SerializeField] public XRHand Hand { get; private set; }
		[field: SerializeField] public Transform RootBone { get; private set; }
		[SerializeField, HideInInspector] private Transform[] jointBones = new Transform[XRHandJointID.EndMarker.ToIndex()];

		public XRHandTracker Tracker { get; private set; }
		public Pose RootPose { get; private set; } = Pose.identity;

		private NativeArray<XRHandJoint> joints;
		private NativeArray<Pose> jointLocalPoses;
		private NativeArray<float> boneParentScales;
		private bool[] hasJointBone;

		protected override void OnEnable()
		{
			base.OnEnable();
			AllocateJointData();
			Tracker = GetComponentInParent<XRHandTracker>();
			if (Tracker) Tracker.AddHand(this);
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			if (Tracker)
			{
				Tracker.RemoveHand(this);
				Tracker = null;
			}
			DisposeJointData();
		}

		public void ApplyTrackingState(XRDeviceTrackingState state) => TrackingState = state;

		public void ApplyRootPose(Pose pose)
		{
			RootPose = pose;
			transform.SetLocalPositionAndRotation(pose.position, pose.rotation);
		}

		public void ApplyJoints(UnityEngine.XR.Hands.XRHand handData)
		{
			if (!joints.IsCreated) return;

			for (var i = 0; i < joints.Length; i++) joints[i] = handData.GetJoint(XRHandJointIDUtility.FromIndex(i));
			CalculateJointLocalPoses(RootPose, ref joints, ref boneParentScales, ref jointLocalPoses);

			for (var i = 0; i < jointLocalPoses.Length; i++)
			{
				if (!hasJointBone[i]) continue;
				var pose = jointLocalPoses[i];
				jointBones[i].SetLocalPositionAndRotation(pose.position, pose.rotation);
			}
		}

		private void AllocateJointData()
		{
			var jointCount = XRHandJointID.EndMarker.ToIndex();
			joints = new NativeArray<XRHandJoint>(jointCount, Allocator.Persistent);
			jointLocalPoses = new NativeArray<Pose>(jointCount, Allocator.Persistent);
			boneParentScales = new NativeArray<float>(jointCount, Allocator.Persistent);
			hasJointBone = new bool[jointCount];

			var handScale = transform.lossyScale.x;
			for (var i = 0; i < jointCount; i++)
			{
				var bone = i < jointBones.Length ? jointBones[i] : null;
				hasJointBone[i] = bone;
				boneParentScales[i] = bone ? bone.parent.lossyScale.x / handScale : 1f;
			}
		}

		private void DisposeJointData()
		{
			if (joints.IsCreated) joints.Dispose();
			if (jointLocalPoses.IsCreated) jointLocalPoses.Dispose();
			if (boneParentScales.IsCreated) boneParentScales.Dispose();
		}

		[BurstCompile]
		private static void CalculateJointLocalPoses(in Pose rootPose, ref NativeArray<XRHandJoint> joints, ref NativeArray<float> parentScales, ref NativeArray<Pose> localPoses)
		{
			var wristIndex = XRHandJointID.Wrist.ToIndex();
			if (!TryGetBonePose(joints[wristIndex], out var wristPose)) return;
			localPoses[wristIndex] = ToLocalPose(rootPose, wristPose, parentScales[wristIndex]);

			var palmIndex = XRHandJointID.Palm.ToIndex();
			if (TryGetBonePose(joints[palmIndex], out var palmPose)) localPoses[palmIndex] = ToLocalPose(wristPose, palmPose, parentScales[palmIndex]);

			for (var finger = (int)XRHandFingerID.Thumb; finger <= (int)XRHandFingerID.Little; finger++)
			{
				var fingerId = (XRHandFingerID)finger;
				var parentPose = wristPose;
				var backIndex = fingerId.GetBackJointID().ToIndex();
				for (var i = fingerId.GetFrontJointID().ToIndex(); i <= backIndex; i++)
				{
					if (!TryGetBonePose(joints[i], out var jointPose)) continue;
					localPoses[i] = ToLocalPose(parentPose, jointPose, parentScales[i]);
					parentPose = jointPose;
				}
			}
		}

		private static bool TryGetBonePose(in XRHandJoint joint, out Pose pose)
		{
			if (!joint.TryGetPose(out pose)) return false;
			pose.rotation *= new Quaternion(0f, 1f, 0f, 0f);
			return true;
		}

		private static Pose ToLocalPose(in Pose parentPose, in Pose pose, float parentScale)
		{
			var inverseParentRotation = Quaternion.Inverse(parentPose.rotation);
			return new Pose(inverseParentRotation * (pose.position - parentPose.position) / parentScale, inverseParentRotation * pose.rotation);
		}

#if UNITY_EDITOR
		private void OnValidate()
		{
			jointBones = FindJointBones(RootBone);
		}

		private static Transform[] FindJointBones(Transform rootBone)
		{
			var bones = new Transform[XRHandJointID.EndMarker.ToIndex()];
			if (!rootBone) return bones;

			foreach (var bone in rootBone.GetComponentsInChildren<Transform>(true))
			{
				for (var i = 0; i < bones.Length; i++)
				{
					if (!bone.name.EndsWith(XRHandJointIDUtility.FromIndex(i).ToString(), StringComparison.OrdinalIgnoreCase)) continue;
					bones[i] = bone;
					break;
				}
			}
			return bones;
		}
#endif
	}
}