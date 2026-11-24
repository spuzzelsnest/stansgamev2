using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Level template 4a: every character stands in the T-pose. Joan's Mixamo "T-Pose" clip (_downloads/_Animations,
    /// imported to Animations/Mixamo/Common) puts any humanoid into it, so rigs can be compared side by side: arms level
    /// and straight, shoulders not pulled up, skin not stretched or torn at the armpits, elbows, hips and knees, the
    /// helmet and kit following the head and body. A character whose skin breaks here has bad weights (printscreens 21,
    /// 33, 41), whatever pose it was modelled in.
    /// </summary>
    public static class TPoseCheck
    {
        const string ClipPath = "Assets/StansGame/Animations/Mixamo/Common/T-Pose.anim";

        static AnimationClip Clip()
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            return c;   // (kept for reference; the pose itself comes from each avatar)
        }

        /// <summary>
        /// The T-pose exactly as Unity's humanoid avatar has it (the skeleton stored in the avatar when the model was
        /// imported). Sampling the Mixamo T-Pose clip in the editor stretched the skins into spikes (printscreen 43:
        /// a humanoid clip can't be played outside Play mode reliably), so the avatar's own pose is used here.
        /// </summary>
        static bool Pose(GameObject go, AnimationClip clip)
        {
            var anim = go.GetComponentInChildren<Animator>(true);
            if (!anim || !anim.avatar || !anim.avatar.isHuman) return false;
            Undo.RegisterFullObjectHierarchyUndo(go, "T-Pose");
            var byName = new Dictionary<string, Transform>();
            foreach (var t in anim.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            int n = 0;
            foreach (var sb in anim.avatar.humanDescription.skeleton)
            {
                if (!byName.TryGetValue(sb.name, out var t) || t == anim.transform || t == go.transform) continue;
                t.localRotation = sb.rotation;   // turn the joints only: the bone lengths stay as the model has them
                n++;
            }
            return n > 0;
        }

        /// <summary>The Scene view looking at the whole line-up from the front.</summary>
        [MenuItem("Stan's Game/Debug/T-Pose Line-up: Reset View", priority = 182)]
        static void ResetView()
        {
            var b = new Bounds(Vector3.zero, Vector3.zero); bool any = false;
            foreach (var r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude))
            { if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; } }
            var sv = SceneView.lastActiveSceneView; if (!sv) return;
            if (!any) b = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 1f));
            // from the front (the soldiers face -Z), a little above eye height
            sv.LookAt(b.center, Quaternion.Euler(8f, 0f, 0f), Mathf.Max(3f, b.extents.x * 1.1f), false);
            sv.Repaint();
        }

        [MenuItem("Stan's Game/Debug/T-Pose Selected Characters", priority = 180)]
        static void PoseSelected()
        {
            var clip = Clip();
            int n = 0;
            foreach (var go in Selection.gameObjects) if (Pose(go, clip)) n++;
            Debug.Log($"Stan's Game: {n} character(s) put in the T-pose (Ctrl+Z puts them back)");
        }

        [MenuItem("Stan's Game/Debug/T-Pose Line-up (all characters)", priority = 181)]
        static void LineUp()
        {
            var clip = Clip();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var list = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/StansGame" }))
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (!pf || pf.name.StartsWith("NetPlayer") || pf.GetComponent<TankController>() || pf.GetComponent<TruckController>()) continue;
                var a = pf.GetComponentInChildren<Animator>(true);
                if (a && a.avatar && a.avatar.isHuman) list.Add(pf);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            for (int i = 0; i < list.Count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(list[i]);
                go.transform.position = new Vector3(i * 2.2f, 0f, 0f);
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // facing the camera
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;   // nobody walks off
                Pose(go, clip);
                var label = new GameObject(list[i].name).AddComponent<TextMesh>();
                label.transform.position = go.transform.position + new Vector3(0f, 2.15f, 0f);
                label.text = list[i].name; label.characterSize = 0.05f; label.fontSize = 48; label.anchor = TextAnchor.MiddleCenter; label.color = Color.black;
            }
            var cam = Camera.main;
            if (cam) { cam.transform.position = new Vector3((list.Count - 1) * 1.1f, 1.2f, -Mathf.Max(6f, list.Count * 1.3f)); cam.transform.rotation = Quaternion.identity; }
            ResetView();
            Debug.Log($"Stan's Game: T-pose line-up of {list.Count} characters (a new, unsaved scene - look from the front and the side)");
        }

        // ------------------------------------------------------------------ sizes

        /// <summary>Height of every character prefab (top of the head or helmet to the soles), into Logs/character-sizes.txt.</summary>
        [MenuItem("Stan's Game/Tools/Character Sizes Report", priority = 301)]
        public static void SizesReport()
        {
            var log = new System.Text.StringBuilder("prefab\theight m\tmodel\tmodel scale in prefab\n");
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/StansGame" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!pf) continue;
                var a = pf.GetComponentInChildren<Animator>(true);
                if (!a || !a.avatar || !a.avatar.isHuman) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
                var b = new Bounds(); bool any = false; string mesh = "";
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!r.sharedMesh) continue;
                    r.updateWhenOffscreen = true;
                    if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; mesh = AssetDatabase.GetAssetPath(r.sharedMesh); }
                }
                var anim = go.GetComponentInChildren<Animator>(true);
                log.AppendLine($"{path}\t{(any ? b.size.y : 0f):0.000}\t{mesh}\t{anim.transform.lossyScale.y:0.0000}");
                Object.DestroyImmediate(go);
            }
            string logs = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs");
            System.IO.Directory.CreateDirectory(logs);
            System.IO.File.WriteAllText(System.IO.Path.Combine(logs, "character-sizes.txt"), log.ToString());
            Debug.Log("Stan's Game: character sizes written to Logs/character-sizes.txt");
        }

        // ------------------------------------------------------------------ straighten the avatars

        /// <summary>
        /// Printscreens 44-47: in the line-up some soldiers had their arms half down or bent at the elbow and wrist.
        /// Their skins are fine; it is the T-pose stored in each model's humanoid avatar (taken from the pose the file
        /// was saved in) that is off - and every animation is played relative to that pose, which is why some arms
        /// looked rubbery or crooked in play (printscreens 21, 33). This does what Unity's "Enforce T-Pose" button does,
        /// for every character model: the upper arm, forearm and hand laid straight out sideways and level, legs
        /// straight down under the hips, then the avatar is rebuilt from that pose. The meshes are not touched.
        /// Then Build Everything, so the prefabs pick the new avatars up.
        /// </summary>
        [MenuItem("Stan's Game/Tools/Straighten Character Avatars (Enforce T-Pose)", priority = 300)]
        public static void StraightenAvatars()
        {
            var log = new System.Text.StringBuilder("Straighten avatars (enforce T-pose)\n");
            int fixedN = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/StansGame/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
                if (mi.animationType != ModelImporterAnimationType.Human || mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) continue;
                var hd = mi.humanDescription;
                if (hd.human == null || hd.human.Length == 0 || hd.skeleton == null || hd.skeleton.Length == 0) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!model) continue;
                var go = Object.Instantiate(model);
                try
                {
                    var byName = new Dictionary<string, Transform>();
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
                    // start from the model's own pose as the file has it now (a stored avatar pose can be older than the
                    // file: German_Panzergren was re-exported from Blender, and its old avatar pose on the new bones made
                    // him explode into spikes in play - 24 Nov)
                    var bone = new Dictionary<string, Transform>();
                    foreach (var hb in hd.human) if (byName.TryGetValue(hb.boneName, out var t)) bone[hb.humanName] = t;
                    Transform B(string n) => bone.TryGetValue(n, out var t) ? t : null;
                    var hips = B("Hips"); if (!hips) continue;
                    float worst = 0f;
                    void Aim(Transform from, Transform to, Vector3 dir)
                    {
                        if (!from || !to) return;
                        Vector3 cur = to.position - from.position; if (cur.sqrMagnitude < 1e-8f) return;
                        float a = Vector3.Angle(cur, dir); if (a < 0.5f) return;
                        worst = Mathf.Max(worst, a);
                        from.rotation = Quaternion.FromToRotation(cur, dir) * from.rotation;
                    }
                    foreach (var side in new[] { "Left", "Right" })
                    {
                        var upper = B(side + "UpperArm"); var lower = B(side + "LowerArm"); var hand = B(side + "Hand");
                        var finger = B(side + "MiddleProximal") ?? B(side + "IndexProximal");
                        if (!upper || !lower || !hand) continue;
                        // straight out to that side, level (the side the shoulder is on, in the model's own frame)
                        Vector3 right = go.transform.right;
                        Vector3 outward = Vector3.Dot(upper.position - hips.position, right) >= 0f ? right : -right;
                        Aim(upper, lower, outward);
                        Aim(lower, hand, outward);
                        if (finger) Aim(hand, finger, outward);
                        // legs straight down
                        var thigh = B(side + "UpperLeg"); var shin = B(side + "LowerLeg"); var foot = B(side + "Foot");
                        Aim(thigh, shin, -go.transform.up);
                        Aim(shin, foot, -go.transform.up);
                    }
                    // the avatar's pose made again from every node of the model, as Unity does for a new model
                    var sk = new List<SkeletonBone>();
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        sk.Add(new SkeletonBone { name = t == go.transform ? model.name : t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
                    hd.skeleton = sk.ToArray();
                    mi.humanDescription = hd;
                    mi.SaveAndReimport();
                    fixedN++;
                    log.AppendLine($"  {path}: avatar pose rebuilt from the model (arms/legs straightened up to {worst:0} deg)");
                }
                finally { Object.DestroyImmediate(go); }
            }
            string logs = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs");
            System.IO.Directory.CreateDirectory(logs);
            System.IO.File.WriteAllText(System.IO.Path.Combine(logs, "tpose-avatars.txt"), log.ToString());
            Debug.Log($"Stan's Game: {fixedN} character avatar(s) straightened into the T-pose (Logs/tpose-avatars.txt). Now Build Everything.");
        }
    }
}
