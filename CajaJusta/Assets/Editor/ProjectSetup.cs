using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;

/// <summary>Deja el proyecto listo para celular: escena principal, vertical, nombre e ID de iOS.</summary>
public static class ProjectSetup
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Shapester/Configurar proyecto")]
    public static void Setup()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.productName = "Shapester";
        PlayerSettings.companyName = "MiJuego";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.mijuego.cajajusta");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.stripEngineCode = false; // la UI se arma por código; no recortar clases del motor
        AssetDatabase.SaveAssets();
    }
}
