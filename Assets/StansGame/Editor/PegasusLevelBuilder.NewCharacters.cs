using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Joan's Tripo characters (_downloads/_Characters, Nov 2026), rigged by Models/Source~/tripo_rig.py with the same
    /// "mixamorig:" skeleton as the French civilian, so they share his humanoid mapping and every humanoid animation.
    /// Stan's Game > Import New Characters: humanoid avatar, textured material, and a prefab in Prefabs/Characters
    /// (standing idle, Health and a ragdoll) ready to place - first in the test level.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string CharDir = Root + "/Models/Characters";

        public static readonly (string file, string label, Faction side)[] NewCharacters =
        {
            ("British_Infantry", "British infantryman (battledress, Brodie helmet, '37 webbing)", Faction.British),
            ("British_TankCrew", "British tank crewman (black beret, holster)", Faction.British),
            ("US_Paratrooper_HD", "US paratrooper, 82nd Airborne (M1942 jump suit, M1 helmet)", Faction.British),
            ("Civilian_Man", "Norman townsman (cap, jacket, satchel)", Faction.British),   // (no civilian side in the game: they only need it for the ragdoll test)
            ("Civilian_Woman", "Norman townswoman (long dark dress)", Faction.British),   // (no civilian side in the game: they only need it for the ragdoll test)
            ("Nurse_Vintage", "Nurse (white uniform and cap)", Faction.British),   // (no civilian side in the game: they only need it for the ragdoll test)
        };

        [MenuItem("Stan's Game/Import New Characters", priority = 21)]
        public static void ImportNewCharacters()
        {
            string dir = PrefabDir + "/Characters";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(PrefabDir, "Characters");
            int ok = 0;
            foreach (var (file, label, side) in NewCharacters)
            {
                string fbx = $"{CharDir}/{file}/{file}.fbx";
                var model = HumanModel(fbx);
                if (!model) { Debug.LogWarning($"Stan's Game: {fbx} missing or not a valid humanoid"); continue; }
                var mat = TexMat("Char_" + file, $"{CharDir}/{file}/{file}_BaseColor.jpg", 0.25f);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                go.name = file;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = mat; r.sharedMaterials = mats;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                }
                // everyone the height of the British soldier, 1.78 m to the top of the helmet; the women 1.66 m (Joan, 24 Nov)
                {
                    var hb = new Bounds(); bool any = false;
                    foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { if (any) hb.Encapsulate(r.bounds); else { hb = r.bounds; any = true; } }
                    bool woman = file.Contains("Woman") || file.Contains("Nurse");
                    if (any && hb.size.y > 0.1f) go.transform.localScale *= (woman ? 1.66f : 1.78f) / hb.size.y;
                }
                var anim = go.GetComponent<Animator>(); if (!anim) anim = go.AddComponent<Animator>();
                anim.runtimeAnimatorController = CivilianController();
                anim.applyRootMotion = false;
                var h = go.AddComponent<Health>(); h.faction = side;
                var cap = go.AddComponent<CapsuleCollider>(); cap.height = 1.75f; cap.radius = 0.3f; cap.center = new Vector3(0f, 0.88f, 0f);
                CharacterImporter.BuildRagdoll(anim);
                go.AddComponent<Ragdoll>().animator = anim;
                PrefabUtility.SaveAsPrefabAsset(go, $"{dir}/{file}.prefab");
                Object.DestroyImmediate(go);
                ok++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Stan's Game: {ok} of {NewCharacters.Length} new characters imported (Prefabs/Characters)");
        }

        /// <summary>A model with our "mixamorig:" skeleton set up as a humanoid (as the French civilian).</summary>
        static GameObject HumanModel(string path)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!mi) return null;
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(path);
            if (mi.animationType != ModelImporterAnimationType.Human || mi.materialImportMode != ModelImporterMaterialImportMode.None
                || !avatar || !avatar.isValid || !avatar.isHuman)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
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
                }
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();
                avatar = AssetDatabase.LoadAssetAtPath<Avatar>(path);
            }
            bool good = avatar && avatar.isValid && avatar.isHuman;
            Debug.Log($"Stan's Game: {System.IO.Path.GetFileNameWithoutExtension(path)} avatar {(good ? "ok" : "NOT valid")}");
            return good ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        }
    }
}
