using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// French civilians. Models/Civilian/French_Civilian.fbx is Joan's French civilian download
    /// (_downloads/French-resistent-character, an unrigged A-pose mesh) rigged in Blender by
    /// Models/Source~/rig_french_civilian.py: a Mixamo-named skeleton (so Unity maps it as a humanoid and the
    /// soldiers' animations fit), weights from Blender's bone heat on a watertight proxy, arms raised to a T-pose,
    /// the nine texture sets packed into one (French_Civilian_BaseColor.jpg); the resistance satchel and pouch left off.
    /// For now he is Georges Gondrée at his café door with a bottle of champagne (Models/Props/Champagne).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string CivilianDir = Root + "/Models/Civilian";
        const string ChampagneDir = Root + "/Models/Props/Champagne";

        static GameObject CivilianModel()
        {
            string path = $"{CivilianDir}/French_Civilian.fbx";
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
                // the bones are named as Mixamo names them, but say so outright rather than trust the auto-mapper
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model)
                {
                    var hd = mi.humanDescription;
                    var human = new System.Collections.Generic.List<HumanBone>();
                    foreach (var (unity, mix) in CivilianBones)
                    {
                        var hb = new HumanBone { humanName = unity, boneName = "mixamorig:" + mix };
                        hb.limit.useDefaultValues = true;
                        human.Add(hb);
                    }
                    hd.human = human.ToArray();
                    var skel = new System.Collections.Generic.List<SkeletonBone>();
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
                Debug.Log($"Stan's Game: French civilian avatar {(avatar && avatar.isValid && avatar.isHuman ? "ok" : "NOT valid")}");
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static readonly (string, string)[] CivilianBones =
        {
            ("Hips", "Hips"), ("Spine", "Spine"), ("Chest", "Spine1"), ("UpperChest", "Spine2"), ("Neck", "Neck"), ("Head", "Head"),
            ("LeftShoulder", "LeftShoulder"), ("LeftUpperArm", "LeftArm"), ("LeftLowerArm", "LeftForeArm"), ("LeftHand", "LeftHand"),
            ("RightShoulder", "RightShoulder"), ("RightUpperArm", "RightArm"), ("RightLowerArm", "RightForeArm"), ("RightHand", "RightHand"),
            ("LeftUpperLeg", "LeftUpLeg"), ("LeftLowerLeg", "LeftLeg"), ("LeftFoot", "LeftFoot"), ("LeftToes", "LeftToeBase"),
            ("RightUpperLeg", "RightUpLeg"), ("RightLowerLeg", "RightLeg"), ("RightFoot", "RightFoot"), ("RightToes", "RightToeBase"),
        };

        static Material TexMat(string name, string texPath, float smooth, float metal = 0f, Color? tint = null)
        {
            var m = Mat(name, tint ?? Color.white, smooth, metal);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A plain standing controller: the soldiers' relaxed idle (the arms are set by IK).</summary>
        static RuntimeAnimatorController CivilianController()
        {
            string path = Root + "/Animations/Civilian.controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing) return existing;
            AnimationClip clip = null;
            foreach (var n in new[] { "Idle", "AimStraight" })
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{Root}/Animations/Hero/Hero@{n}.fbx"))
                    if (o is AnimationClip c && !c.name.StartsWith("__preview")) { clip = c; break; }
                if (clip) break;
            }
            var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            var st = ac.layers[0].stateMachine.AddState("Stand");
            st.motion = clip;
            ac.layers[0].stateMachine.defaultState = st;
            var layers = ac.layers; layers[0].iKPass = true; ac.layers = layers;
            EditorUtility.SetDirty(ac);
            return ac;
        }

        static GameObject ChampagneBottle(Transform parent, Vector3 pos, float yaw, string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{ChampagneDir}/ChampagneBottle.fbx");
            if (!asset) return null;
            if (AssetImporter.GetAtPath($"{ChampagneDir}/ChampagneBottle.fbx") is ModelImporter bi && bi.materialImportMode != ModelImporterMaterialImportMode.None)
            { bi.materialImportMode = ModelImporterMaterialImportMode.None; bi.SaveAndReimport(); }
            var glass = TexMat("Champagne_Glass", $"{ChampagneDir}/M_ChampagneBottleGlass_Albedo.png", 0.85f, 0f, new Color(0.55f, 0.7f, 0.5f));
            var foil = TexMat("Champagne_Foil", $"{ChampagneDir}/M_ChampagneSuit_Albedo.png", 0.55f, 0.5f);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = i == 0 ? glass : foil;
                r.sharedMaterials = ms;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return go;
        }

        /// <summary>Georges Gondrée outside the café door on the Bénouville road, facing the bridge.</summary>
        static void CafeOwnerGondree(Transform env)
        {
            var model = CivilianModel();
            if (!model) return;
            var holder = new GameObject("Georges Gondrée (café owner)");
            holder.transform.SetParent(env);
            // the café door is in the north gable at (-canal - 17, z -4.97); he stands just outside it, a little east
            Vector3 p = new Vector3(-CanalHalfWidth - 16.2f, 0f, -4.2f);
            p.y = GroundAt(p);
            holder.transform.position = p;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, holder.transform);
            inst.name = "Model";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.rotation = Quaternion.Euler(0f, 50f, 0f);   // looking up the road towards the bridge
            var mat = TexMat("French_Civilian", $"{CivilianDir}/French_Civilian_BaseColor.jpg", 0.2f);
            var rs = inst.GetComponentsInChildren<Renderer>();
            foreach (var r in rs) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = mat; r.sharedMaterials = ms; }
            // 1.72 m to the top of his beret, feet on the ground
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y > 0.1f) inst.transform.localScale *= 1.72f / b.size.y;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.position += new Vector3(0f, p.y - b.min.y, 0f);

            var anim = inst.GetComponent<Animator>(); if (!anim) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = CivilianController();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var cap = inst.AddComponent<CapsuleCollider>();
            cap.height = 1.72f / inst.transform.lossyScale.y; cap.radius = 0.22f / inst.transform.lossyScale.y;
            cap.center = new Vector3(0f, cap.height / 2f, 0f);

            var owner = inst.AddComponent<CafeOwner>();
            var bottle = ChampagneBottle(holder.transform, p + Vector3.up, 0f, "Champagne (in his hand)");
            owner.bottle = bottle ? bottle.transform : null;

            // two more bottles waiting on the cider cask by the café's canal-side wall
            var cask = env.root.GetComponentsInChildren<Transform>(true);
            foreach (var t in cask)
                if (t.name == "Cider cask (café)")
                {
                    var cb = UsKitImporter.WorldBounds(t.gameObject);
                    ChampagneBottle(t.parent, new Vector3(cb.center.x - 0.08f, cb.max.y, cb.center.z + 0.05f), 20f, "Champagne (on the cask)");
                    ChampagneBottle(t.parent, new Vector3(cb.center.x + 0.09f, cb.max.y, cb.center.z - 0.06f), 110f, "Champagne (on the cask)");
                    break;
                }
        }
    }
}
