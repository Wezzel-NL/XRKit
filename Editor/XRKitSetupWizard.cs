using Editor;
using UnityEditor;
using UnityEngine;

public class XRKitSetupWizard : EditorWindow
{
	private const string LogoPath = "Packages/com.wezzelnl.xrkit/Editor/Assets/WezzelLogo.png";
	private const float LogoHeight = 96;
	private const string WelcomeText = "Welcome to XR Kit!\nXR Kit is a universal cross-platform SDK for VR and MR headsets. This automatic setup wizard will help you get started by configuring your Unity project. This setup wizard will configure your unity project to work properly with XRKit.\nTo get started press continue!";
	private const string WarningText = "Pressing Continue will permanently alter your project settings without undo, make sure to make a backup before continuing!";
	private static readonly Vector2 WindowSize = new Vector2(480, 320);

	private Texture2D logo;
	private GUIStyle containerStyle;
	private GUIStyle bodyStyle;
	private GUIStyle warningStyle;

	[MenuItem("Window/XR Kit/Setup Wizard")]
	public static void Open()
	{
		var window = GetWindow<XRKitSetupWizard>(true, "XR Kit Setup", true);
		window.minSize = WindowSize;
		window.maxSize = WindowSize;

		var main = EditorGUIUtility.GetMainWindowPosition();
		window.position = new Rect(main.center - WindowSize / 2, WindowSize);
	}

	private void OnGUI()
	{
		if (!logo) logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
		containerStyle ??= new GUIStyle { padding = new RectOffset(14, 14, 14, 14) };
		bodyStyle ??= new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 12 };
		warningStyle ??= new GUIStyle(EditorStyles.helpBox) { wordWrap = true, fontSize = 13, fontStyle = FontStyle.Bold, padding = new RectOffset(10, 10, 10, 10) };

		using (new EditorGUILayout.VerticalScope(containerStyle))
		{
			if (logo) DrawLogo();
			GUILayout.Label(WelcomeText, bodyStyle);
			GUILayout.Space(12);
			GUILayout.Label(new GUIContent(WarningText, EditorGUIUtility.IconContent("console.warnicon").image), warningStyle);
			GUILayout.FlexibleSpace();

			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Cancel", GUILayout.Width(100), GUILayout.Height(28))) Cancel();
				if (GUILayout.Button("Continue", GUILayout.Width(100), GUILayout.Height(28))) Continue();
			}
		}
	}

	private void DrawLogo()
	{
		var rect = GUILayoutUtility.GetRect(0, LogoHeight, GUILayout.ExpandWidth(true));
		GUI.DrawTexture(rect, logo, ScaleMode.ScaleToFit);
		GUILayout.Space(12);
	}

	private void Cancel()
	{
		Close();
		GUIUtility.ExitGUI();
	}

	private void Continue()
	{
		XRKitSetup.Execute();
		Close();
		GUIUtility.ExitGUI();
	}
}