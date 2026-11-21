using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Import Vehicles.
    /// Sets up the armour models in Assets/StansGame/Models/Vehicles (prepared in Blender: metres, ground origin,
    /// facing +Z): the StuG III (Finnish markings replaced by a Balkenkreuz) and the Hotchkiss H35, whole and as the
    /// turretless hull that 21st Panzer's workshops (Major Becker) turned into the 7.5 cm PaK 40 (Sf) auf GW 39H(f).
    /// The H35 textures are copied from _downloads/hotchkiss-h35/textures.
    /// </summary>
    public static class VehicleImporter
    {
        public const string Dir = "Assets/StansGame/Models/Vehicles";

        [MenuItem("Stan's Game/Import Vehicles", priority = 2)]
        public static void Import()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            string h35Src = Path.Combine(project, "_downloads", "hotchkiss-h35", "textures");
            string h35Dst = Path.Combine(project, Dir, "H35");
            Directory.CreateDirectory(h35Dst);
            if (Directory.Exists(h35Src))
                foreach (var f in Directory.GetFiles(h35Src, "*.png")) File.Copy(f, Path.Combine(h35Dst, Path.GetFileName(f)), true);
            AssetDatabase.Refresh();

            foreach (var part in new[] { "body", "tower", "running_g", "track_l", "track_r" })
                SetNormal($"{Dir}/H35/H35_{part}_Normal.png");
            var h35 = new System.Collections.Generic.Dictionary<string, Material>();
            foreach (var part in new[] { "body", "tower", "running_g", "track_l", "track_r" })
                h35[part] = MakeMaterial($"H35_{part}", $"{Dir}/H35/H35_{part}_BaseColor.png", $"{Dir}/H35/H35_{part}_Normal.png", 0.25f);
            foreach (var model in new[] { "Hotchkiss_H35", "Hotchkiss_H35_Hull" })
                Remap($"{Dir}/{model}.fbx", h35);

            var stug = new System.Collections.Generic.Dictionary<string, Material>
            {
                { "mat_64", MakeMaterial("StuG_hull", $"{Dir}/StuG_hull.jpg", null, 0.2f) },
                { "mat_65", MakeMaterial("StuG_extra", $"{Dir}/StuG_extra.jpg", null, 0.2f) },
                { "mat_66", MakeMaterial("StuG_tracks", $"{Dir}/StuG_tracks.jpg", null, 0.3f) },
            };
            Remap($"{Dir}/StuG_III.fbx", stug);
            AssetDatabase.SaveAssets();
            Debug.Log("Stan's Game: vehicles imported (StuG III, Hotchkiss H35, H35 hull for the PaK 40 (Sf) conversion).");
        }

        static void SetNormal(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
        }

        static Material MakeMaterial(string name, string albedo, string normal, float smooth)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo);
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tex ? Color.white : new Color(0.45f, 0.42f, 0.32f));
            if (!string.IsNullOrEmpty(normal))
            {
                var n = AssetDatabase.LoadAssetAtPath<Texture2D>(normal);
                if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
            }
            m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Remap(string fbx, System.Collections.Generic.Dictionary<string, Material> map)
        {
            if (!(AssetImporter.GetAtPath(fbx) is ModelImporter mi)) { Debug.LogWarning("Stan's Game: missing " + fbx); return; }
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importAnimation = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var kv in map) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
        }
    }
}
