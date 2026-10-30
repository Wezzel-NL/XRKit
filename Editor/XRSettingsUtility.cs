using System;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

public static class XRSettingsUtility
{
	public static XRManagerSettings GetManager(BuildTargetGroup group)
	{
		var generalSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
		return generalSettings ? generalSettings.Manager : null;
	}

	private const string OpenXRLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";

	public static void UseOnlyOpenXR(BuildTargetGroup group)
	{
		var manager = GetOrCreateManager(group);
		var loaders = manager.activeLoaders;

		for (var i = loaders.Count - 1; i >= 0; i--)
		{
			var loader = loaders[i];
			if (!loader) manager.TryRemoveLoader(loader);
			else if (loader.GetType().FullName != OpenXRLoaderTypeName) XRPackageMetadataStore.RemoveLoader(manager, loader.GetType().FullName, group);
		}

		if (!XRPackageMetadataStore.AssignLoader(manager, OpenXRLoaderTypeName, group)) throw new InvalidOperationException($"Could not assign OpenXR loader for {group}");

		EditorUtility.SetDirty(manager);
		AssetDatabase.SaveAssets();
	}

	private static XRManagerSettings GetOrCreateManager(BuildTargetGroup group)
	{
#pragma warning disable CS0618 // Type or member is obsolete
		if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perBuildTarget)) throw new InvalidOperationException("No XR General Settings asset, open Project Settings > XR Plug-in Management once");
#pragma warning restore CS0618 // Type or member is obsolete

		if (!perBuildTarget.HasSettingsForBuildTarget(group)) perBuildTarget.CreateDefaultSettingsForBuildTarget(group);
		if (!perBuildTarget.HasManagerSettingsForBuildTarget(group)) perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(group);

		EditorUtility.SetDirty(perBuildTarget);
		return perBuildTarget.ManagerSettingsForBuildTarget(group);
	}

	public static void SetInitOnStartup(BuildTargetGroup group, bool initOnStartup)
	{
		var generalSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
		if (!generalSettings) return;

		generalSettings.InitManagerOnStart = initOnStartup;
		EditorUtility.SetDirty(generalSettings);
		AssetDatabase.SaveAssets();
	}
}