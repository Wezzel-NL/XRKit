using System;
using UnityEngine;
using WezzelNL.XRKit.Runtime.Rig.Devices;

namespace Data
{
	public class XRAnchor : XRTracker
	{
		public Guid ID { get; private set; }
		public XRAnchorTracker Tracker { get; private set; }
		public Vector3 Position => transform.position;
		public Quaternion Rotation => transform.rotation;
		public Pose Pose => new(Position, Rotation);
		private const float PositionTolerance = 0.001f;
		private const float RotationTolerance = 0.1f;
		private Vector3 appliedLocalPosition;
		private Quaternion appliedLocalRotation = Quaternion.identity;

		public void Initialize(Vector3 position) => Initialize(null, Guid.NewGuid(), position, Quaternion.identity);

		public void Initialize(Guid id, Vector3 position) => Initialize(null, id, position, Quaternion.identity);

		public void Initialize(XRAnchorTracker tracker, Vector3 position, Quaternion rotation) => Initialize(tracker, Guid.NewGuid(), position, rotation);

		public void Initialize(XRAnchorTracker tracker, Guid id, Vector3 position, Quaternion rotation)
		{
			Tracker = tracker;
			ID = id;
			ApplyTrackedPose(position, rotation);
		}

		public void ApplyTrackedPose(Vector3 position, Quaternion rotation)
		{
			transform.SetPositionAndRotation(position, rotation);
			appliedLocalPosition = transform.localPosition;
			appliedLocalRotation = transform.localRotation;
		}

		private void Update()
		{
			if (!Tracker) return;

			var moved = (transform.localPosition - appliedLocalPosition).sqrMagnitude > PositionTolerance * PositionTolerance;
			var rotated = Quaternion.Angle(transform.localRotation, appliedLocalRotation) > RotationTolerance;
			if (!moved && !rotated) return;

			Tracker.MoveAnchor(this, Position, Rotation);
		}
	}
}