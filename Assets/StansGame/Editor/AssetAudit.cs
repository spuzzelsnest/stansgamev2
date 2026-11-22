using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Two checks over what the game really uses, each writing a report to Logs/ (easier to read than the Console):
    ///   Check Ragdolls  - every character prefab and every character placed in the levels: the rig, the ragdoll
    ///                     bones (hips, chest, head, arms, legs), their colliders, joints and limits, masses, loose
    ///                     rigidbodies (a rifle with physics would be dragged into the ragdoll), and the men who still
    ///                     have a placeholder body (no rigged model: they fall over in one piece).
    ///   Check Textures  - every material on every object in the levels: pink (broken shader), white (lost its
    ///                     texture), not URP, untextured big surfaces, low-resolution or oversized textures, and the
    ///                     biggest surfaces without a normal map.
    /// Both open the levels in the build list one after the other and come back to the level you had open.
    /// </summary>
    public static class AssetAudit
    {
        static readonly HumanBodyBones[] Main =
        {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
        };

        // ------------------------------------------------------------------ ragdolls

        [MenuItem("Stan's Game/Debug/Check Ragdolls (all characters)", priority = 220)]
        static void CheckRagdolls()
        {
            var sb = new StringBuilder();
            int problems = 0;
            var placeholders = new SortedDictionary<string, List<string>>();   // role -> levels
            var checkedKinds = new HashSet<string>();

            sb.AppendLine("== Character prefabs ==");
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/StansGame" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!go || !go.GetComponent<Health>() || !(go.GetComponent<SoldierAI>() || go.GetComponent<SoldierController>() || go.GetComponent<Ragdoll>())) continue;
                problems += CheckOne(go, System.IO.Path.GetFileNameWithoutExtension(path), sb, placeholders, "prefab");
                checkedKinds.Add(go.name);
            }

            sb.AppendLine().AppendLine("== Characters placed in the levels (not from a prefab above) ==");
            ForEachLevel(level =>
            {
                foreach (var h in Object.FindObjectsByType<Health>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (!(h.GetComponent<SoldierAI>() || h.GetComponent<SoldierController>())) continue;
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(h.gameObject);
                    if (src && checkedKinds.Contains(src.name)) continue;   // its prefab is checked above
                    string kind = KindOf(h.name);
                    if (checkedKinds.Contains(level + "|" + kind)) continue;   // one of each kind a level is enough
                    checkedKinds.Add(level + "|" + kind);
                    problems += CheckOne(h.gameObject, $"{level}: {h.name}", sb, placeholders, level);
                }
                // townspeople, nurses and other people without a rig (not soldiers)
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    string n = t.name;
                    if (!(n.StartsWith("Townsperson") || n.Contains("civilian") || n.Contains("Civilian") || n.StartsWith("Nurse") || n.StartsWith("Villager"))) continue;
                    if (t.GetComponentInChildren<Animator>(true)) continue;
                    if (t.parent && (t.parent.name.StartsWith("Townsperson") || t.parent.name.StartsWith("Nurse"))) continue;
                    Add(placeholders, KindOf(n) + " (civilian, primitive shapes)", level);
                }
            });

            sb.AppendLine().AppendLine("== Still a placeholder body (no rigged model) - to do ==");
            if (placeholders.Count == 0) sb.AppendLine("(none)");
            foreach (var kv in placeholders) sb.AppendLine($"- {kv.Key}  [{string.Join(", ", kv.Value.Distinct())}]");

            Write("ragdoll-check.txt", $"Ragdoll check, {System.DateTime.Now:yyyy-MM-dd HH:mm}\n{problems} problems\n\n{sb}");
            Debug.Log($"Stan's Game: ragdoll check - {problems} problems, {placeholders.Count} kinds of placeholder body (Logs/ragdoll-check.txt)");
        }

        static string KindOf(string name)
        {
            // "Sentry by the fire 2" -> "Sentry by the fire": numbers and the bits in brackets don't make a new kind
            string k = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\d+$", "");
            return k.Trim();
        }

        static void Add(SortedDictionary<string, List<string>> d, string k, string level)
        {
            if (!d.TryGetValue(k, out var l)) d[k] = l = new List<string>();
            l.Add(level);
        }

        static int CheckOne(GameObject go, string label, StringBuilder sb, SortedDictionary<string, List<string>> placeholders, string where)
        {
            var issues = new List<string>();
            var anim = go.GetComponentInChildren<Animator>(true);
            var skinned = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var h = go.GetComponent<Health>();
            string side = h ? (h.faction == Faction.German ? "German" : "Allied") : "?";
            if (!skinned || !anim)
            {
                Add(placeholders, $"{KindOf(go.name)} ({side})", where);
                sb.AppendLine($"- {label}: placeholder body (no rigged model) - falls over in one piece");
                return 0;
            }
            if (!anim.avatar || !anim.avatar.isValid || !anim.isHuman) issues.Add("the model has no valid humanoid avatar (no ragdoll can be built)");
            if (!go.GetComponent<Ragdoll>()) issues.Add("no Ragdoll component (dies without falling)");
            if (!go.GetComponent<CharacterController>() && !go.GetComponent<Collider>()) issues.Add("no CharacterController or collider on the root");

            var bodies = go.GetComponentsInChildren<Rigidbody>(true).Where(r => r.gameObject != go).ToArray();
            float mass = 0f;
            if (anim.isHuman && anim.avatar && anim.avatar.isValid)
                foreach (var b in Main)
                {
                    var t = anim.GetBoneTransform(b);
                    if (!t && b == HumanBodyBones.Chest) t = anim.GetBoneTransform(HumanBodyBones.Spine);
                    if (!t) { issues.Add($"the rig has no {b} bone"); continue; }
                    var rb = t.GetComponent<Rigidbody>();
                    if (!rb) { issues.Add($"{b}: no ragdoll body"); continue; }
                    if (!t.GetComponent<Collider>()) issues.Add($"{b}: body without a collider (falls through the ground)");
                }
            foreach (var rb in bodies)
            {
                mass += rb.mass;
                string n = rb.name;
                if (rb.GetComponentInParent<Weapon>() || rb.GetComponentInParent<WeaponHolder>()) { issues.Add($"'{n}' (a weapon) has a rigidbody: it becomes part of the ragdoll"); continue; }
                if (rb.mass <= 0.01f) issues.Add($"'{n}' weighs nothing");
                if (!rb.isKinematic) issues.Add($"'{n}' is not kinematic while alive (it will flop while he walks)");
                foreach (var c in rb.GetComponents<Collider>())
                {
                    if (c is CapsuleCollider cc && (cc.radius <= 0.001f || cc.height <= 0.001f || float.IsNaN(cc.height))) issues.Add($"'{n}': capsule collider of zero size");
                    if (c is BoxCollider bc && (bc.size.x <= 0.001f || bc.size.y <= 0.001f || bc.size.z <= 0.001f)) issues.Add($"'{n}': box collider of zero size");
                    if (c.isTrigger) issues.Add($"'{n}': its collider is a trigger (falls through the ground)");
                }
                var j = rb.GetComponent<CharacterJoint>();
                bool isHips = anim.isHuman && anim.avatar && anim.avatar.isValid && rb.transform == anim.GetBoneTransform(HumanBodyBones.Hips);
                if (!j) { if (!isHips) issues.Add($"'{n}': body without a joint (comes off the skeleton)"); continue; }
                if (!j.connectedBody) issues.Add($"'{n}': joint not connected to anything");
                else if (!rb.transform.IsChildOf(j.connectedBody.transform)) issues.Add($"'{n}': joint connected to '{j.connectedBody.name}', which is not above it in the skeleton");
                if (j.lowTwistLimit.limit > j.highTwistLimit.limit) issues.Add($"'{n}': twist limits the wrong way round ({j.lowTwistLimit.limit:0} > {j.highTwistLimit.limit:0})");
                if (j.swing1Limit.limit <= 0f && j.swing2Limit.limit <= 0f) issues.Add($"'{n}': both swing limits 0 (locked stiff)");
                if (j.swing1Limit.limit > 177f || j.swing2Limit.limit > 177f) issues.Add($"'{n}': swing limit over 177 degrees (bends right round)");
                if (j.axis.sqrMagnitude < 1e-4f) issues.Add($"'{n}': joint axis is zero");
                else if (Mathf.Abs(Vector3.Dot(j.axis.normalized, j.swingAxis.normalized)) > 0.95f) issues.Add($"'{n}': twist and swing axes are the same (the limb can't bend properly)");
            }
            if (bodies.Length > 0 && (mass < 30f || mass > 150f)) issues.Add($"total ragdoll mass {mass:0} kg (about 60-90 expected)");

            if (issues.Count == 0) { sb.AppendLine($"- {label} ({side}): OK - {bodies.Length} bodies, {mass:0} kg"); return 0; }
            sb.AppendLine($"- {label} ({side}): {issues.Count} problem{(issues.Count == 1 ? "" : "s")}");
            foreach (var s in issues) sb.AppendLine("    * " + s);
            return issues.Count;
        }

        // ------------------------------------------------------------------ textures

        class MatUse { public Material m; public int count; public float area; public string example; public HashSet<string> levels = new HashSet<string>(); }

        [MenuItem("Stan's Game/Debug/Check Textures (objects in the levels)", priority = 221)]
        static void CheckTextures()
        {
            var uses = new Dictionary<Material, MatUse>();
            ForEachLevel(level =>
            {
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    var b = r.bounds; float area = Mathf.Max(b.size.x * b.size.y, b.size.x * b.size.z, b.size.y * b.size.z);
                    foreach (var m in r.sharedMaterials)
                    {
                        if (!m) continue;
                        if (!uses.TryGetValue(m, out var u)) uses[m] = u = new MatUse { m = m, example = Path(r.transform) };
                        u.count++; u.area += area; u.levels.Add(level);
                    }
                }
            });

            var broken = new List<string>(); var white = new List<string>(); var notUrp = new List<string>();
            var plain = new List<(float a, string s)>(); var lowRes = new List<string>(); var huge = new List<string>(); var noNormal = new List<(float a, string s)>();
            var hugeTex = new HashSet<Texture>();
            foreach (var u in uses.Values)
            {
                var m = u.m; string where = $"'{m.name}' on {u.count} object{(u.count == 1 ? "" : "s")} [{string.Join(", ", u.levels)}], e.g. {u.example}";
                string sh = m.shader ? m.shader.name : "(none)";
                if (!m.shader || sh == "Hidden/InternalErrorShader" || !m.shader.isSupported) { broken.Add(where + $" - shader '{sh}'"); continue; }
                if (sh.StartsWith("Universal Render Pipeline/Particles") || sh.StartsWith("Sprites") || sh.StartsWith("UI/") || sh.StartsWith("Skybox") || sh.Contains("Nature/")) continue;
                if (!sh.StartsWith("Universal Render Pipeline") && !sh.StartsWith("Shader Graphs") && !sh.StartsWith("StansGame")) notUrp.Add(where + $" - shader '{sh}' (may turn pink in the built game)");
                Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                Color col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.grey;
                if (!tex)
                {
                    bool emissive = m.IsKeywordEnabled("_EMISSION");
                    if (col.r > 0.92f && col.g > 0.92f && col.b > 0.92f && col.a > 0.5f && !emissive) white.Add(where + " - plain white with no texture (lost its texture?)");
                    else if (u.area > 6f && col.a > 0.5f && !emissive) plain.Add((u.area, where + $" - flat colour ({col.r:0.00}, {col.g:0.00}, {col.b:0.00}), {u.area:0} m² in all"));
                    continue;
                }
                int w = tex.width, h = tex.height;
                if ((w >= 4096 || h >= 4096) && hugeTex.Add(tex)) huge.Add($"'{tex.name}' {w}x{h} ({AssetDatabase.GetAssetPath(tex)}) - a 2048 import would do");
                float perUse = u.area / Mathf.Max(1, u.count);
                if (Mathf.Max(w, h) <= 256 && perUse > 20f) lowRes.Add(where + $" - texture '{tex.name}' only {w}x{h} on ~{perUse:0} m² each");
                bool hasNormal = m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap");
                if (!hasNormal && u.area > 30f) noNormal.Add((u.area, where + $" - {u.area:0} m² with texture '{tex.name}' but no normal map"));
            }

            var sb = new StringBuilder();
            void Section(string title, IEnumerable<string> lines, string none = "(none)")
            {
                var l = lines.ToList();
                sb.AppendLine().AppendLine($"== {title} ({l.Count}) ==");
                if (l.Count == 0) sb.AppendLine(none);
                foreach (var s in l) sb.AppendLine("- " + s);
            }
            Section("PINK - broken or missing shader", broken);
            Section("WHITE - no texture, plain white", white);
            Section("Not a URP shader", notUrp);
            Section("Improvement: big surfaces in a flat colour (a texture would help) - biggest first, top 30", plain.OrderByDescending(x => x.a).Take(30).Select(x => x.s));
            Section("Improvement: low-resolution texture on big surfaces", lowRes);
            Section("Improvement: biggest textured surfaces without a normal map - top 25", noNormal.OrderByDescending(x => x.a).Take(25).Select(x => x.s));
            Section("Memory: textures of 4096 or more", huge);
            int problems = broken.Count + white.Count + notUrp.Count;
            Write("texture-check.txt", $"Texture check, {System.DateTime.Now:yyyy-MM-dd HH:mm}\n{uses.Count} materials in use, {problems} problems, {plain.Count + lowRes.Count + noNormal.Count} possible improvements\n{sb}");
            Debug.Log($"Stan's Game: texture check - {uses.Count} materials, {problems} problems (Logs/texture-check.txt)");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Opens each level in the build list in turn, runs the check, and goes back to the level that was open.</summary>
        static void ForEachLevel(System.Action<string> check)
        {
            var start = EditorSceneManager.GetActiveScene();
            if (start.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string back = start.path;
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && System.IO.File.Exists(s.path)).Select(s => s.path).ToList();
            try
            {
                for (int i = 0; i < scenes.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Stan's Game check", System.IO.Path.GetFileNameWithoutExtension(scenes[i]), (float)i / scenes.Count);
                    var sc = EditorSceneManager.OpenScene(scenes[i], OpenSceneMode.Single);
                    check(sc.name);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!string.IsNullOrEmpty(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            }
        }

        static void Write(string file, string text)
        {
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/" + file, text);
        }

        static string Path(Transform t)
        {
            string s = t.name;
            for (var p = t.parent; p && p.parent; p = p.parent) s = p.name + "/" + s;
            return s.Length > 80 ? "..." + s.Substring(s.Length - 77) : s;
        }
    }
}
