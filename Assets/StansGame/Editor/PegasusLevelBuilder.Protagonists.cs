using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The game's three main characters, the soldiers the player plays:
    /// <list type="bullet">
    /// <item>British: the Ox &amp; Bucks / 6th Airborne para (Pegasus Bridge, the Château) - built by BuildPlayer, body from AddBritishBody.</item>
    /// <item>American: the 505th PIR paratrooper (Sainte-Mère-Église) - built by BuildUsPlayer, body from AddUsBody, which
    /// since 22 Nov is the "HD" paratrooper (Models/Characters/US_Paratrooper_HD, the slung rifle cut off his back with
    /// Models/Source~/us_para_hd_norifle.py); the older retextured British body is the fallback.</item>
    /// <item>German: the Heer rifleman "A" (Prefabs/German_Rifleman_Heer_A) - the best-rigged German model (full humanoid
    /// skeleton, passes the ragdoll check); not playable yet (on the to-do list: the sector selection).</item>
    /// </list>
    /// Each is also saved without his controls as Prefabs/Protagonists/Protagonist_&lt;side&gt;.prefab (body, animator,
    /// weapons in his hands), for showing his animations (the Proving Ground) and later the sector selection. A change
    /// to a protagonist is made in the builder above it comes from, so it reaches the playable soldier and these alike.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        public const string ProtagonistDir = PrefabDir + "/Protagonists";
        const string GermanProtagonistSource = PrefabDir + "/German_Rifleman_Heer_A.prefab";

        /// <summary>
        /// The American main character's body: the HD paratrooper (M1 helmet with net, jump suit, harness, pack, all in
        /// the model), set up like the British para body - 1.80 m, the soldier animator, always animated, ragdoll,
        /// feet kept on the ground.
        /// </summary>
        static bool AddUsHdBody(GameObject root)
        {
            const string file = "US_Paratrooper_HD";
            string path = $"{CharDir}/{file}/{file}.fbx";
            var model = HumanModel(path);
            if (!model) return false;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Model";
            inst.transform.SetParent(root.transform, false);
            var mat = TexMat("Char_" + file, $"{CharDir}/{file}/{file}_BaseColor.jpg", 0.25f);
            var rs = inst.GetComponentsInChildren<Renderer>();
            foreach (var r in rs)
            {
                var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = mat; r.sharedMaterials = ms;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y > 0.1f) inst.transform.localScale *= 1.78f / b.size.y;   // to the top of the helmet
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.position += new Vector3(0f, root.transform.position.y - b.min.y, 0f);
            var anim = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root + "/Animations/Soldier.controller");
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            CharacterImporter.BuildRagdoll(anim);
            inst.AddComponent<FootGrounder>().animator = anim;
            var ai = root.GetComponent<SoldierAI>(); if (ai) ai.animator = anim;
            var rd = root.GetComponent<Ragdoll>(); if (rd) rd.animator = anim;
            return true;
        }

        /// <summary>Builds the three protagonist prefabs (from the same builders as the playable soldiers) and returns them: British, American, German.</summary>
        static GameObject[] BuildProtagonists()
        {
            if (!AssetDatabase.IsValidFolder(ProtagonistDir)) AssetDatabase.CreateFolder(PrefabDir, "Protagonists");
            var tmp = new GameObject("_protagonists").transform;
            var result = new GameObject[3];
            try
            {
                var br = BuildPlayer(tmp).gameObject;
                result[0] = SaveProtagonist(br, "British", "Animations/Soldier_Allied.overrideController");
                var us = BuildUsPlayer(tmp, null).gameObject;
                result[1] = SaveProtagonist(us, "American", "Animations/Soldier_Allied.overrideController");
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(GermanProtagonistSource);
                if (src)
                {
                    var de = (GameObject)PrefabUtility.InstantiatePrefab(src, tmp);
                    PrefabUtility.UnpackPrefabInstance(de, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    result[2] = SaveProtagonist(de, "German", "Animations/Soldier_German.overrideController");
                }
                else Debug.LogWarning("Stan's Game: no " + GermanProtagonistSource + " - run Import Characters and Weapons");
            }
            finally { Object.DestroyImmediate(tmp.gameObject); }
            return result;
        }

        /// <summary>The soldier without anything that moves or controls him: the body, the animator, the weapons and the
        /// hands on them. Ragdoll bones stay, kinematic.</summary>
        static GameObject SaveProtagonist(GameObject go, string side, string controller)
        {
            go.name = "Protagonist_" + side;
            go.transform.SetParent(null, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var keep = new HashSet<System.Type> { typeof(WeaponHolder), typeof(HandIK), typeof(Stance), typeof(Weapon), typeof(FootGrounder) };
            // the ones others depend on go last (Unity refuses to remove Health before Ragdoll, the controller before Loadout)
            foreach (var t in new[] { typeof(Loadout), typeof(VehicleEntry), typeof(Ragdoll), typeof(SoldierAI), typeof(SoldierController), typeof(Health) })
                foreach (var c in go.GetComponentsInChildren(t, true)) Object.DestroyImmediate(c);
            for (int pass = 0; pass < 4; pass++)
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb && !keep.Contains(mb.GetType()) && mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("StansGame"))
                        Object.DestroyImmediate(mb);
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var c in go.GetComponents<CharacterController>()) Object.DestroyImmediate(c);
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (c && c.GetType().FullName != null && c.GetType().FullName.StartsWith("Unity.Netcode")) Object.DestroyImmediate(c);
            }
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            var anim = go.GetComponentInChildren<Animator>();
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>($"{Root}/{controller}");
            if (anim) { if (ctrl) anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; }
            if (!go.GetComponent<Stance>() && anim) { var st = go.AddComponent<Stance>(); st.animator = anim; }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{ProtagonistDir}/Protagonist_{side}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
