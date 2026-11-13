using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// Makes the current objective clear in game, next to the game's own objective text (top left):
    /// a marker on the objective with what to do and how far it is (clamped to the screen edge when it
    /// is behind or off screen), a "New objective" banner with a sound when the phase changes, and a
    /// progress line under the objective (defenders left, signals sent, time to hold).
    /// Targets come from the mission itself: the nearest east-bank defender, the bridge objective,
    /// the lead panzer.
    /// </summary>
    public sealed class ObjectiveHud : MonoBehaviour
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo phaseStartField = typeof(MissionDirector).GetField("phaseStart", Any);
        static readonly Color Accent = new Color(0.88f, 0.74f, 0.45f);

        static ObjectiveHud instance;
        MissionDirector watched;
        int shownPhase = -1;
        float bannerAt = -100f;
        bool pending;

        AudioSource sfx;
        AudioClip sting;
        static Texture2D white;
        static GUIStyle small, big, body, marker, progress;

        /// <summary>The phase whose banner was shown last (for tests).</summary>
        public static int BannerPhase { get { return instance != null ? instance.shownPhase : -1; } }

        void Awake()
        {
            instance = this;
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            sting = Sting();
        }

        /// <summary>Shows the banner for the current phase now (after the glider exit).</summary>
        public static void Announce()
        {
            if (instance != null) instance.pending = true;
        }

        void Update()
        {
            MissionDirector md = MissionDirector.Instance;
            if (md != watched) { watched = md; shownPhase = -1; pending = false; }
            if (md == null || Net.InMenu || md.phases == null || Bootcamp.Active) return;
            SoldierController p = AdditionsBootstrap.LocalPlayer();
            bool playing = p != null && p.enabled && !CinematicDirector.Playing && !GliderExit.Exiting;
            if (!playing) return;
            int phase = md.CurrentPhase;
            if (phase != shownPhase || pending)
            {
                pending = false;
                shownPhase = phase;
                bannerAt = Time.time;
                if (sting != null) sfx.PlayOneShot(sting, 0.55f);
            }
        }

        // ------------------------------------------------------------------ targets

        /// <summary>Phase 1 target: the nearest east-bank defender still alive, else the bridge.</summary>
        public static Vector3 PhaseZeroTarget(MissionDirector md, Vector3 from)
        {
            Transform t = NearestDefender(md, from);
            if (t != null) return t.position;
            if (md.bridgeObjective != null) return md.bridgeObjective.position;
            return from + Vector3.forward * 10f;
        }

        static Transform NearestDefender(MissionDirector md, Vector3 from)
        {
            IList list = md.eastBankDefenders as IList;
            if (list == null) return null;
            Transform best = null;
            float bd = float.MaxValue;
            foreach (object o in list)
            {
                Transform t = o is Component ? ((Component)o).transform : o is GameObject ? ((GameObject)o).transform : null;
                if (t == null) continue;
                Health h = t.GetComponent<Health>();
                if (h != null && h.IsDead) continue;
                float d = (t.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }

        static int DefendersLeft(MissionDirector md)
        {
            IList list = md.eastBankDefenders as IList;
            return list == null ? -1 : list.Count;
        }

        bool Target(MissionDirector md, Vector3 from, out Vector3 pos, out string verb)
        {
            pos = Vector3.zero;
            verb = null;
            switch (md.CurrentPhase)
            {
                case 0:
                    Transform d = NearestDefender(md, from);
                    if (d == null) return false;
                    pos = d.position; verb = "ATTACK"; return true;
                case 1:
                    if (md.bridgeObjective == null) return false;
                    pos = md.bridgeObjective.position; verb = "TAKE"; return true;
                case 3:
                case 5:
                    if (md.bridgeObjective == null) return false;
                    pos = md.bridgeObjective.position; verb = "DEFEND"; return true;
                case 4:
                    if (md.panzer == null || md.panzer.IsDead) return false;
                    pos = md.panzer.transform.position; verb = "DESTROY"; return true;
            }
            return false;
        }

        string ProgressLine(MissionDirector md)
        {
            float since = phaseStartField != null && Net.Simulates ? Time.time - (float)phaseStartField.GetValue(md) : -1f;
            switch (md.CurrentPhase)
            {
                case 0:
                    int n = DefendersLeft(md);
                    return n < 0 ? null : n == 1 ? "1 defender left on the east bank" : n + " defenders left on the east bank";
                case 1:
                    return "Get across the bridge to the café on the west bank";
                case 2:
                    return "Hold " + KeyLabel.Of(Key.H) + " to transmit:  " + md.SignalsSent + " / " + md.signalsNeeded + " sent";
                case 3:
                    return since < 0f ? "Hold the bridge" : "Hold the bridge:  " + HoldTime(md.tanksDelay - since);
                case 4:
                    return "Switch to the PIAT (" + KeyLabel.Of(Key.Digit3) + ") and hit the lead tank";
                case 5:
                    return since < 0f ? "Hold on" : "Commandos arrive in  " + HoldTime(md.holdOnSeconds - since);
            }
            return null;
        }

        static string HoldTime(float seconds)
        {
            return Progress.FormatTime(Mathf.Max(0f, seconds));
        }

        // ------------------------------------------------------------------ drawing

        static void Styles()
        {
            if (white == null)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (small != null) return;
            small = new GUIStyle(GUI.skin.label);
            small.alignment = TextAnchor.MiddleCenter;
            small.fontStyle = FontStyle.Bold;
            small.normal.textColor = Color.white;
            big = new GUIStyle(small);
            body = new GUIStyle(small); body.fontStyle = FontStyle.Normal; body.wordWrap = true;
            marker = new GUIStyle(small);
            progress = new GUIStyle(small); progress.alignment = TextAnchor.MiddleLeft; progress.fontStyle = FontStyle.Normal;
        }

        void OnGUI()
        {
            GUI.depth = -5;   // set on every event: Unity sorts scripts on the Layout event
            if (Event.current.type != EventType.Repaint) return;
            if (Bootcamp.Active) return;
            MissionDirector md = MissionDirector.Instance;
            SoldierController p = AdditionsBootstrap.LocalPlayer();
            if (md == null || p == null || !p.enabled || Net.InMenu || CinematicDirector.Playing || GliderExit.Exiting || SettingsPanel.Open) return;
            if (md.phases == null || md.CurrentPhase < 0 || md.CurrentPhase >= md.phases.Length) return;
            Styles();
            float s = Mathf.Clamp(Screen.height / 1080f, 0.8f, 1.4f);
            small.fontSize = Mathf.RoundToInt(16f * s);
            big.fontSize = Mathf.RoundToInt(40f * s);
            body.fontSize = Mathf.RoundToInt(19f * s);
            marker.fontSize = Mathf.RoundToInt(14f * s);
            progress.fontSize = 15;   // matches the game's own objective line, which is not scaled
            Color keep = GUI.color;

            // Progress line under the game's objective text (20,44).
            string prog = ProgressLine(md);
            if (!string.IsNullOrEmpty(prog))
            {
                GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.Label(new Rect(37f, 73f, 700f, 22f), prog, progress);
                GUI.color = Accent;
                GUI.DrawTexture(new Rect(20f, 78f, 4f, 13f), white);
                GUI.Label(new Rect(36f, 72f, 700f, 22f), prog, progress);
            }

            // Marker
            Camera cam = p.cam != null ? p.cam : Camera.main;
            Vector3 target;
            string verb;
            if (cam != null && Target(md, p.transform.position, out target, out verb))
            {
                float dist = Vector3.Distance(p.transform.position, target);
                if (dist > 4f) DrawMarker(cam, target + Vector3.up * 2.2f, verb, dist, s);
            }

            // Banner
            float age = Time.time - bannerAt;
            if (age < 5.5f)
            {
                MissionDirector.Phase ph = md.phases[md.CurrentPhase];
                float a = Mathf.Clamp01(age / 0.35f) * Mathf.Clamp01((5.5f - age) / 0.8f);
                float w = Mathf.Min(900f * s, Screen.width - 40f);
                float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.29f;   // clear of the objective lines and the game's own message (H*0.22)
                float bodyH = body.CalcHeight(new GUIContent(ph.objective ?? ""), w - 40f * s);
                float h = 110f * s + bodyH;
                GUI.color = new Color(0f, 0f, 0f, 0.6f * a);
                GUI.DrawTexture(new Rect(x, y, w, h), white);
                GUI.color = new Color(Accent.r, Accent.g, Accent.b, a);
                GUI.DrawTexture(new Rect(x, y, w, 3f * s), white);
                GUI.Label(new Rect(x, y + 10f * s, w, 24f * s), md.CurrentPhase == md.phases.Length - 1 ? "MISSION" : "NEW OBJECTIVE", small);
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.Label(new Rect(x, y + 34f * s, w, 50f * s), (ph.title ?? "").ToUpperInvariant(), big);
                GUI.color = new Color(1f, 1f, 1f, 0.85f * a);
                GUI.Label(new Rect(x + 20f * s, y + 90f * s, w - 40f * s, bodyH), ph.objective ?? "", body);
            }
            GUI.color = keep;
        }

        void DrawMarker(Camera cam, Vector3 world, string verb, float dist, float s)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            bool behind = sp.z < 0f;
            Vector2 c = new Vector2(sp.x, Screen.height - sp.y);
            if (behind) c = new Vector2(Screen.width - c.x, Screen.height - c.y);
            float m = 70f * s;
            Rect safe = new Rect(m, m + 60f, Screen.width - 2f * m, Screen.height - 2f * m - 160f);
            bool edge = behind || !safe.Contains(c);
            if (edge)
            {
                Vector2 mid = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Vector2 dir = (c - mid);
                if (behind && dir.sqrMagnitude < 1f) dir = Vector2.down;
                float k = Mathf.Min(Mathf.Abs((safe.width * 0.5f) / Mathf.Max(0.001f, Mathf.Abs(dir.x))),
                                    Mathf.Abs((safe.height * 0.5f) / Mathf.Max(0.001f, Mathf.Abs(dir.y))));
                c = mid + dir * Mathf.Min(1f, k);
                c.x = Mathf.Clamp(c.x, safe.xMin, safe.xMax);
                c.y = Mathf.Clamp(c.y, safe.yMin, safe.yMax);
            }

            float size = 16f * s;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 4f);
            Matrix4x4 keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, c);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(c.x - size * 0.5f - 2f, c.y - size * 0.5f - 2f, size + 4f, size + 4f), white);
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, pulse);
            GUI.DrawTexture(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), white);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(c.x - size * 0.2f, c.y - size * 0.2f, size * 0.4f, size * 0.4f), white);
            GUI.matrix = keep;

            if (edge)
            {
                // Small arrow pointing the way, outside the diamond.
                Vector2 mid = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Vector2 dir = (c - mid).normalized;
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                GUIUtility.RotateAroundPivot(ang, c);
                GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.9f);
                GUI.DrawTexture(new Rect(c.x + size * 0.9f, c.y - 2f * s, 10f * s, 4f * s), white);
                GUI.matrix = keep;
            }

            string text = verb + "   " + Mathf.RoundToInt(dist) + " m";
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.Label(new Rect(c.x - 99f * s, c.y + size * 0.9f + 1f, 200f * s, 20f * s), text, marker);
            GUI.color = Color.white;
            GUI.Label(new Rect(c.x - 100f * s, c.y + size * 0.9f, 200f * s, 20f * s), text, marker);
        }

        static AudioClip Sting()
        {
            int rate = 44100;
            float len = 0.7f;
            int n = Mathf.CeilToInt(rate * len);
            float[] d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float f = t < 0.18f ? 392f : 587.3f;      // G4 then D5, a short bugle-like call
                float local = t < 0.18f ? t : t - 0.18f;
                float env = Mathf.Min(1f, local * 60f) * Mathf.Exp(-local * (t < 0.18f ? 9f : 4.5f));
                float v = Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * f * t) + 0.15f * Mathf.Sin(6f * Mathf.PI * f * t);
                d[i] = v * env * 0.3f;
            }
            AudioClip c = AudioClip.Create("objective sting", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
