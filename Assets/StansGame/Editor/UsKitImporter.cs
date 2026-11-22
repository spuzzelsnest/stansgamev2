using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Import US Kit.
    /// Sets up the American models for chapter 2 (Sainte-Mère-Église). They were prepared by Claude in Blender from
    /// Joan's _downloads with Models/Source~/us_kit_1944.py: metres, centred, textures embedded, and a tiny
    /// "MuzzleMarker" (weapons) or "NoseMarker" (C-47) mesh that tells which way the model faces.
    ///   Weapon_Garand   M1 rifle, 1107 mm          Weapon_Carbine  M1A1 carbine, folding stock open, 905 mm
    ///   Weapon_Bazooka  2.36-in rocket launcher M1A1, 1370 mm
    ///   Models/Weapons/MkIIGrenade.fbx (used by the level builder for the Mk II grenade)
    ///   Weapon_Thompson Thompson M1A1, 813 mm
    ///   Models/Vehicles/C47.fbx, Flakvierling38.fbx, Models/US/T5Canopy.fbx, Models/Town/ChurchHuberville.fbx (used by the
    ///   level builder; see Models/Source~/sme_props_1944.py)
    /// </summary>
    public static class UsKitImporter
    {
        const string WeaponDir = "Assets/StansGame/Models/Weapons";
        const string VehicleDir = "Assets/StansGame/Models/Vehicles";
        const string PrefabDir = "Assets/StansGame/Prefabs";

        [MenuItem("Stan's Game/Import US Kit", priority = 23)]
        public static void Import()
        {
            foreach (var p in new[] { $"{WeaponDir}/Garand.fbx", $"{WeaponDir}/Carbine.fbx", $"{WeaponDir}/Bazooka.fbx", $"{WeaponDir}/MkIIGrenade.fbx",
                                      $"{WeaponDir}/Thompson.fbx", $"{VehicleDir}/C47.fbx", $"{VehicleDir}/Flakvierling38.fbx",
                                      "Assets/StansGame/Models/US/T5Canopy.fbx", "Assets/StansGame/Models/Town/ChurchHuberville.fbx" })
                SetupModel(p);
            BuildWeapon("Garand", $"{WeaponDir}/Garand.fbx");
            BuildWeapon("Carbine", $"{WeaponDir}/Carbine.fbx");
            BuildWeapon("Bazooka", $"{WeaponDir}/Bazooka.fbx", 0.45f, 0.1f);
            BuildWeapon("Thompson", $"{WeaponDir}/Thompson.fbx", 0.4f, 0.2f);
            AssetDatabase.SaveAssets();
            Debug.Log("Stan's Game: US kit imported (M1 rifle, M1A1 carbine, Thompson, bazooka, Mk II grenade, C-47, Flakvierling 38, T-5 canopy, Huberville church). Rebuild the Sainte-Mère-Église level to use it.");
        }

        static void SetupModel(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) { Debug.LogWarning("Stan's Game: missing " + path); return; }
            mi.globalScale = 1f; mi.useFileScale = true;
            mi.importAnimation = false; mi.importCameras = false; mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;   // URP Lit with the embedded textures
            mi.SaveAndReimport();
        }

        /// <summary>Hides the marker meshes and returns the marker's world position.</summary>
        public static Vector3? TakeMarker(GameObject inst, string name)
        {
            var m = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith(name));
            if (!m) return null;
            Vector3 p = m.position;
            var r = m.GetComponent<Renderer>(); if (r) Object.DestroyImmediate(r);
            var f = m.GetComponent<MeshFilter>(); if (f) Object.DestroyImmediate(f);
            return p;
        }

        public static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Turns a model about Y so the point from its centre to the marker faces +Z.</summary>
        public static void FaceMarkerForward(GameObject inst, Vector3 marker)
        {
            var bb = WorldBounds(inst);
            Vector3 dir = marker - bb.center; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            inst.transform.RotateAround(bb.center, Vector3.up, Vector3.SignedAngle(dir, Vector3.forward, Vector3.up));
        }

        static void BuildWeapon(string name, string modelPath, float gripFromButt = 0.35f, float gripDrop = 0.2f)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (!model) { Debug.LogWarning("Stan's Game: missing " + modelPath); return; }
            var root = new GameObject("Weapon_" + name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(root.transform, false);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var markerName = "MuzzleMarker";
            var mk = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith(markerName));
            if (mk)
            {
                FaceMarkerForward(inst, mk.position);
            }
            var bb = WorldBounds(inst);
            // where the hand holds it: ~35% from the butt, below the receiver
            Vector3 grip = new Vector3(bb.center.x, bb.center.y - bb.extents.y * gripDrop, bb.min.z + bb.size.z * gripFromButt);
            inst.transform.position -= grip;
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            var mp = TakeMarker(inst, markerName);
            bb = WorldBounds(inst);
            muzzle.position = mp ?? new Vector3(bb.center.x, bb.center.y + bb.extents.y * 0.25f, bb.max.z);
            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Weapon_{name}.prefab");
            Object.DestroyImmediate(root);
        }
    }
}
