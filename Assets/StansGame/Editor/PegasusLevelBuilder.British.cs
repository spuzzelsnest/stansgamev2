using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The British paratrooper and officer models (from _downloads/British para, rigged by Joan with Mixamo):
    /// humanoid, animated with the same soldier animations as the Germans, with a ragdoll. Used for the player and
    /// for all the British soldiers when present; the blockout capsule is the fallback.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string BritishDir = Root + "/Models/British";
        static bool britishChecked;
        static GameObject britishParaModel, britishOfficerModel;

        static GameObject SetupBritishModel(string downloadName, string file, string texPrefix)
        {
            if (!AssetDatabase.IsValidFolder(BritishDir)) AssetDatabase.CreateFolder(Root + "/Models", "British");
            string path = $"{BritishDir}/{file}.fbx";
            string src = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_downloads", "British para", downloadName));
            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            if (!File.Exists(abs) && File.Exists(src)) { File.Copy(src, abs); AssetDatabase.ImportAsset(path); }
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!mi) return null;
            if (mi.animationType != ModelImporterAnimationType.Human || mi.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;   // the textures come as separate, smaller files
                mi.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static void LoadBritishModels()
        {
            if (britishChecked) return;
            britishChecked = true;
            britishParaModel = SetupBritishModel("British para.fbx", "British_Para", "British_Para");
            britishOfficerModel = SetupBritishModel("British officer.fbx", "British_Officer", "British_Officer");
            Debug.Log($"Stan's Game: British para model {(britishParaModel ? "ok" : "missing")}, officer {(britishOfficerModel ? "ok" : "missing")}");
        }

        static Material BritishMat(string prefix)
        {
            var m = Mat(prefix, Color.white, 0.25f);
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{BritishDir}/{prefix}_BaseColor.jpg");
            string nPath = $"{BritishDir}/{prefix}_Normal.png";
            if (AssetImporter.GetAtPath(nPath) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap) { ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport(); }
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(nPath);
            if (albedo) { m.SetTexture("_BaseMap", albedo); m.mainTexture = albedo; }
            if (normal) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Puts the weapons in the model's right hand: a WeaponHolder follows the hand bone and points the gun
        /// where the soldier aims (as on the German soldiers).
        /// </summary>
        static Transform MountWeapons(GameObject root, params Weapon[] weapons)
        {
            var anim = root.GetComponentInChildren<Animator>();
            var holderGo = new GameObject("WeaponHolder");
            holderGo.transform.SetParent(root.transform, false);
            holderGo.transform.localPosition = new Vector3(0.2f, 1.35f, 0.3f);
            var holder = holderGo.AddComponent<WeaponHolder>();
            holder.animator = anim;
            holder.hand = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            foreach (var w in weapons)
            {
                if (!w) continue;
                w.transform.SetParent(holderGo.transform, false);
                w.transform.localPosition = Vector3.zero;
                w.transform.localRotation = Quaternion.identity;
            }
            return holderGo.transform;
        }

        /// <summary>
        /// Puts the rigged British model on a soldier (instead of the blockout capsule): animator, ragdoll.
        /// Returns false when the model is not there.
        /// </summary>
        static bool AddBritishBody(GameObject root, bool officer = false)
        {
            LoadBritishModels();
            var model = officer && britishOfficerModel ? britishOfficerModel : britishParaModel;
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root + "/Animations/Soldier.controller");
            if (!model) return false;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Model";
            inst.transform.SetParent(root.transform, false);
            // stand him on the ground: 1.78 m to the top of the helmet (a man of about 1.73 m, the average then)
            var rs = inst.GetComponentsInChildren<Renderer>();
            var mat = BritishMat(model == britishOfficerModel ? "British_Officer" : "British_Para");
            foreach (var r in rs) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = mat; r.sharedMaterials = ms; }
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y > 0.1f) inst.transform.localScale *= 1.78f / b.size.y;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.position += new Vector3(0f, root.transform.position.y - b.min.y, 0f);

            var anim = inst.GetComponent<Animator>();
            if (!anim) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            // always animate: the Mixamo mesh's bounds don't follow its 1/100-scale skeleton, so Unity thought the
            // paras were off screen and froze their bones (no animation, no hand IK)
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            CharacterImporter.BuildRagdoll(anim);
            inst.AddComponent<FootGrounder>().animator = anim;
            var ai = root.GetComponent<SoldierAI>(); if (ai) ai.animator = anim;
            var rd = root.GetComponent<Ragdoll>(); if (rd) rd.animator = anim;
            return true;
        }
    }
}
