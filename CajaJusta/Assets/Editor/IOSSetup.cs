using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif

/// <summary>
/// Menu "Shapester > Configure iOS": leaves the project ready for an App Store build
/// (bundle ID, version, orientation, minimum iOS, full-screen, 1024px app icon).
/// Also adds the export-compliance flag to Info.plist on every iOS build, so App Store
/// Connect doesn't ask about encryption each time (the game only uses standard HTTPS/WSS).
/// </summary>
public static class IOSSetup
{
    // Change BEFORE the first upload to Apple: it can't be changed afterwards.
    public const string BundleId = "com.aferrante.shapester";
    public const string Version = "1.0";
    public const string BuildNumber = "1";
    const string IconPath = "Assets/Icons/AppIcon.png";

    [MenuItem("Shapester/Configure iOS")]
    public static void Configure()
    {
        PlayerSettings.productName = "Shapester";
        PlayerSettings.companyName = "Shapester";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
        PlayerSettings.bundleVersion = Version;
        PlayerSettings.iOS.buildNumber = BuildNumber;
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;   // iPhone only: no iPad screenshots needed (iPad can still run it in compatibility mode)
        PlayerSettings.iOS.requiresFullScreen = true;
        PlayerSettings.statusBarHidden = true;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.iOS.appleEnableAutomaticSigning = false;   // the cloud build signs with your certificate + profile
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1); // ARM64

        CreateIcon();
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

        AssetDatabase.SaveAssets();
        Debug.Log("SHAPESTER iOS configured: " + BundleId + " v" + Version + " (" + BuildNumber + ")");
    }

    /// <summary>1024x1024 opaque icon (Apple requires no transparency), chunky pixels for the retro look.</summary>
    static void CreateIcon()
    {
        const int low = 64, size = 1024;
        var small = new Texture2D(2, 2);
        small.LoadImage(WebBuild.MakeIcon(low));
        var big = new Texture2D(size, size, TextureFormat.RGB24, false);
        var px = new Color32[size * size];
        var src = small.GetPixels32();
        int k = size / low;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var c = src[(y / k) * low + (x / k)];
                c.a = 255;
                px[y * size + x] = c;
            }
        big.SetPixels32(px);
        big.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
        File.WriteAllBytes(IconPath, big.EncodeToPNG());
        Object.DestroyImmediate(small);
        Object.DestroyImmediate(big);

        AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Point;
        importer.SaveAndReimport();
    }

#if UNITY_IOS
    [PostProcessBuild]
    public static void AddExportCompliance(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        var plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
        plist.WriteToFile(plistPath);
    }
#endif
}
