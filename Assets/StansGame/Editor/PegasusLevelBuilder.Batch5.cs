using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Download batch 5 (Models/Props/SME, made by Models/Source~/downloads_batch5.py and pak38_1944.py):
    /// the field bunker that replaces the old MG pillbox, garden sheds, the small Bénouville house, and the rig that
    /// turns the PaK 38 model into a gun that can be laid and fired (FieldGun).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        /// <summary>
        /// A prop from Models/Props/SME standing on the ground, inside a holder at y = 0 so the level's final
        /// "set everything down on the LiDAR ground" pass lifts it exactly once. Optional box collider round the whole
        /// thing, or mesh colliders (for buildings you can walk into).
        /// </summary>
        static GameObject GroundProp(string file, Transform parent, Vector3 pos, float yaw, string name, bool boxCollider, bool meshColliders = false)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent);
            holder.transform.position = new Vector3(pos.x, 0f, pos.z);
            var go = SmeProp(file, holder.transform, new Vector3(pos.x, 0f, pos.z), yaw, 0f, file, boxCollider);
            if (!go) { Debug.LogWarning($"Stan's Game: {SmePropDir}/{file}.fbx missing"); Object.DestroyImmediate(holder); return null; }
            var lp = go.transform.localPosition; lp.y = pos.y; go.transform.localPosition = lp;
            if (meshColliders)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (!mf.GetComponent<Collider>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            return holder;
        }

        /// <summary>
        /// Rigs the three-part PaK 38 model: a traverse pivot on the axle line carrying the upper carriage (shield,
        /// cradle, sight) and an elevation pivot at the trunnions carrying the barrel, the muzzle and the sight marker.
        /// </summary>
        static FieldGun RigFieldGun(GameObject model)
        {
            Transform Find(string n)
            {
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
                return null;
            }
            var upper = Find("Pak38_Upper"); var barrel = Find("Pak38_Barrel");
            var muzzle = Find("Pak38_Muzzle"); var sight = Find("Pak38_Sight");
            if (!upper || !barrel || !muzzle || !sight) { Debug.LogWarning("Stan's Game: Pak38.fbx is the old one-piece model (run Models/Source~/pak38_1944.py)"); return null; }
            Vector3 fwd = muzzle.position - barrel.position; fwd.y = 0f; fwd.Normalize();
            var trav = new GameObject("Traverse").transform; trav.SetParent(model.transform, true);
            trav.SetPositionAndRotation(upper.position, Quaternion.LookRotation(fwd, Vector3.up));
            var elev = new GameObject("Elevation").transform; elev.SetParent(trav, true);
            elev.SetPositionAndRotation(barrel.position, Quaternion.LookRotation(fwd, Vector3.up));
            upper.SetParent(trav, true); barrel.SetParent(elev, true); muzzle.SetParent(elev, true); sight.SetParent(elev, true);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            var fg = model.AddComponent<FieldGun>();
            fg.traverse = trav; fg.elevate = elev; fg.barrel = barrel; fg.muzzle = muzzle; fg.sight = sight;
            fg.shellPrefab = pak38Shell;
            return fg;
        }

        /// <summary>Garden sheds behind the café and the house next to it.</summary>
        static void PegasusBatch5(Transform env)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>($"{SmePropDir}/GardenShed.fbx")) return;
            var b = env.Find("Buildings") ?? env;
            float e = CanalHalfWidth;
            GroundProp("GardenShed", b, new Vector3(-e - 13.5f, 0f, -28.5f), 90f, "Garden shed (café)", true);
            GroundProp("GardenShed", b, new Vector3(-e - 31.5f, 0f, -30.5f), 5f, "Garden shed (house by the café)", true);
        }
    }
}
