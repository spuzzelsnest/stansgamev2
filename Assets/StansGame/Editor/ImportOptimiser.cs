using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Keeps the texture memory of the models sensible (22 Nov): several downloads came with 4096-pixel sheets and
    /// 25 MB normal maps (the MG 42's) that the game never shows that close. Caps every texture under Models: colour
    /// sheets at 2048, normal maps and the mask sheets (occlusion, roughness, metal) at 1024, weapons' colour sheets at
    /// 2048 (they are seen close up), with mipmaps and normal-quality compression. The files on disk are untouched;
    /// only how Unity imports them changes, so it can be run again at any time.
    /// </summary>
    public static class ImportOptimiser
    {
        const string ModelRoot = "Assets/StansGame/Models";

        [MenuItem("Stan's Game/Setup/Optimise Texture Imports (cap sizes, compress)", priority = 261)]
        public static void Run()
        {
            int changed = 0, seen = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ModelRoot }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("~")) continue;
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                    seen++;
                    string lower = path.ToLowerInvariant();
                    bool normal = ti.textureType == TextureImporterType.NormalMap || lower.Contains("normal") || lower.EndsWith("_n.png")
                               || lower.EndsWith("_n.jpg") || lower.Contains("_nrm") || lower.Contains("_norm");
                    bool mask = lower.Contains("roughness") || lower.Contains("metallic") || lower.Contains("occlusion") || lower.Contains("_ao.");
                    int cap = normal || mask ? 1024 : 2048;
                    bool dirty = false;
                    if (ti.maxTextureSize > cap) { ti.maxTextureSize = cap; dirty = true; }
                    if (ti.textureCompression == TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Compressed; dirty = true; }
                    if (!ti.mipmapEnabled && ti.textureType == TextureImporterType.Default) { ti.mipmapEnabled = true; dirty = true; }
                    if (dirty) { ti.SaveAndReimport(); changed++; }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log($"Stan's Game: texture imports optimised - {changed} of {seen} textures under Models changed");
        }
    }
}
