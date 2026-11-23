using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Joan's Tripo characters (PegasusLevelBuilder.NewCharacters.cs) in the story levels (to-do 3, 23 Nov):
    ///   - Sainte-Mère-Église: Civilian_Man and Civilian_Woman as the townspeople of the bucket chain and the onlookers
    ///     the Germans kept back from the burning house (the night of 5/6 June the curfew was lifted to fight the fire).
    ///   - Château de Bénouville: Nurse_Vintage, the second nurse, with Madame Vion and at the aid post.
    ///   - The 2nd Royal Warwicks at the end of the Château mission: British_Infantry (battledress and Brodie helmet,
    ///     not the airborne smock) - PegasusLevelBuilder.British.cs AddBritishBody with body "British_Infantry".
    /// These people are not targets: no Health, no ragdoll; the civilians' idle controller.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        /// <summary>One of the Tripo civilians standing at ease (file: Civilian_Man, Civilian_Woman, Nurse_Vintage).
        /// Null if the model is not in the project.</summary>
        static GameObject StoryCivilian(Transform parent, string file, string label, Vector3 pos, float yaw, float tall = 0f)
        {
            var model = HumanModel($"{CharDir}/{file}/{file}.fbx");
            if (!model) { Debug.LogWarning($"Stan's Game: {file} not found for {label}"); return null; }
            var go = new GameObject(label);
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, go.transform);
            inst.name = "Model";
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
            var mat = TexMat("Char_" + file, $"{CharDir}/{file}/{file}_BaseColor.jpg", 0.25f);
            var rs = inst.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rs)
            {
                var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = mat; r.sharedMaterials = ms;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            if (rs.Length > 0)
            {
                // a Norman of 1944: men about 1.70 m, women about 1.60 m
                if (tall <= 0f) tall = file.Contains("Man") && !file.Contains("Woman") ? 1.71f : 1.61f;
                Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.y > 0.1f) inst.transform.localScale *= tall / b.size.y;
                b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                inst.transform.position += new Vector3(0f, pos.y - b.min.y, 0f);
            }
            var anim = inst.GetComponent<Animator>(); if (!anim) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = CivilianController();
            anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var cap = go.AddComponent<CapsuleCollider>(); cap.height = tall; cap.radius = 0.25f; cap.center = new Vector3(0f, tall / 2f, 0f);
            return go;
        }
    }
}
