using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Menú "Shapester > Build Web": genera la versión web en Builds/Web y la deja lista para
/// instalarse en el iPhone con "Compartir > Agregar a pantalla de inicio" (abre sin barras
/// de Safari, con ícono y nombre propios).
/// </summary>
public static class WebBuild
{
    const string OutDir = "Builds/Web";

    [MenuItem("Shapester/Build Web")]
    public static void Build()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled; // works with any server
        PlayerSettings.productName = "Shapester";
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity" }, OutDir, BuildTarget.WebGL, BuildOptions.None);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) return;
        MakeAppLike();
        Debug.Log("Build web listo en " + Path.GetFullPath(OutDir));
    }

    static void MakeAppLike()
    {
        File.WriteAllBytes(Path.Combine(OutDir, "icon.png"), MakeIcon(180));
        File.WriteAllText(Path.Combine(OutDir, "manifest.json"),
@"{
  ""name"": ""Shapester"",
  ""short_name"": ""Shapester"",
  ""display"": ""fullscreen"",
  ""orientation"": ""portrait"",
  ""background_color"": ""#12141f"",
  ""theme_color"": ""#12141f"",
  ""icons"": [{ ""src"": ""icon.png"", ""sizes"": ""180x180"", ""type"": ""image/png"" }]
}");

        var indexPath = Path.Combine(OutDir, "index.html");
        var html = File.ReadAllText(indexPath);
        const string extra =
@"
    <meta name=""apple-mobile-web-app-capable"" content=""yes"">
    <meta name=""mobile-web-app-capable"" content=""yes"">
    <meta name=""apple-mobile-web-app-status-bar-style"" content=""black"">
    <meta name=""apple-mobile-web-app-title"" content=""Shapester"">
    <meta name=""theme-color"" content=""#12141f"">
    <link rel=""apple-touch-icon"" href=""icon.png"">
    <link rel=""manifest"" href=""manifest.json"">
    <style>
      html, body { background: #12141f; overscroll-behavior: none; touch-action: none;
                   -webkit-user-select: none; user-select: none; -webkit-touch-callout: none; }
      #unity-canvas { touch-action: none; }
    </style>";
        if (!html.Contains("apple-mobile-web-app-capable"))
        {
            int i = html.IndexOf("<head>");
            if (i >= 0) html = html.Insert(i + "<head>".Length, extra);
            File.WriteAllText(indexPath, html);
        }
    }

    /// <summary>Ícono simple: fondo oscuro, recuadro amarillo y tres figuras de colores.</summary>
    public static byte[] MakeIcon(int s)
    {
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var bg = new Color32(18, 20, 31, 255);
        var yellow = new Color32(255, 217, 64, 255);
        var c1 = new Color32(110, 200, 255, 255);
        var c2 = new Color32(255, 120, 170, 255);
        var c3 = new Color32(120, 230, 140, 255);
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = x / (float)s, v = y / (float)s;
                var col = bg;
                bool inBox = u > 0.18f && u < 0.82f && v > 0.18f && v < 0.82f;
                bool inner = u > 0.215f && u < 0.785f && v > 0.215f && v < 0.785f;
                if (inBox && !inner) col = yellow;
                if (u > 0.25f && u < 0.5f && v > 0.25f && v < 0.5f) col = c1;                       // cuadrado
                if (v > 0.25f && v < 0.5f && u > 0.52f && u < 0.75f && (u - 0.52f) < (0.5f - v) * 0.92f) col = c2; // triángulo
                if (new Vector2(u - 0.5f, v - 0.63f).magnitude < 0.12f) col = c3;                   // círculo
                px[y * s + x] = col;
            }
        tex.SetPixels32(px);
        tex.Apply();
        var png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        return png;
    }
}
