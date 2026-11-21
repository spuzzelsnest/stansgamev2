using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Women of Bénouville: Madame Léa Vion, her nurses, and villagers sheltering at the maternity home.
    /// Models/Civilian/Nurse/Nurse.fbx is Joan's nurse download (_downloads/nurse) turned into a Mixamo-named humanoid
    /// by Models/Source~/rig_nurse.py; the dress colours are recolourings of its texture (nurse_dresses.py):
    /// Blue and White for the nurses, Vion (a dark navy dress) for Madame Vion, Green/Brown/Burgundy for villagers.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string NurseDir = Root + "/Models/Civilian/Nurse";

        static GameObject NurseModel()
        {
            string path = $"{NurseDir}/Nurse.fbx";
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!mi) return null;
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(path);
            if (mi.animationType != ModelImporterAnimationType.Human || mi.materialImportMode != ModelImporterMaterialImportMode.None || !avatar || !avatar.isValid || !avatar.isHuman)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model)
                {
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
                Debug.Log($"Stan's Game: nurse avatar {(avatar && avatar.isValid && avatar.isHuman ? "ok" : "NOT valid")}");
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>A woman in one of the dresses (Blue, White, Vion, Green, Brown, Burgundy), standing at ease.</summary>
        static GameObject Woman(Transform parent, string name, Vector3 pos, float yaw, string dress, Color hair)
        {
            var model = NurseModel();
            if (!model) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            pos.y = GroundAt(pos);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var mats = new Dictionary<string, Material>
            {
                { "Dress", TexMat($"Nurse_Dress_{dress}", $"{NurseDir}/Nurse_Dress_{dress}.png", 0.12f) },
                { "Head", TexMat("Nurse_Head", $"{NurseDir}/Nurse_Head.png", 0.25f) },
                { "Skin", TexMat("Nurse_Skin", $"{NurseDir}/Nurse_Skin.png", 0.25f) },
                { "Hair", TexMat($"Nurse_Hair_{ColorUtility.ToHtmlStringRGB(hair)}", $"{NurseDir}/Nurse_Hair.png", 0.3f, 0f, hair) },
                { "Eyes", TexMat("Nurse_Eyes", $"{NurseDir}/Nurse_Eyes.png", 0.8f) },
                { "Lashes", TexMat("Nurse_Lashes", $"{NurseDir}/Nurse_Lashes.png", 0.1f) },
            };
            foreach (var key in new[] { "Hair", "Lashes" })
            {
                // cut-out for the hair cards and lashes (alpha in the texture)
                var m = mats[key];
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.4f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cull", 0f);
                EditorUtility.SetDirty(m);
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var kv in mats)
                    if (r.name.Contains(kv.Key)) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = kv.Value; r.sharedMaterials = ms; break; }
            var anim = go.GetComponent<Animator>(); if (!anim) anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = CivilianController();
            anim.applyRootMotion = false;
            var cap = go.AddComponent<CapsuleCollider>(); cap.height = 1.7f; cap.radius = 0.25f; cap.center = new Vector3(0f, 0.85f, 0f);
            return go;
        }

        /// <summary>At the château: Madame Vion at the entrance, her nurses, villagers sheltering there. Returns Madame Vion.</summary>
        static Transform ChateauWomen(Transform park, GameObject chateau)
        {
            var g = Group("Madame Vion, her nurses and villagers", park);
            var portico = chateau ? FindDeep(chateau.transform, "PorticoMarker") : null;
            Vector3 front = portico ? portico.position : ChateauPos + new Vector3(0f, 0f, -20f);
            Vector3 outDir = chateau ? front - chateau.transform.position : Vector3.back; outDir.y = 0f; outDir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, outDir);
            float face = Quaternion.LookRotation(outDir).eulerAngles.y;
            var brown = new Color(0.45f, 0.32f, 0.22f); var dark = new Color(0.25f, 0.2f, 0.16f); var fair = new Color(0.85f, 0.72f, 0.5f);
            var vion = Woman(g, "Madame Léa Vion (director of the maternity home)", front + outDir * 1.5f, face, "Vion", dark);
            Woman(g, "Nurse", front + outDir * 0.8f + side * 1.6f, face - 15f, "White", brown);
            Woman(g, "Nurse", front + outDir * 0.6f - side * 1.8f, face + 20f, "Blue", fair);
            Woman(g, "Nurse", front - side * 4.5f + outDir * 2.5f, face + 60f, "Blue", dark);
            Woman(g, "Villager sheltering at the château", front + side * 5f + outDir * 2f, face - 40f, "Green", brown);
            Woman(g, "Villager sheltering at the château", front + side * 6.2f + outDir * 3f, face - 70f, "Burgundy", dark);
            // and at the aid post in the village
            Woman(g, "Nurse at the aid post", AidPost + new Vector3(-6f, 0f, -1.5f), -90f, "White", brown);
            Woman(g, "Villager", AidPost + new Vector3(-6.5f, 0f, 3f), -110f, "Brown", fair);
            return vion ? vion.transform : null;
        }
    }
}
