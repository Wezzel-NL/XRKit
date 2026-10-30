using System;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.SpatialEntities;

namespace Editor
{
	public static class XRKitSetup
	{
		public static void Execute()
		{
			var settings = XRKitSettingsProvider.GetOrCreate();
			XRSettingsUtility.SetInitOnStartup(BuildTargetGroup.Standalone, false);
			XRSettingsUtility.SetInitOnStartup(BuildTargetGroup.Android, false);
			XRSettingsUtility.UseOnlyOpenXR(BuildTargetGroup.Standalone);
			XRSettingsUtility.UseOnlyOpenXR(BuildTargetGroup.Android);
			ConfigureOpenXRDesktop();
			ConfigureOpenXRAndroid();
			settings.SetupCompleted = true;
		}

		private static void ConfigureOpenXRDesktop()
		{
			var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
			if (!settings) return;
			ConfigureOpenXRShared(settings);
			EditorUtility.SetDirty(settings);
		}
		
		private static void ConfigureOpenXRAndroid()
		{
			var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
			if (!settings) return;
			ConfigureOpenXRShared(settings);
			EditorUtility.SetDirty(settings);
		}

		private static void ConfigureOpenXRShared(OpenXRSettings settings)
		{
			if (!settings) return;
			settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
			settings.autoColorSubmissionMode = true;
			settings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
			settings.useOpenXRPredictedTime = true;
			DisableAllFeatures(settings);
			SetFeature("HandTrackingDataSourceFeature",settings, true);
			SetFeature<HandTracking>(settings, true);
			SetFeature<FoveatedRenderingFeature>(settings, true);
			SetFeature<XrPerformanceSettingsFeature>(settings, true);
			SetFeature<ApiLayersFeature>(settings, true);
			SetFeature<EyeGazeInteraction>(settings, true);
			SetFeature<OculusTouchControllerProfile>(settings, true);
			SetFeature<ValveIndexControllerProfile>(settings, true);
			SetFeature<HTCViveControllerProfile>(settings, true);
			SetFeature<MetaQuestTouchPlusControllerProfile>(settings, true);
			SetFeature<HPReverbG2ControllerProfile>(settings, true);
			SetFeature<HandInteractionProfile>(settings, true);
			var anchorFeature = SetFeature<SpatialAnchorFeature>(settings, true);
			if (anchorFeature) anchorFeature.automaticallyLoadSavedAnchors = true;
			var markerFeature = SetFeature<SpatialMarkerFeature>(settings, true);
			if (markerFeature)
			{
				var qr = markerFeature.qrCodeConfigOptions;
				qr.enableCapability = true;
			}
		}

		private static void DisableAllFeatures(OpenXRSettings settings)
		{
			foreach (var feature in settings.GetFeatures())
			{
				if(!feature) continue;
				feature.enabled = false;
			}
		}

		private static void SetFeature(string name, OpenXRSettings settings, bool enabled)
		{
			foreach (var feature in settings.GetFeatures())
			{
				if (!feature) continue; 
				if (!feature.GetType().Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
				feature.enabled = enabled;
			}
		}
		private static T SetFeature<T>(OpenXRSettings settings, bool enabled) where T : OpenXRFeature
		{
			var feat = settings.GetFeature<T>();
			if (!feat) return null;
			feat.enabled = enabled;
			return feat;
		}
	}
}