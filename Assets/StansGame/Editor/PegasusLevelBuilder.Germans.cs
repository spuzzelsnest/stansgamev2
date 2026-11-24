using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The German infantry of Normandy, June 1944: four figures from Joan's download
    /// (_downloads/normandy-nazis-call-of-duty-2-hd, unrigged), rigged in Blender by Models/Source~/rig_german_infantry.py
    /// with a Mixamo-named skeleton (so the soldiers' animations fit), bone-heat weights, arms raised to a T-pose:
    ///   German_Heer_A   rifleman in the M36/M40 field blouse, Y-straps and the M24 entrenching tool
    ///   German_Heer_B   the same kit, an older man with a moustache
    ///   German_Heer_C   rifleman with his Zeltbahn (camouflage shelter-quarter) over the blouse and a net on the helmet
    ///   German_Officer  Leutnant in field-grey with the peaked cap (Schirmmütze)
    ///   German_Panzergren  a Panzergrenadier (Joan's CC0 download, already skinned to a Mixamo skeleton): M42 helmet with
    ///                   a wire scrim and leaves, ammunition pouches - the 21st Panzer Division's 192nd Panzergrenadier
    ///                   Regiment counter-attacked the bridges on 6 June. Its own textures (PzGren_*), Models/Source~/panzergren.py.
    /// All four share one texture (German_Normandy_BaseColor.jpg). The greatcoat figure of the pack is left out
    /// (its long skirt does not run well). Every German built through Soldier() gets one of these bodies, picked from
    /// his name so a rebuild gives the same faces in the same places; the older single-body prefabs are the fallback.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string GermanDir = Root + "/Models/German";
        static readonly string[] GermanRiflemen = { "German_Heer_A", "German_Heer_B", "German_Heer_C", "German_Panzergren" };
        static readonly HashSet<string> germanPrefabsBuilt = new HashSet<string>();

        static GameObject GermanBodyModel(string file)
        {
            string path = $"{GermanDir}/{file}.fbx";
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!mi) return null;
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(path);
            if (mi.animationType != ModelImporterAnimationType.Human || mi.materialImportMode != ModelImporterMaterialImportMode.None
                || !avatar || !avatar.isValid || !avatar.isHuman)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model)
                {
                    // the bones carry Mixamo's names; map them outright (same table as the French civilian)
                    var hd = mi.humanDescription;
                    var human = new List<HumanBone>();
                    foreach (var (unity, mix) in CivilianBones)
                    {
                        var hb = new HumanBone { humanName = unity, boneName = "mixamorig:" + mix };
                        hb.limit.useDefaultValues = true;
                        human.Add(hb);
                    }
                    hd.human = human.ToArray();
                    var skel = new List<SkeletonBone>();
                    foreach (var t in model.GetComponentsInChildren<Transform>(true))
                        skel.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
                    hd.skeleton = skel.ToArray();
                    hd.upperArmTwist = 0.5f; hd.lowerArmTwist = 0.5f; hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f;
                    hd.armStretch = 0.05f; hd.legStretch = 0.05f; hd.feetSpacing = 0f; hd.hasTranslationDoF = false;
                    mi.humanDescription = hd;
                    mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                }
                mi.SaveAndReimport();
                avatar = AssetDatabase.LoadAssetAtPath<Avatar>(path);
                Debug.Log($"Stan's Game: {file} avatar {(avatar && avatar.isValid && avatar.isHuman ? "ok" : "NOT valid")}");
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>Puts one of the German bodies on a soldier root: animator, ragdoll, feet on the ground. False when missing.</summary>
        static bool AddGermanBody(GameObject root, string file)
        {
            var model = GermanBodyModel(file);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root + "/Animations/Soldier.controller");
            if (!model) return false;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Model";
            inst.transform.SetParent(root.transform, false);
            var rs = inst.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { Object.DestroyImmediate(inst); return false; }
            var mat = TexMat("German_Normandy", $"{GermanDir}/German_Normandy_BaseColor.jpg", 0.18f);
            foreach (var r in rs)
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    // the Panzergrenadier brings his own sheets (materials aren't imported, so by part and slot:
                    // body; pouches; helmet = steel, wire scrim, leaves - the leaves cut out)
                    string mn = file != "German_Panzergren" ? "" : r.name == "main" ? "PzGren_Body" : r.name == "ammopouches" ? "PzGren_Pouches"
                              : r.name == "helmet" ? (i == 0 ? "PzGren_Helmet" : i == 1 ? "PzGren_Wire" : "PzGren_Leaves") : "PzGren_Body";
                    if (mn.Length > 0)
                        ms[i] = mn == "PzGren_Leaves" ? TexMat(mn, $"{GermanDir}/{mn}.png", Color.white, 0.1f, true)
                                                     : TexMat(mn, $"{GermanDir}/{mn}.jpg", mn == "PzGren_Helmet" ? 0.3f : 0.18f);
                    else ms[i] = mat;
                }
                r.sharedMaterials = ms;
            }
            // 1.80 m to the top of the M40 helmet (or the officer's cap): a man of about 1.73 m
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y > 0.1f) inst.transform.localScale *= 1.78f / b.size.y;   // the same height as the British soldier (Joan, 24 Nov)
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.position += new Vector3(0f, root.transform.position.y - b.min.y, 0f);

            var anim = inst.GetComponent<Animator>();
            if (!anim) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            CharacterImporter.BuildRagdoll(anim);
            inst.AddComponent<FootGrounder>().animator = anim;
            var ai = root.GetComponent<SoldierAI>(); if (ai) ai.animator = anim;
            var rd = root.GetComponent<Ragdoll>(); if (rd) rd.animator = anim;
            return true;
        }

        static int StableHash(string s) { unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h & 0x7fffffff; } }

        /// <summary>
        /// The prefab for a German soldier of this name: his weapon from the name (MG42, MP40, else the Kar98k), his body
        /// from the name too (an officer's for a Leutnant, otherwise one of the three riflemen). Null when the models are missing.
        /// </summary>
        static GameObject GermanSoldierPrefab(string name)
        {
            string lower = name.ToLowerInvariant();
            bool officer = lower.Contains("leutnant") || lower.Contains("officer");
            string body = officer ? "German_Officer" : GermanRiflemen[StableHash(name) % GermanRiflemen.Length];
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), $"{GermanDir}/{body}.fbx"))) return null;
            string kind; string gun; float dmg, rpm; bool auto; int mag;
            if (name.Contains("MG42")) { kind = "MG"; gun = "MG42"; dmg = 20f; rpm = 1200f; auto = true; mag = 50; }
            else if (name.Contains("MP40") || officer) { kind = "SMG"; gun = "MP40"; dmg = 30f; rpm = 500f; auto = true; mag = 32; }
            else { kind = "Rifleman"; gun = "Kar98k"; dmg = 55f; rpm = 30f; auto = false; mag = 5; }
            string prefabName = $"German_{kind}_{body.Replace("German_", "")}";
            string path = $"{PrefabDir}/{prefabName}.prefab";
            if (germanPrefabsBuilt.Contains(prefabName))
            {
                var done = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (done) return done;
            }
            var go = new GameObject(prefabName);
            var cc = go.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            var h = go.AddComponent<Health>(); h.faction = Faction.German;
            var w = AddWeapon(go.transform, gun, dmg, rpm, auto, mag);
            var ai = go.AddComponent<SoldierAI>(); ai.weapon = w; ai.skill = 0.3f;
            go.AddComponent<Ragdoll>();
            if (!AddGermanBody(go, body)) { Object.DestroyImmediate(go); return null; }
            MountWeapons(go, w);
            AddNet(go);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            germanPrefabsBuilt.Add(prefabName);
            return prefab;
        }
    }
}
