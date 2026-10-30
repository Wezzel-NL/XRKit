using UnityEditor;
using UnityEngine;

namespace Editor
{
	[InitializeOnLoad]
	public class XRKitSetupWizardStarter
	{
		static XRKitSetupWizardStarter()
		{
			var settings = XRKitSettingsProvider.GetOrCreate();
			if (!settings) return;
			if (!settings.SetupCompleted) XRKitSetupWizard.Open();
		}
	}
}