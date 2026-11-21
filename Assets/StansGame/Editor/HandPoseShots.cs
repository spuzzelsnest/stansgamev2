using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu (play mode): Stan's Game > Debug > Capture Hand Poses.
    /// Photographs the player and the nearest soldiers carrying other weapons from the side and from the front-left,
    /// under a temporary light, into _downloads/previews/hands_*.png — to check how each weapon sits in the hands —
    /// plus the player's own view (hands_view_player.png) and each arm's reach (hands_report.txt).
    /// </summary>
    public static class HandPoseShots
    {
        [MenuItem("Stan's Game/Debug/Skip Ahead (play mode)", priority = 51)]
        static void SkipAhead()
        {
            if (Application.isPlaying && BenouvilleMission.Instance)
            {
                BenouvilleMission.Instance.DebugSkip();
                Debug.Log($"Stan's Game: Bénouville phase {BenouvilleMission.Instance.CurrentPhase}");
                return;
            }
            if (!Application.isPlaying || !MissionDirector.Instance) { Debug.LogWarning("Stan's Game: start the game first"); return; }
            MissionDirector.Instance.DebugSkip();
            Debug.Log($"Stan's Game: mission phase {MissionDirector.Instance.CurrentPhase}");
        }

        [MenuItem("Stan's Game/Debug/Raise or Lower Bridge (play mode)", priority = 53)]
        static void ToggleBridge()
        {
            var lift = BridgeLift.Instance;
            if (!Application.isPlaying || !lift) { Debug.LogWarning("Stan's Game: start the game first"); return; }
            if (!lift.Request(!lift.Raised, out string why)) { Debug.Log("Stan's Game: " + why + " (forcing it for the test)"); lift.DebugForce(!lift.Raised); }
            Debug.Log($"Stan's Game: bridge going {(lift.Raised ? "up" : "down")}");
        }

        [MenuItem("Stan's Game/Debug/Go to the Bridge Controls (play mode)", priority = 54)]
        static void GoToControls()
        {
            var lift = BridgeLift.Instance;
            if (!Application.isPlaying || !lift || !lift.panel) { Debug.LogWarning("Stan's Game: start the game first"); return; }
            foreach (var sc in Object.FindObjectsByType<SoldierController>(FindObjectsSortMode.None))
            {
                if (!sc.enabled) continue;
                var cc = sc.GetComponent<CharacterController>();
                if (cc) cc.enabled = false;
                var hut = lift.panel.parent;
                sc.transform.position = lift.panel.position + hut.right * 1.0f + Vector3.down * 1.0f;
                sc.transform.rotation = Quaternion.LookRotation(-hut.right);
                if (cc) cc.enabled = true;
                Debug.Log("Stan's Game: player moved to the bridge controller");
                return;
            }
        }

        // Ctrl+Shift+H: works from the Game view too, so the mouse never has to leave the game (moving it to the menu
        // turns the player's view)
        [MenuItem("Stan's Game/Debug/Capture Hand Poses %#h", priority = 52)]
        static void Capture()
        {
            if (!Application.isPlaying) { Debug.LogWarning("Stan's Game: start the game first (hand poses only exist in play mode)"); return; }
            // soldiers off screen don't run their animation (or the hand IK): wake them all for a few frames first
            var animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
            var modes = animators.Select(a => a.cullingMode).ToArray();
            foreach (var a in animators) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            int until = Time.frameCount + 10;   // ten game frames, so every soldier has run his hand IK
            void Tick()
            {
                if (Application.isPlaying && Time.frameCount < until) return;
                EditorApplication.update -= Tick;
                try { Shoot(); }
                catch (System.Exception e) { Debug.LogError("Stan's Game: hand-pose capture failed: " + e); }
                finally { for (int i = 0; i < animators.Length; i++) if (animators[i]) animators[i].cullingMode = modes[i]; }
            }
            EditorApplication.update += Tick;
        }

        static void Shoot()
        {
            if (!Application.isPlaying) return;
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_downloads", "previews");
            Directory.CreateDirectory(dir);
            var report = new System.Text.StringBuilder();

            var holders = Object.FindObjectsByType<WeaponHolder>(FindObjectsSortMode.None)
                .Where(h => h.isActiveAndEnabled && h.Body && h.Body.GetComponentInChildren<Animator>() && (h.Active || h.Body.GetComponent<SoldierController>())).ToList();
            var player = Object.FindAnyObjectByType<SoldierController>();
            Vector3 from = player ? player.transform.position : Vector3.zero;
            // the player first, then one soldier for every other weapon, nearest first
            var picks = holders.OrderBy(h => player && h.Body == player.transform ? -1f : Vector3.Distance(h.Body.position, from))
                               .GroupBy(h => player && h.Body == player.transform ? "player" : h.Active ? h.Active.displayName : "none")
                               .Select(g => g.First()).Take(8).ToList();

            var rt = new RenderTexture(900, 900, 24);
            var tex = new Texture2D(900, 900, TextureFormat.RGB24, false);
            void Save(Camera c, string file)
            {
                var keep = c.targetTexture; c.targetTexture = rt; c.Render(); c.targetTexture = keep;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
            }

            // what the player sees (first person: his own hands on the gun)
            var main = player && player.cam ? player.cam : Camera.main;   // the player's own camera (Camera.main can be another one)
            if (main && player) Save(main, "hands_view_player.png");

            var camGo = new GameObject("HandPoseCam"); var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.35f, 0.38f, 0.42f);
            cam.cullingMask = main ? main.cullingMask : ~0;
            var lightGo = new GameObject("HandPoseLight"); var lt = lightGo.AddComponent<Light>();
            lt.type = LightType.Directional; lt.intensity = 2.2f;
            int n = 0;
            foreach (var h in picks)
            {
                Transform b = h.Body;
                string who = (player && b == player.transform ? "player_" : b.name.Replace("(Clone)", "") + "_") + (h.Active ? h.Active.displayName.Replace(" ", "") : h.Gesture != WeaponHolder.HoldGesture.None ? h.Gesture.ToString() : "nothing");
                var st = b.GetComponent<Stance>();
                if (st && st.Current != StanceKind.Stand) who += "_" + st.Current;
                var anim = b.GetComponentInChildren<Animator>();
                var ik = anim ? anim.GetComponent<HandIK>() : null;
                report.AppendLine($"{n:00} {who}: " + (ik ? ik.Report() : "no HandIK"));
                if (player && b == player.transform && main)
                {
                    Vector3 gp = main.transform.InverseTransformPoint(h.transform.position);
                    Vector3 gf = main.transform.InverseTransformDirection(h.transform.forward);
                    Vector3 bf = main.transform.InverseTransformDirection(b.forward);
                    report.AppendLine($"  view: gun pivot {gp:F2} from the eye (x right, y up, z ahead), gun points {Vector3.Angle(gf, Vector3.forward):0} deg off the view ({gf:F2}), body faces {bf:F2}, fp {h.FpBlend:0.00}");
                }
                // aim at the hands (between the two hand bones), from close by
                Vector3 focus = h.transform.position;
                if (anim && anim.isHuman)
                {
                    var rh = anim.GetBoneTransform(HumanBodyBones.RightHand); var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                    var ch = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Spine);
                    if (rh && lh && ch) focus = Vector3.Lerp((rh.position + lh.position) * 0.5f, ch.position, 0.3f);
                }
                foreach (var (tag, offset) in new[] { ("side", b.right * 1.9f + b.forward * 0.3f), ("front", b.forward * 1.7f - b.right * 0.9f), ("top", -b.forward * 0.6f + b.right * 0.5f + b.up * 1.1f) })
                {
                    cam.transform.position = focus + offset + Vector3.up * 0.15f;
                    cam.transform.LookAt(focus);
                    lightGo.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position + Vector3.down * 1.5f);
                    Save(cam, $"hands_{n:00}_{who}_{tag}.png");
                }
                n++;
            }
            Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); rt.Release();
            try { File.WriteAllText(Path.Combine(dir, "hands_report.txt"), report.ToString()); }
            catch (IOException e) { Debug.LogWarning("Stan's Game: couldn't write hands_report.txt (" + e.Message + ")"); }
            Debug.Log($"Stan's Game: {n} hand-pose subjects captured to {dir}\n{report}");
        }
    }
}
