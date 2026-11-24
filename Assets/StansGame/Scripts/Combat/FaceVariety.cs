using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// So that not every soldier of a model looks the same (Joan, 24 Nov): each one gets one of four complexions -
    /// as the model was made, sunburnt and ruddy, pale, or weathered and tanned. Only the skin of the face, neck and
    /// hands changes (the variants are the same texture with the skin recoloured: Resources/Faces/&lt;texture&gt;_v1..3,
    /// made by Models/Source~/face_variants.py). Picked from where the man stands when he appears, so every player in a
    /// multiplayer game sees the same face on the same man. The player's own soldier keeps the original.
    /// Called from Health.Awake, so soldiers spawned later get one too.
    /// </summary>
    public static class FaceVariety
    {
        static readonly Dictionary<(Material, int), Material> made = new Dictionary<(Material, int), Material>();
        static readonly Dictionary<string, bool> hasVariants = new Dictionary<string, bool>();
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        public static void Apply(GameObject who)
        {
            if (!who || who.GetComponent<SoldierController>()) return;
            var anim = who.GetComponentInChildren<Animator>(true);
            if (!anim || !anim.avatar || !anim.avatar.isHuman) return;
            Vector3 p = who.transform.position;
            int pick = Mathf.Abs(Mathf.RoundToInt(p.x * 7.3f) * 31 + Mathf.RoundToInt(p.z * 5.1f) * 17 + who.name.Length * 13) % 4;
            if (pick == 0) return;   // as made
            foreach (var r in who.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m) continue;
                    var tex = m.HasProperty(BaseMap) ? m.GetTexture(BaseMap) : m.mainTexture;
                    if (!tex) continue;
                    string key = tex.name;
                    if (!hasVariants.TryGetValue(key, out bool ok)) { ok = Resources.Load<Texture2D>($"Faces/{key}_v1"); hasVariants[key] = ok; }
                    if (!ok) continue;
                    if (!made.TryGetValue((m, pick), out var v) || !v)
                    {
                        var t = Resources.Load<Texture2D>($"Faces/{key}_v{pick}");
                        if (!t) continue;
                        v = new Material(m) { name = m.name + $" (face {pick})" };
                        if (v.HasProperty(BaseMap)) v.SetTexture(BaseMap, t);
                        v.mainTexture = t;
                        made[(m, pick)] = v;
                    }
                    mats[i] = v; changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
