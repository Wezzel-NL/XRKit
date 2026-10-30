using UnityEditor;
using UnityEngine;
using WezzelNL.XRKit.Runtime;

public class XRKitSettingsProvider : AssetPostprocessor
{
	private const string DefaultFolder = "Assets/XR";
	private const string DefaultPath = DefaultFolder + "/XRKitSettings.asset";

	private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
	{
		if (!didDomainReload && deletedAssets.Length == 0) return;

		EditorApplication.delayCall -= Ensure;
		EditorApplication.delayCall += Ensure;
	}

	private static void Ensure() => GetOrCreate();

	public static XRKitSettings GetOrCreate()
	{
		var guids = AssetDatabase.FindAssets($"t:{nameof(XRKitSettings)}");
		if (guids.Length > 1) Debug.LogWarning($"Multiple {nameof(XRKitSettings)} assets found, using {AssetDatabase.GUIDToAssetPath(guids[0])}");

		var settings = guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<XRKitSettings>(AssetDatabase.GUIDToAssetPath(guids[0])) : Create();
		RegisterPreloaded(settings);
		return settings;
	}

	private static XRKitSettings Create()
	{
		if (!AssetDatabase.IsValidFolder(DefaultFolder)) AssetDatabase.CreateFolder("Assets", "XR");

		var settings = ScriptableObject.CreateInstance<XRKitSettings>();
		AssetDatabase.CreateAsset(settings, DefaultPath);
		AssetDatabase.SaveAssets();
		return settings;
	}

	private static void RegisterPreloaded(XRKitSettings settings)
	{
		var preloaded = PlayerSettings.GetPreloadedAssets();
		if (ArrayUtility.Contains(preloaded, settings)) return;

		ArrayUtility.Add(ref preloaded, settings);
		PlayerSettings.SetPreloadedAssets(preloaded);
	}
}