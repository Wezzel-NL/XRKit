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

		public void Initialize(Vector3 position) => Initialize(null, Guid.NewGuid(), position);

		public void Initialize(Guid id, Vector3 position) => Initialize(null, id, position);

		public void Initialize(XRAnchorTracker tracker, Vector3 position) => Initialize(tracker, Guid.NewGuid(), position);

		public void Initialize(XRAnchorTracker tracker, Guid id, Vector3 position)
		{
			Tracker = tracker;
			ID = id;
			transform.position = position;
		}

		private void Update()
		{
			if (!transform.hasChanged) return;
			transform.hasChanged = false;
			if(Tracker) Tracker.MoveAnchor(this, Position);
		}
	}
}