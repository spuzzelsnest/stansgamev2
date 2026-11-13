using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// Pauses the local player while a menu window is open in the field (pause menu, settings, controls):
    /// the controller is switched off, so a click on a button never fires the weapon, and the cursor is
    /// free. Playing alone also stops time and the game's sound, so nothing happens while the menu is
    /// open; in co-op the fight goes on.
    /// </summary>
    public static class GamePause
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo modeField = typeof(NetSession).GetField("mode", Any);
        static readonly HashSet<string> holders = new HashSet<string>();
        static float keepScale = 1f;

        /// <summary>The player whose controller was switched off, or null.</summary>
        public static SoldierController PausedPlayer { get; private set; }

        /// <summary>True while time is stopped (playing alone with a menu open).</summary>
        public static bool TimeStopped { get; private set; }

        /// <summary>True while any menu window holds the pause.</summary>
        public static bool Held { get { return holders.Count > 0; } }

        /// <summary>True when this PC plays the mission on its own (no co-op host or client).</summary>
        public static bool Alone
        {
            get
            {
                NetSession ns = NetSession.Instance;
                if (ns == null || !Net.Simulates || modeField == null) return false;
                string mode = modeField.GetValue(ns) as string;
                return mode != null && mode.StartsWith("Playing alone");
            }
        }

        /// <summary>The session's own status line ("Playing alone", "Hosting ... join code"), or null.</summary>
        public static string Mode
        {
            get
            {
                NetSession ns = NetSession.Instance;
                return ns != null && modeField != null ? modeField.GetValue(ns) as string : null;
            }
        }

        /// <summary>Takes (<paramref name="on"/>) or gives back the pause for one window.</summary>
        public static void Set(string who, bool on)
        {
            if (on) holders.Add(who); else holders.Remove(who);
            Apply();
        }

        /// <summary>Gives back every hold and restores time, sound and the controller (leaving the level).</summary>
        public static void Clear()
        {
            holders.Clear();
            Apply();
        }

        /// <summary>Called every frame: follows a respawn and keeps the state right.</summary>
        public static void Apply()
        {
            SoldierController p = AdditionsBootstrap.LocalPlayer();
            if (holders.Count > 0)
            {
                if (PausedPlayer != null && PausedPlayer != p) ReleasePlayer();
                if (p != null && p.enabled && !Net.InMenu)
                {
                    p.enabled = false;
                    PausedPlayer = p;
                }
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                bool stop = !Net.InMenu && Alone && !CinematicDirector.Playing;
                if (stop && !TimeStopped)
                {
                    keepScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                    Time.timeScale = 0f;
                    AudioListener.pause = true;
                    TimeStopped = true;
                }
                else if (!stop && TimeStopped) Resume();
            }
            else
            {
                bool had = PausedPlayer != null || TimeStopped;
                ReleasePlayer();
                if (TimeStopped) Resume();
                if (had && !Net.InMenu)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        static void Resume()
        {
            Time.timeScale = keepScale;
            AudioListener.pause = false;
            TimeStopped = false;
        }

        static void ReleasePlayer()
        {
            if (PausedPlayer != null) PausedPlayer.enabled = true;
            PausedPlayer = null;
        }
    }

    /// <summary>
    /// The pause menu in the field, in place of the game's own small Esc box (<c>NetSession</c>), which
    /// did not pause and put "End the game" under the crosshair. Esc or the gamepad's Start opens it:
    /// Resume, Settings, Controls and Leave mission (with a confirmation). Playing alone it stops time.
    /// Also draws the session line top right ("Playing alone", or the co-op host's address and join code)
    /// in the house style, as the game's own line is switched off with its menu.
    /// The game's <c>NetSession</c> component is switched off while a level runs and back on in the
    /// start menu and when leaving; its network callbacks keep working while it is off.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo showMenuField = typeof(NetSession).GetField("showMenu", Any);
        static readonly FieldInfo statusField = typeof(NetSession).GetField("status", Any);
        static readonly FieldInfo leavingField = typeof(NetSession).GetField("leaving", Any);
        static readonly MethodInfo leaveMethod = typeof(NetSession).GetMethod("Leave", Any);
        static readonly FieldInfo phaseStartField = typeof(MissionDirector).GetField("phaseStart", Any);

        static PauseMenu instance;

        /// <summary>True while the pause menu is on screen.</summary>
        public static bool Open { get; private set; }

        /// <summary>Bottom of the session line(s) top right, in pixels, for HUD lines placed under it.</summary>
        public static float StatusBottom { get; private set; }

        int focus;
        bool confirmLeave;
        int confirmFocus;
        float openedAt;
        Vector2 lastMouse = new Vector2(-1f, -1f);
        bool owned;   // NetSession switched off by this script

        static readonly string[] Items = { "RESUME", "SETTINGS", "CONTROLS", "LEAVE MISSION" };

        void Awake()
        {
            instance = this;
            StatusBottom = 40f;
        }

        static bool InField
        {
            get { return NetSession.Instance != null && !Net.InMenu && Net.Active; }
        }

        bool Leaving
        {
            get { NetSession ns = NetSession.Instance; return ns != null && leavingField != null && (bool)leavingField.GetValue(ns); }
        }

        void Update()
        {
            NetSession ns = NetSession.Instance;
            if (InField && !Leaving)
            {
                // The game's own menu and status line are replaced by this one.
                if (ns.enabled)
                {
                    if (showMenuField != null) showMenuField.SetValue(ns, false);
                    ns.enabled = false;
                    owned = true;
                }
            }
            else
            {
                if (Open) SetOpenNow(false);
                if (owned && ns != null && !ns.enabled && !Net.InMenu) ns.enabled = true;   // MainMenu runs it in the start menu
                owned = false;
            }

            if (InField) GamePause.Apply();
            if (!InField) return;
            if (SettingsPanel.WindowOpen || ControlsPage.Open) return;

            Keyboard k = Keyboard.current;
            Gamepad g = Gamepad.current;
            if (!Open)
            {
                bool esc = (k != null && k.escapeKey.wasPressedThisFrame) || (g != null && g.startButton.wasPressedThisFrame);
                if (esc && !CinematicDirector.Playing && !GliderExit.Exiting && Ui.TakeBack(true)) SetOpenNow(true);
                return;
            }

            if (Ui.TakeBack(true))
            {
                if (confirmLeave) { confirmLeave = false; Ui.PlayBack(); }
                else { Ui.PlayBack(); SetOpenNow(false); }
                return;
            }
            Vector2Int nav = Ui.Nav();
            if (confirmLeave)
            {
                if (nav.x != 0 || nav.y != 0) { confirmFocus = 1 - confirmFocus; Ui.PlayTick(); }
                if (Ui.TakeOk()) Confirm(confirmFocus == 0);
                return;
            }
            if (nav.y != 0) { focus = (focus + nav.y + Items.Length) % Items.Length; Ui.PlayTick(); }
            if (Ui.TakeOk()) Activate(focus);
        }

        /// <summary>Opens or closes the pause menu (for tests and other scripts).</summary>
        public static void SetOpen(bool open)
        {
            if (instance != null) instance.SetOpenNow(open);
        }

        void SetOpenNow(bool open)
        {
            if (open == Open) return;
            if (open && (!InField || CinematicDirector.Playing)) return;
            Open = open;
            confirmLeave = false;
            if (open)
            {
                focus = 0;
                openedAt = Time.unscaledTime;
                Ui.PlayChoose();
            }
            GamePause.Set("pause", open);
        }

        void Activate(int i)
        {
            Ui.PlayChoose();
            switch (i)
            {
                case 0: SetOpenNow(false); break;
                case 1: SettingsPanel.Show(true); break;
                case 2: ControlsPage.Show(true); break;
                case 3: confirmLeave = true; confirmFocus = 1; break;   // "Stay" first
            }
        }

        void Confirm(bool leave)
        {
            if (!leave) { confirmLeave = false; Ui.PlayBack(); return; }
            Ui.PlayChoose();
            NetSession ns = NetSession.Instance;
            confirmLeave = false;
            Open = false;
            GamePause.Clear();   // time back on: the game's leave routine waits in game time
            if (ns == null || leaveMethod == null) { Debug.LogError("Stan's Game: NetSession.Leave not found"); return; }
            ns.enabled = true;
            owned = false;
            leaveMethod.Invoke(ns, null);
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            GUI.depth = -150;   // over the HUD and the main menu, under the settings and controls windows
            if (!InField) return;
            Ui.ApplyGameFont();
            Event e = Event.current;
            DrawStatus();
            if (!Open) return;

            float s = Ui.MenuScale;
            float fade = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.2f);
            Ui.Backdrop(fade);
            if (SettingsPanel.WindowOpen || ControlsPage.Open) return;

            float x0 = 140f * s, y = 150f * s;
            Ui.Text(new Rect(x0, y, 900f * s, 90f * s), "PAUSED", UiFont.Display, 84f * s, Ui.A(Ui.Paper, fade), TextAnchor.MiddleLeft);
            y += 92f * s;
            MissionDirector md = MissionDirector.Instance;
            string where = MissionWatch.Current.Title.ToUpperInvariant();
            if (md != null) where += "   ·   " + md.ClockText;
            where += "   ·   " + (GamePause.TimeStopped ? "TIME STOPPED" : "THE FIGHT GOES ON (CO-OP)");
            Ui.Text(new Rect(x0 + 4f * s, y, 1200f * s, 34f * s), where, UiFont.Body, 22f * s, Ui.A(Ui.Brass, fade), TextAnchor.MiddleLeft);
            y += 44f * s;
            Ui.Rule(x0 + 4f * s, y, 90f * s, s, fade);
            y += 50f * s;

            for (int i = 0; i < Items.Length; i++)
            {
                Rect r = new Rect(x0 - 30f * s, y + i * 64f * s, 560f * s, 52f * s);
                if (!confirmLeave)
                {
                    if (e.type == EventType.Repaint && r.Contains(e.mousePosition) && (e.mousePosition - lastMouse).sqrMagnitude > 1f && focus != i) { focus = i; Ui.PlayTick(); }
                    if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { e.Use(); focus = i; Activate(i); }
                }
                bool on = focus == i && !confirmLeave;
                if (on)
                {
                    Ui.Fill(r, Ui.A(Ui.Paper, 0.07f * fade));
                    Ui.Fill(new Rect(r.x, r.y + 8f * s, 5f * s, r.height - 16f * s), Ui.A(Ui.Brass, fade));
                }
                string label = i == 3 && !GamePause.Alone ? (Net.Simulates ? "END THE GAME" : "LEAVE THE GAME") : Items[i];
                Ui.Text(new Rect(x0 + (on ? 12f * s : 0f), r.y, 520f * s, r.height), label, UiFont.Body, 34f * s,
                        Ui.A(Ui.Paper, (on ? 1f : 0.55f) * fade), TextAnchor.MiddleLeft, false, false, true);
            }
            if (e.type == EventType.Repaint) lastMouse = e.mousePosition;

            float helpY = y + Items.Length * 64f * s + 20f * s;
            string help = focus == 0 ? "Back to the fight."
                        : focus == 1 ? "Video, audio, gameplay and controls."
                        : focus == 2 ? "Every key, as printed on this keyboard."
                        : GamePause.Alone ? "Back to the main menu. This attempt at the mission is lost."
                        : Net.Simulates ? "Ends the game for everyone and goes back to the main menu."
                        : "Leaves the host's game and goes back to the main menu.";
            Ui.Text(new Rect(x0, helpY, 700f * s, 30f * s), help, UiFont.Body, 19f * s, Ui.A(Ui.Dim, fade), TextAnchor.UpperLeft);

            if (md != null) DrawOrders(md, s, fade);
            if (confirmLeave) DrawConfirm(s, e);

            float fy = Screen.height - 64f * s;
            Ui.Hints(x0, fy, 30f * s, new string[] { Ui.Prompt("↑ ↓", "D-PAD"), "select", Ui.Prompt("ENTER", "A"), "confirm", Ui.Prompt("ESC", "B"), "resume" }, 0.8f * fade);
        }

        /// <summary>The current orders, typed on a sheet, right of the menu.</summary>
        void DrawOrders(MissionDirector md, float s, float fade)
        {
            if (md.phases == null || md.CurrentPhase < 0 || md.CurrentPhase >= md.phases.Length) return;
            MissionDirector.Phase ph = md.phases[md.CurrentPhase];
            float w = Mathf.Min(620f * s, Screen.width - 900f * s);
            if (w < 300f * s) return;
            float x = Screen.width - w - 120f * s, y = 250f * s, pad = 30f * s;
            string goal = ph.objective ?? "";
            string prog = ObjectiveHud.ProgressText(md);
            float gh = Ui.Height(goal, UiFont.Type, 21f * s, w - 2f * pad, true);
            float ph2 = Ui.Height(prog, UiFont.Type, 19f * s, w - 2f * pad);
            float h = pad * 2f + 34f * s + 44f * s + gh + (string.IsNullOrEmpty(prog) ? 0f : ph2 + 18f * s) + 40f * s;
            Rect card = new Rect(x, y, w, h);
            Ui.PaperCard(card, fade, s);
            float cy = y + pad;
            Ui.Text(new Rect(x + pad, cy, w - 2f * pad, 30f * s), "ORDERS   ·   PHASE " + (md.CurrentPhase + 1) + " OF " + md.phases.Length, UiFont.Type, 17f * s, Ui.A(Ui.Ink, 0.7f * fade), TextAnchor.MiddleLeft, false, false, true);
            cy += 34f * s;
            Ui.Text(new Rect(x + pad, cy, w - 2f * pad, 40f * s), (ph.title ?? "").ToUpperInvariant(), UiFont.Display, 32f * s, Ui.A(Ui.Ink, fade), TextAnchor.MiddleLeft);
            cy += 44f * s;
            Ui.Text(new Rect(x + pad, cy, w - 2f * pad, gh), goal, UiFont.Type, 21f * s, Ui.A(Ui.Ink, fade), TextAnchor.UpperLeft, false, true, true);
            cy += gh + 18f * s;
            if (!string.IsNullOrEmpty(prog))
                Ui.Text(new Rect(x + pad, cy, w - 2f * pad, ph2), prog, UiFont.Type, 19f * s, Ui.A(new Color(0.42f, 0.14f, 0.08f), fade), TextAnchor.UpperLeft, false, true);
            float since = phaseStartField != null && Net.Simulates ? Time.time - (float)phaseStartField.GetValue(md) : -1f;
            if (since >= 0f)
                Ui.Text(new Rect(x + pad, card.yMax - pad - 24f * s, w - 2f * pad, 24f * s), "In this phase for " + Progress.FormatTime(since), UiFont.Type, 16f * s, Ui.A(Ui.Ink, 0.6f * fade), TextAnchor.MiddleRight);
        }

        void DrawConfirm(float s, Event e)
        {
            Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.45f));
            float w = 640f * s, h = 250f * s;
            Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            Ui.Panel(r, 1f, s);
            Ui.Fill(r, new Color(0f, 0f, 0f, 0.35f));
            bool alone = GamePause.Alone;
            string q = alone ? "LEAVE THE MISSION?" : Net.Simulates ? "END THE GAME FOR EVERYONE?" : "LEAVE THE HOST'S GAME?";
            Ui.Text(new Rect(r.x, r.y + 28f * s, w, 44f * s), q, UiFont.Display, 34f * s, Ui.Paper, TextAnchor.MiddleCenter);
            Ui.Text(new Rect(r.x + 30f * s, r.y + 80f * s, w - 60f * s, 30f * s),
                alone ? "You go back to the main menu. This attempt is lost." : "Everyone goes back to the main menu.",
                UiFont.Body, 19f * s, Ui.Dim, TextAnchor.MiddleCenter);
            string[] b = { alone ? "LEAVE" : "END", "STAY" };
            float bw = 220f * s, bh = 56f * s, gap = 30f * s;
            float bx = r.x + (w - 2f * bw - gap) * 0.5f, by = r.yMax - bh - 34f * s;
            for (int i = 0; i < 2; i++)
            {
                Rect br = new Rect(bx + i * (bw + gap), by, bw, bh);
                if (e.type == EventType.Repaint && br.Contains(e.mousePosition) && (e.mousePosition - lastMouse).sqrMagnitude > 1f && confirmFocus != i) { confirmFocus = i; Ui.PlayTick(); }
                if (e.type == EventType.MouseDown && e.button == 0 && br.Contains(e.mousePosition)) { e.Use(); confirmFocus = i; Confirm(i == 0); return; }
                bool on = confirmFocus == i;
                Ui.Fill(br, on ? Ui.A(i == 0 ? Ui.Red : Ui.Brass, 0.9f) : Ui.A(Ui.Paper, 0.10f));
                Ui.Text(br, b[i], UiFont.Body, 26f * s, on ? Ui.Ink : Ui.Paper, TextAnchor.MiddleCenter, false, false, true);
            }
        }

        /// <summary>"Playing alone" (or the co-op line) top right, with the Esc hint, in place of the game's line.</summary>
        void DrawStatus()
        {
            if (CinematicDirector.Playing || Event.current.type != EventType.Repaint) return;
            float hs = Ui.HudScale, m = Ui.HudMargin;
            string mode = GamePause.Mode;
            NetSession ns = NetSession.Instance;
            string status = ns != null && statusField != null ? statusField.GetValue(ns) as string : null;
            float y = m * 0.75f, line = 22f * hs;
            float right = Screen.width - m;
            if (!string.IsNullOrEmpty(mode))
            {
                string text = GamePause.Alone ? "PLAYING ALONE" : mode;
                float w = Ui.Hints(right, y, line, new string[] { KeyLabel.Of(Key.Escape), "menu" }, 0.85f, TextAnchor.MiddleRight);
                Ui.Text(new Rect(right - w - 520f * hs - 14f * hs, y, 520f * hs, line), text, UiFont.Body, 15f * hs, Ui.A(Ui.Paper, 0.8f), TextAnchor.MiddleRight, true, false, true);
                y += line + 4f * hs;
            }
            if (!string.IsNullOrEmpty(status))
            {
                Ui.Text(new Rect(right - 700f * hs, y, 700f * hs, line), status, UiFont.Body, 15f * hs, Ui.A(Ui.Amber, 0.9f), TextAnchor.MiddleRight, true);
                y += line + 4f * hs;
            }
            StatusBottom = y + 4f * hs;
        }
    }
}
