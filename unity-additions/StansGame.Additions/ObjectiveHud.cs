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

        static ObjectiveHud instance;
        MissionDirector watched;
        int shownPhase = -1;
        float bannerAt = -100f;
        bool pending;

        AudioSource sfx;
        AudioClip sting;

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
            if (md != watched) { watched = md; shownPhase = -1; pending = false; piatText = null; }
            if (md != null && md.phases != null) FixPiatText(md);
            if (md == null || Net.InMenu || md.phases == null) return;
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
                    if (PiatKey(md) == 0)
                    {
                        Pickup piat = PiatPickup();
                        if (piat != null) { pos = piat.transform.position; verb = "TAKE THE PIAT"; return true; }
                    }
                    if (md.panzer == null || md.panzer.IsDead) return false;
                    pos = md.panzer.transform.position; verb = "DESTROY"; return true;
            }
            return false;
        }

        /// <summary>The progress line for the current phase (also shown on the pause menu's orders).</summary>
        public static string ProgressText(MissionDirector md)
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
                    int key = PiatKey(md);
                    if (key > 0) return "Switch to the PIAT (" + KeyLabel.Of(Key.Digit1 + key - 1) + ") and hit the lead vehicle";
                    return PiatPickup() != null ? "Pick up the PIAT (" + KeyLabel.Of(Key.E) + ")" : "Wait for the PIAT to be brought up";
                case 5:
                    return since < 0f ? "Hold on" : "Commandos arrive in  " + HoldTime(md.holdOnSeconds - since);
            }
            return null;
        }

        static string HoldTime(float seconds)
        {
            return Progress.FormatTime(Mathf.Max(0f, seconds));
        }

        // ------------------------------------------------------------------ PIAT (phase 4)

        const string PiatOriginal = "German armour coming from Bénouville. Stop the lead vehicle with the PIAT (3).";
        string piatText;   // the game's own phase 4 objective, when it is the known one

        /// <summary>The weapon key (1 to 4) of the PIAT in the local player's kit, or 0 before the handover.</summary>
        public static int PiatKey(MissionDirector md)
        {
            SoldierController p = AdditionsBootstrap.LocalPlayer();
            if (p == null) return 0;
            Loadout lo = p.GetComponent<Loadout>();
            if (lo != null && lo.primary != null && lo.primary.hold == HoldStyle.Launcher) return 1;
            if (lo != null && lo.sidearm != null && lo.sidearm.hold == HoldStyle.Launcher) return 2;
            if (p.weapons != null)
                foreach (Weapon w in p.weapons)
                    if (w != null && w.hold == HoldStyle.Launcher) return 1;
            return 0;
        }

        /// <summary>The PIAT lying ready to be picked up, or null.</summary>
        public static Pickup PiatPickup()
        {
            foreach (Pickup q in Pickup.All)
                if (q != null && q.weapon != null && q.weapon.hold == HoldStyle.Launcher) return q;
            return null;
        }

        /// <summary>
        /// The game's phase 4 objective sends the player to "(3)", which is the Mills bomb until the PIAT is
        /// handed over (and the PIAT then takes the main weapon's place). The text follows the handover.
        /// </summary>
        void FixPiatText(MissionDirector md)
        {
            if (md.phases.Length <= 4 || md.phases[4] == null) return;
            if (piatText == null)
            {
                if (md.phases[4].objective != PiatOriginal) return;
                piatText = md.phases[4].objective;
            }
            string want;
            if (md.CurrentPhase != 4 || Net.InMenu) want = "German armour coming from Bénouville. Stop the lead vehicle with the PIAT.";
            else
            {
                int key = PiatKey(md);
                want = key > 0
                    ? "German armour coming from Bénouville. Stop the lead vehicle with the PIAT (" + KeyLabel.Of(Key.Digit1 + key - 1) + ")."
                    : "German armour coming from Bénouville. Take the PIAT and stop the lead vehicle.";
            }
            if (md.phases[4].objective != want) md.phases[4].objective = want;
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            GUI.depth = -5;   // set on every event: Unity sorts scripts on the Layout event
            if (Event.current.type != EventType.Repaint) return;
            MissionDirector md = MissionDirector.Instance;
            if (md == null || Net.InMenu || CinematicDirector.Playing) return;
            if (md.phases == null || md.CurrentPhase < 0 || md.CurrentPhase >= md.phases.Length) return;
            Ui.ApplyGameFont();
            float s = Ui.HudScale;

            // The orders block top left, over the game's own two lines there (always, as the game draws them).
            DrawOrders(md, s);

            SoldierController p = AdditionsBootstrap.LocalPlayer();
            if (p == null || !p.enabled || GliderExit.Exiting || SettingsPanel.Open) return;

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
            if (age < 5.5f) DrawBanner(md, age, s);
        }

        static GUIStyle measure;

        void DrawOrders(MissionDirector md, float s)
        {
            MissionDirector.Phase ph = md.phases[md.CurrentPhase];
            float m = Ui.HudMargin;
            string clock = md.ClockText;
            string title = (ph.title ?? "").ToUpperInvariant();
            string goal = ph.objective ?? "";
            string prog = ProgressText(md);

            // The game's lines: "<b>clock</b> 6 June 1944 - title" (20, 16, size 20) and the objective
            // (20, 44, size 16), in the default skin's font. Measure them so the plate always covers them.
            if (measure == null) { measure = new GUIStyle(GUI.skin.label); measure.richText = true; measure.wordWrap = false; }
            measure.font = GUI.skin.font;
            measure.fontSize = 20;
            float gw1 = measure.CalcSize(new GUIContent("<b>" + clock + "</b> 6 June 1944 — " + (ph.title ?? ""))).x;
            measure.fontSize = 16;
            float gw2 = Mathf.Min(900f, measure.CalcSize(new GUIContent(goal)).x);
            Rect cover = new Rect(12f, 12f, Mathf.Max(gw1, gw2) + 16f, 62f);

            float w = Mathf.Clamp(Mathf.Max(Ui.Width(goal, UiFont.Body, 18f * s), Ui.Width(prog, UiFont.Body, 16f * s) + 16f * s) + 42f * s, 360f * s, 640f * s);
            float pad = 12f * s;
            float lineH = 26f * s;
            float goalH = Ui.Height(goal, UiFont.Body, 18f * s, w - 2f * pad - 6f * s);
            float progH = string.IsNullOrEmpty(prog) ? 0f : 24f * s;
            Rect plate = new Rect(m, m, w, pad + lineH + 4f * s + goalH + (progH > 0f ? 6f * s + progH : 0f) + pad);
            plate = Rect.MinMaxRect(Mathf.Min(plate.xMin, cover.xMin), Mathf.Min(plate.yMin, cover.yMin),
                                    Mathf.Max(plate.xMax, cover.xMax), Mathf.Max(plate.yMax, cover.yMax));
            Ui.Fill(plate, Ui.Night);   // opaque, the game's lines are under it
            Ui.Fill(new Rect(plate.x, plate.y, Mathf.Max(2f, 3f * s), plate.height), Ui.Brass);

            float x = plate.x + pad + 6f * s, y = plate.y + pad;
            float cw = Ui.Width(clock, UiFont.Body, 20f * s, true);
            Ui.Text(new Rect(x, y, cw + 4f, lineH), clock, UiFont.Body, 20f * s, Ui.Brass, TextAnchor.MiddleLeft, false, false, true);
            float dx = x + cw + 10f * s;
            string date = "6 JUNE 1944   ·   ";
            float dw = Ui.Width(date, UiFont.Body, 14f * s);
            Ui.Text(new Rect(dx, y + 2f * s, dw + 4f, lineH), date, UiFont.Body, 14f * s, Ui.Dim, TextAnchor.MiddleLeft);
            Ui.Text(new Rect(dx + dw, y, plate.xMax - dx - dw - pad, lineH), title, UiFont.Body, 18f * s, Ui.Paper, TextAnchor.MiddleLeft, false, false, true);
            y += lineH + 4f * s;
            Ui.Text(new Rect(x, y, plate.xMax - x - pad, goalH), goal, UiFont.Body, 18f * s, Ui.A(Ui.Paper, 0.92f), TextAnchor.UpperLeft, false, true);
            y += goalH;
            if (progH > 0f)
            {
                y += 6f * s;
                Ui.Fill(new Rect(x, y + progH * 0.5f - 1f * s, 10f * s, 2f * s), Ui.Brass);
                Ui.Text(new Rect(x + 16f * s, y, plate.xMax - x - pad - 16f * s, progH), prog, UiFont.Body, 16f * s, Ui.Brass, TextAnchor.MiddleLeft);
            }
        }

        void DrawBanner(MissionDirector md, float age, float s)
        {
            MissionDirector.Phase ph = md.phases[md.CurrentPhase];
            float a = Mathf.Clamp01(age / 0.35f) * Mathf.Clamp01((5.5f - age) / 0.8f);
            float w = Mathf.Min(900f * s, Screen.width - 40f);
            float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.29f;   // clear of the objective lines and the game's own message (H*0.22)
            string goal = ph.objective ?? "";
            float bodyH = Ui.Height(goal, UiFont.Body, 20f * s, w - 80f * s);
            float h = 120f * s + bodyH;
            Ui.Fill(new Rect(x, y, w, h), Ui.A(Ui.Night, 0.82f * a));
            Ui.Fill(new Rect(x, y, w, 2f * s), Ui.A(Ui.Brass, a));
            Ui.Fill(new Rect(x, y + h - 2f * s, w, 2f * s), Ui.A(Ui.Brass, a));
            bool last = md.CurrentPhase == md.phases.Length - 1;
            Ui.Text(new Rect(x, y + 12f * s, w, 24f * s), last ? "MISSION" : "NEW OBJECTIVE", UiFont.Body, 16f * s, Ui.A(Ui.Brass, a), TextAnchor.MiddleCenter, false, false, true);
            Ui.Text(new Rect(x, y + 38f * s, w, 56f * s), (ph.title ?? "").ToUpperInvariant(), UiFont.Display, 46f * s, Ui.A(Ui.Paper, a), TextAnchor.MiddleCenter);
            Ui.Text(new Rect(x + 40f * s, y + 100f * s, w - 80f * s, bodyH), goal, UiFont.Body, 20f * s, Ui.A(Ui.Paper, 0.88f * a), TextAnchor.UpperCenter, false, true);
        }

        void DrawMarker(Camera cam, Vector3 world, string verb, float dist, float s)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            bool behind = sp.z < 0f;
            Vector2 c = new Vector2(sp.x, Screen.height - sp.y);
            if (behind) c = new Vector2(Screen.width - c.x, Screen.height - c.y);
            float m = 70f * s;
            Rect safe = new Rect(m, m + 90f * s, Screen.width - 2f * m, Screen.height - 2f * m - 200f * s);
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
            Ui.Fill(new Rect(c.x - size * 0.5f - 2f, c.y - size * 0.5f - 2f, size + 4f, size + 4f), new Color(0f, 0f, 0f, 0.6f));
            Ui.Fill(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), Ui.A(Ui.Brass, pulse));
            Ui.Fill(new Rect(c.x - size * 0.2f, c.y - size * 0.2f, size * 0.4f, size * 0.4f), new Color(0f, 0f, 0f, 0.8f));
            GUI.matrix = keep;

            if (edge)
            {
                // Small arrow pointing the way, outside the diamond.
                Vector2 mid = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Vector2 dir = (c - mid).normalized;
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                GUIUtility.RotateAroundPivot(ang, c);
                Ui.Fill(new Rect(c.x + size * 0.9f, c.y - 2f * s, 10f * s, 4f * s), Ui.A(Ui.Brass, 0.9f));
                GUI.matrix = keep;
            }

            string text = verb + "   " + Mathf.RoundToInt(dist) + " m";
            Ui.Text(new Rect(c.x - 120f * s, c.y + size * 0.9f, 240f * s, 22f * s), text, UiFont.Body, 15f * s, Ui.Paper, TextAnchor.MiddleCenter, true, false, true);
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
