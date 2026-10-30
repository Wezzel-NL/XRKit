using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

namespace WezzelNL.XRKit.Runtime
{
	public class XRKit : MonoBehaviour
	{
		public static bool XRActive { get; private set; } = false;
		
		private static XRRuntime runtime;
		
		public static XRRuntime GetRuntime()
		{
			if(!XRActive) throw new InvalidOperationException("XRKit is not active.");
			return runtime;
		}
		
		public IEnumerator Start()
		{
			if(XRActive) throw new InvalidOperationException("XRKit is already active.");
			yield return XRGeneralSettings.Instance.Manager.InitializeLoader();
			runtime = new XRRuntime(XRGeneralSettings.Instance.Manager);	
			if (XRGeneralSettings.Instance.Manager.activeLoader == null) Debug.LogError("Failed to initialize XR Loader, most likely there is no XR device connected.");
			else XRGeneralSettings.Instance.Manager.StartSubsystems();
			XRActive = true;
		}

		public void OnDestroy()
		{
			if(!XRActive) throw new InvalidOperationException("XRKit is not active.");
			runtime = null; 
			if (XRGeneralSettings.Instance.Manager.activeLoader == null) return;
			XRGeneralSettings.Instance.Manager.StopSubsystems();
			XRGeneralSettings.Instance.Manager.DeinitializeLoader();
			XRActive = false;
		}
	}
}