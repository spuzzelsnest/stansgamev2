using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Notre-Dame-de-l'Assomption as modelled in Blender (Models/Source~/church_sme.py, textures from church_tex.py):
    /// nave with a lean-to south aisle, the crossing tower with its louvred belfry, corner pinnacles, saddleback roof and
    /// gable clocks, the Gothic choir, buttresses, and the churchyard wall with its graves. Replaces the Huberville scan.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string ChurchModel = Root + "/Models/Town/ChurchSME.fbx";
        static bool smeModelledChurch;
        static Vector3 smeSteelePinnacle;

        static Material ChurchMat(string name, string tex, Color tint, float smooth)
        {
            var m = Mat(name, tint, smooth);
            if (tex == null) return m;
            string d = $"{GenTexDir}/{tex}_d.png", n = $"{GenTexDir}/{tex}_n.png";
            if (AssetImporter.GetAtPath(n) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap)
            { ni.textureType = TextureImporterType.NormalMap; ni.wrapMode = TextureWrapMode.Repeat; ni.SaveAndReimport(); }
            if (AssetImporter.GetAtPath(d) is TextureImporter di && di.wrapMode != TextureWrapMode.Repeat)
            { di.wrapMode = TextureWrapMode.Repeat; di.SaveAndReimport(); }
            var dt = AssetDatabase.LoadAssetAtPath<Texture2D>(d);
            var nt = AssetDatabase.LoadAssetAtPath<Texture2D>(n);
            if (dt) { m.SetTexture("_BaseMap", dt); m.mainTexture = dt; }
            if (nt) { m.SetTexture("_BumpMap", nt); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Places the modelled church at the origin; false when the model is not in the project.</summary>
        static bool SmeChurchModel(Transform town)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ChurchModel);
            smeModelledChurch = asset;
            if (!asset) return false;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, town);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Church Notre-Dame-de-l'Assomption";
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f) * asset.transform.rotation);

            Transform Find(string n) => go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith(n));
            var steele = Find("SteeleMarker");
            // Blender X is east and Y north: the Steele pinnacle is the north-west one. If the import came in turned, turn it round.
            if (steele && steele.position.x > 0f && steele.position.z < 0f) go.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * go.transform.rotation;
            var top = Find("TowerMarker");
            smeTowerTop = top ? top.position : new Vector3(0f, 32f, 0f);
            smeSteelePinnacle = steele ? steele.position : new Vector3(-4.6f, 28.3f, 4.6f);
            foreach (var mk in new[] { top, steele }) if (mk) Object.DestroyImmediate(mk.gameObject);

            var mats = new System.Collections.Generic.Dictionary<string, Material>
            {
                { "SME_Rubble", ChurchMat("SME_Church_Rubble", "SME_Rubble", Color.white, 0.08f) },
                { "SME_Ashlar", ChurchMat("SME_Church_Ashlar", "SME_Ashlar", Color.white, 0.12f) },
                { "SME_Slate", ChurchMat("SME_Church_Slate", "SME_Slate", Color.white, 0.35f) },
                { "SME_Oak", ChurchMat("SME_Church_Oak", "SME_Oak", Color.white, 0.15f) },
                { "SME_Louvre", ChurchMat("SME_Church_Louvre", "SME_Oak", new Color(0.8f, 0.8f, 0.82f), 0.1f) },
                { "SME_Glass", ChurchMat("SME_Church_Leaded_Glass", null, new Color(0.05f, 0.06f, 0.08f), 0.85f) },
                { "SME_Iron", ChurchMat("SME_Church_Iron", null, new Color(0.08f, 0.08f, 0.08f), 0.35f) },
                { "SME_ClockFace", ChurchMat("SME_Church_ClockFace", null, new Color(0.86f, 0.85f, 0.80f), 0.3f) },
            };
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                    if (ms[i]) foreach (var kv in mats) if (ms[i].name.StartsWith(kv.Key)) { ms[i] = kv.Value; break; }
                r.sharedMaterials = ms;
                var mf = r.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh) r.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            var towerMark = new GameObject("Church tower").transform; towerMark.SetParent(go.transform);
            towerMark.position = new Vector3(smeTowerTop.x, 0f, smeTowerTop.z);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return true;
        }
    }
}
