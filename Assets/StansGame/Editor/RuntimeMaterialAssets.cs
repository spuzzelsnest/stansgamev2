using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Template materials in Resources/RuntimeMaterials for RuntimeMaterials.Unlit / .Lit. Being in Resources, they
    /// (and their shaders) always end up in the built game - Shader.Find alone only finds shaders some scene
    /// material already uses, which is why the tracers threw on every shot in the built demo.
    /// </summary>
    [InitializeOnLoad]
    public static class RuntimeMaterialAssets
    {
        const string Dir = "Assets/StansGame/Resources/RuntimeMaterials";

        static RuntimeMaterialAssets() { EditorApplication.delayCall += Ensure; }

        public static void Ensure()
        {
            if (!Directory.Exists(Dir)) { Directory.CreateDirectory(Dir); AssetDatabase.Refresh(); }
            Make("Unlit", "Universal Render Pipeline/Unlit", "Unlit/Color");
            Make("Lit", "Universal Render Pipeline/Lit", "Standard");
        }

        static void Make(string name, string shader, string fallback)
        {
            string path = $"{Dir}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path)) return;
            var s = Shader.Find(shader) ?? Shader.Find(fallback);
            if (!s) { Debug.LogWarning($"Stan's Game: shader {shader} not found for {path}"); return; }
            AssetDatabase.CreateAsset(new Material(s) { name = name }, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"Stan's Game: made {path}");
        }
    }
}
