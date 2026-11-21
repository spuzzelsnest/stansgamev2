using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Some of the props made in Blender carry their textures inside the FBX (embedded media). Unity only shows them
    /// once they are extracted next to the model; until then the materials import plain white (Joan's printscreen:
    /// the trucks, the PaK 38 and the mortars). This extracts them into Textures/&lt;model&gt; beside the FBX and
    /// re-imports the model, so its materials find them. Runs at the start of every level build, and from the menu.
    /// </summary>
    public static class PropTextureFixer
    {
        static readonly string[] Folders = { "Assets/StansGame/Models/Props", "Assets/StansGame/Models/Town", "Assets/StansGame/Models/Vehicles" };

        [MenuItem("Stan's Game/Tools/Extract Embedded Prop Textures", priority = 70)]
        static void Menu() => Debug.Log($"Stan's Game: textures extracted for {Fix()} models");

        public static int Fix()
        {
            int n = 0;
            var folders = System.Array.FindAll(Folders, AssetDatabase.IsValidFolder);
            if (folders.Length == 0) return 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
                bool missing = false;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Material m && !m.mainTexture) { missing = true; break; }
                if (!missing) continue;
                string dir = Path.GetDirectoryName(path).Replace('\\', '/') + "/Textures/" + Path.GetFileNameWithoutExtension(path);
                if (AssetDatabase.IsValidFolder(dir)) continue;   // done before (the model's other materials are plain colours)
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
                if (imp.ExtractTextures(dir))
                {
                    AssetDatabase.Refresh();
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    n++;
                }
            }
            return n;
        }
    }
}
