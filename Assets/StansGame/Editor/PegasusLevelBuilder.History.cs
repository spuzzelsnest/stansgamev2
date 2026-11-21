using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>The "The History" film of the Pegasus Bridge level (Scripts/Mission/HistoryFilm.cs) and its wartime photographs (History/PB).</summary>
    public static partial class PegasusLevelBuilder
    {
        static void PegasusHistory(Transform root)
        {
            var go = new GameObject("The History (film from the main menu)");
            go.transform.SetParent(root);
            var film = go.AddComponent<HistoryFilm>();
            var list = new List<Texture2D>();
            const string dir = Root + "/History/PB";
            if (AssetDatabase.IsValidFolder(dir))
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetImporter.GetAtPath(path) is TextureImporter ti && (ti.mipmapEnabled || ti.npotScale != TextureImporterNPOTScale.None))
                    { ti.mipmapEnabled = false; ti.npotScale = TextureImporterNPOTScale.None; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport(); }
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex) list.Add(tex);
                }
            film.photos = list.ToArray();
            film.voice = HistoryNarration.Clips();
            // the German cars of the night, shown only in the film (HistoryFilm.Show)
            Kubelwagen(go.transform, "History car: Major Schmidt", new Vector3(-46f, 0f, 3f), 262f, false, false);
            Kubelwagen(go.transform, "History car: Sergeant Hickmann", new Vector3(-175f, 0f, 9f), 268f, false, false);
            Debug.Log($"Stan's Game: The History film with {list.Count} photographs");
        }
    }
}
