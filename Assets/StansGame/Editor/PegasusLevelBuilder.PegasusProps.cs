using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Pegasus Bridge dressing from download batch 4 (Models/Props/SME, made by Models/Source~/sme_props_1944c.py):
    /// ammunition boxes at the pillbox and the anti-tank gun pit, the garrison's stores under a tarpaulin on the west
    /// bank, crates by the bridge sentry and cider casks against the Café Gondrée.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static void PegasusDownloadProps(Transform defences, Transform env)
        {
            if (!UnityEditor.AssetDatabase.IsValidFolder(SmePropDir)) return;
            var g = Group("Props (downloads, batch 4)", defences);
            float e = CanalHalfWidth;
            // MG pillbox: belts and boxes inside the sandbag wall on its south side
            SmeProp("AmmoBox1", g, new Vector3(e + 14.6f, 0f, 11.3f), 10f, 0f, "MG ammunition boxes", false);
            SmeProp("AmmoBox3", g, new Vector3(e + 15.3f, 0f, 11.4f), -15f, 0f, "MG ammunition boxes", false);
            // anti-tank gun pit (centre e + 17, -14): shell boxes inside the open back (west side, towards the bridge)
            SmeProp("AmmoBox2", g, new Vector3(e + 14.7f, 0f, -13.0f), 80f, 0f, "AT shell box", false);
            SmeProp("AmmoBox2", g, new Vector3(e + 14.5f, 0f, -14.2f), 95f, 0f, "AT shell box", false);
            SmeProp("CrateSmall", g, new Vector3(e + 14.6f, 0f, -15.4f), 30f, 0f, "AT shell crate");
            // west bank: the garrison's stores north of the road, and a crate the bridge sentry sits on
            SmeProp("SupplyStack", g, new Vector3(-e - 9f, 0f, 13.5f), 0f, 0f, "Stores under a tarpaulin (west bank)");
            SmeProp("CrateWide", g, new Vector3(-e - 5.5f, 0f, 12.2f), 85f, 0f, "Crate");
            SmeProp("CrateSmall", g, new Vector3(-e - 3.2f, 0f, 5.2f), 20f, 0f, "Crate by the bridge");
            // Café Gondrée: cider casks against its east wall, facing the canal
            float cafeEast = -e - 17f + 4.25f + 0.45f;
            SmeProp("Barrel", g, new Vector3(cafeEast, 0f, -13.8f), 0f, 0f, "Cider cask (café)");
            SmeProp("Barrel", g, new Vector3(cafeEast + 0.1f, 0f, -14.7f), 30f, 0f, "Cider cask (café)");
            SmeProp("Barrel", g, new Vector3(cafeEast + 0.05f, 0.37f, -15.8f), 0f, 90f, "Cider cask (café, on its side)");
            SmeProp("CrateTall", g, new Vector3(cafeEast + 0.1f, 0f, -17.1f), 5f, 0f, "Crate (café)");
        }

        /// <summary>
        /// Chalk 91's 2-inch mortar (Models/Props/SME/Mortar60Commando.fbx, the Pindad Mo-1 standing in for the British
        /// ML 2-inch) and two bomb boxes: hidden while Howard's glider flies in, unloaded ahead of its nose once it has stopped.
        /// </summary>
        static void PegasusGliderCargo(Transform lz, List<GliderApproach> gliders)
        {
            if (gliders == null || gliders.Count == 0 || !AssetDatabase.LoadAssetAtPath<GameObject>($"{SmePropDir}/Mortar60Commando.fbx")) return;
            var glider = gliders[0].transform;
            var holder = new GameObject("2-inch mortar (unloaded from glider 91)");
            holder.transform.SetParent(lz);
            holder.transform.SetPositionAndRotation(glider.position, Quaternion.Euler(0f, glider.eulerAngles.y, 0f));
            var kit = new GameObject("Kit"); kit.transform.SetParent(holder.transform, false);
            void Item(string fbx, Vector3 local, float yaw, string name)
            {
                var go = SmeProp(fbx, kit.transform, holder.transform.TransformPoint(local), holder.transform.eulerAngles.y + yaw, 0f, name, false);
                if (!go) return;
                GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)0);   // it moves with the glider's landing spot at run time
                var lp = go.transform.localPosition; lp.y = local.y; go.transform.localPosition = lp;
            }
            Item("Mortar60Commando", Vector3.zero, 0f, "ML 2-inch mortar");
            Item("AmmoBox2", new Vector3(0.7f, 0f, -0.6f), 80f, "2-inch bombs");
            Item("AmmoBox2", new Vector3(-0.6f, 0f, -0.7f), 100f, "2-inch bombs");
            var cu = holder.AddComponent<CargoUnload>();
            cu.glider = glider; cu.kit = kit;
            cu.offset = new Vector3(-2.5f, 0f, 11f);
        }
    }
}
