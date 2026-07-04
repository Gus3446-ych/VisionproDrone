#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Callbacks;

public static class VisionOSBuildPostprocessor
{
	[PostProcessBuild(1000)]
	public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
	{
		if (target.ToString() != "VisionOS" && target != BuildTarget.iOS)
			return;

		var plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
		if (!File.Exists(plistPath))
			return;

		SetPlistString(
			plistPath,
			"NSLocalNetworkUsageDescription",
			"This app connects to a DJI Tello drone on the local Wi-Fi network for control and live video.");
	}

	private static void SetPlistString(string plistPath, string key, string value)
	{
		var document = XDocument.Load(plistPath, LoadOptions.PreserveWhitespace);
		var dict = document.Root?.Element("dict");
		if (dict == null)
			return;

		var existingKey = dict.Elements("key").FirstOrDefault(element => element.Value == key);
		if (existingKey != null) {
			var existingValue = existingKey.ElementsAfterSelf().FirstOrDefault();
			if (existingValue != null)
				existingValue.ReplaceWith(new XElement("string", value));
			else
				existingKey.AddAfterSelf(new XElement("string", value));
		} else {
			dict.Add(new XElement("key", key));
			dict.Add(new XElement("string", value));
		}

		document.Save(plistPath);
	}
}
#endif
