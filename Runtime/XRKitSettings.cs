using System;
using UnityEngine;

namespace WezzelNL.XRKit.Runtime
{
	public class XRKitSettings : ScriptableObject
	{
		[field: SerializeField] public bool SetupCompleted { get; set; } = false;
		
		
		public static XRKitSettings Current { get; private set; }
		private void OnEnable() => Current = this;
	}
}