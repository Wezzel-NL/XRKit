using System.Collections.Generic;
using UnityEngine;

namespace WezzelNL.XRKit.Runtime.Rig
{
	[DefaultExecutionOrder(-100)]
	[AddComponentMenu("WezzelNL/XR/XRRig")]
	public class XRRig : MonoBehaviour
	{
		public IEnumerable<XRDevice> Devices => XRDevices;
		private List<XRDevice> XRDevices { get; set; } = new();
	
		public void AddDevice(XRDevice device) => XRDevices.Add(device);
		public void RemoveDevice(XRDevice device) => XRDevices.Remove(device);
	}
}
