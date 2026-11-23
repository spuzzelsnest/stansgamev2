using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Retired 23 Nov (to-do): the first German soldiers - German_Rifleman, German_MG and German_SMG (the early
    /// "German soldier" download: stiff joints) and GermanSoldier (a body without a rig). Every German is now one of the
    /// Normandy Heer / Panzergrenadier / officer models (PegasusLevelBuilder.Germans.cs). Build Everything clears them
    /// out first; Stan's Game > Debug > Retire Old German Prefabs does it on its own. The model files under
    /// Models/GermanSoldier are left alone (the download can be looked at again).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static readonly string[] RetiredPrefabs = { "German_Rifleman", "German_MG", "German_SMG", "GermanSoldier" };

        [MenuItem("Stan's Game/Debug/Retire Old German Prefabs", priority = 160)]
        public static void RetireOldGermans()
        {
            int gone = 0;
            foreach (var n in RetiredPrefabs)
            {
                string path = $"{PrefabDir}/{n}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) && AssetDatabase.DeleteAsset(path)) gone++;
            }
            if (gone > 0) { AssetDatabase.SaveAssets(); Debug.Log($"Stan's Game: {gone} old German prefabs retired ({string.Join(", ", RetiredPrefabs)})"); }
        }
    }
}
