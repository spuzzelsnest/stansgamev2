using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// Title screen, main menu, level select (with the mission briefing from the level's own phases) and
    /// credits, shown over the level while the game is in its start menu (<c>Net.InMenu</c>). It takes
    /// the place of the game's own connection menu (<c>NetSession</c>), which comes back for Co-op.
    /// Starting a level calls <c>NetSession.PlayAlone</c>, so the Intro film plays exactly as before.
    /// Works with mouse, keyboard (arrows or the move keys, Enter, Esc) and gamepad (d-pad, A, B).
    /// After Esc &gt; Leave the game reloads the level and the main menu opens again.
    /// This is the drop-in version for the compiled demo; with the Unity source it becomes the
    /// UI Toolkit screens of the game structure plan (phase 2).
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        enum Page { None, Title, Main, Levels, Role, Credits, Coop }

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly MethodInfo playAlone = typeof(NetSession).GetMethod("PlayAlone", Any);

        static readonly string[] MainItems = { "PLAY", "LEVEL SELECT", "CO-OP", "SETTINGS", "CREDITS", "QUIT" };
        static readonly string[] MainHelp =
        {
            "", // filled in with the level to continue
            "Pick a mission. Finish one to open the next.",
            "Play with friends: host on your home network or online with a join code.",
            "Video, audio, gameplay and controls, with every key on this keyboard.",
            "Who made this.",
            "Back to the desktop.",
        };
        static readonly string[] CreditLines =
        {
            "STAN'S GAME",
            "Pegasus Bridge 1944",
            "",
            "A game by Joan and Stan",
            "",
            "Made with Unity 6",
            "Terrain: IGN LiDAR HD",
            "Character: modelled in Blender with MPFB (MakeHuman)",
            "Face reference: Stan's own photographs",
            "Menus, HUD, sounds and textures: made in code, no files",
            "Sounds, music and models of the game itself: list in progress",
            "",
            "Full list with licences: unity-additions/README.md",
        };

        static bool titleSeen;

        Page page = Page.None;
        int focus;
        int levelFocus;
        int roleFocus;
        Page roleBack = Page.Main;
        float pageOpened;
        float startAt = -1f;
        Vector2 lastMouse = new Vector2(-1f, -1f);
        readonly List<string> phaseTitles = new List<string>();
        readonly List<string> phaseGoals = new List<string>();
        string continueHelp;

        const int tick = 0, choose = 1;

        /// <summary>True while one of the menu pages (not Co-op) is on screen.</summary>
        public static bool Showing { get; private set; }

        // ------------------------------------------------------------------ flow

        void Update()
        {
            NetSession ns = NetSession.Instance;
            bool inMenu = ns != null && Net.InMenu;
            if (!inMenu)
            {
                // In the field the pause menu (PauseMenu) switches the game's NetSession on and off.
                if (page != Page.None) page = Page.None;
                startAt = -1f;
                Showing = false;
                return;
            }

            if (page == Page.None) Open(titleSeen ? Page.Main : Page.Title);
            ns.enabled = page == Page.Coop;      // the game's own menu only on the Co-op page
            Showing = page != Page.Coop;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (startAt >= 0f)
            {
                if (Time.unscaledTime >= startAt) StartLevel(ns);
                return;
            }
            if (SettingsPanel.Open) return;
            HandleKeys();
        }

        void Open(Page p)
        {
            page = p;
            pageOpened = Time.unscaledTime;
            if (p == Page.Main)
            {
                LevelInfo next = LevelCatalogue.All[LevelCatalogue.ContinueIndex()];
                continueHelp = "Level " + next.Number + "  ·  " + next.Title + "  ·  " + next.Place;
            }
            if (p == Page.Levels) BuildBriefing();
        }

        void HandleKeys()
        {
            Keyboard k = Keyboard.current;
            Gamepad g = Gamepad.current;
            Mouse m = Mouse.current;

            if (page == Page.Title)
            {
                bool any = (k != null && k.anyKey.wasPressedThisFrame) || (m != null && m.leftButton.wasPressedThisFrame)
                        || (g != null && (g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame));
                if (any && Time.unscaledTime - pageOpened > 0.4f && !SettingsPanel.Open)
                {
                    titleSeen = true;
                    Play(choose);
                    Open(Page.Main);
                }
                return;
            }

            bool up = (k != null && (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame))
                   || (g != null && (g.dpad.up.wasPressedThisFrame || g.leftStick.up.wasPressedThisFrame));
            bool down = (k != null && (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame))
                     || (g != null && (g.dpad.down.wasPressedThisFrame || g.leftStick.down.wasPressedThisFrame));
            bool ok = (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame))
                   || (g != null && g.buttonSouth.wasPressedThisFrame);
            bool back = Ui.TakeBack();
            bool left = (k != null && (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame))
                     || (g != null && (g.dpad.left.wasPressedThisFrame || g.leftStick.left.wasPressedThisFrame));
            bool right = (k != null && (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame))
                      || (g != null && (g.dpad.right.wasPressedThisFrame || g.leftStick.right.wasPressedThisFrame));

            switch (page)
            {
                case Page.Main:
                    if (up) Move(ref focus, -1, MainItems.Length);
                    if (down) Move(ref focus, 1, MainItems.Length);
                    if (ok) Activate(focus);
                    break;
                case Page.Levels:
                    if (up) { Move(ref levelFocus, -1, LevelCatalogue.All.Length); BuildBriefing(); }
                    if (down) { Move(ref levelFocus, 1, LevelCatalogue.All.Length); BuildBriefing(); }
                    if (ok) TryStart(levelFocus);
                    if (back) { Play(tick); Open(Page.Main); }
                    break;
                case Page.Role:
                    if (left || up) Move(ref roleFocus, -1, Roles.Cards.Length);
                    if (right || down) Move(ref roleFocus, 1, Roles.Cards.Length);
                    if (ok) StartAs(roleFocus);
                    if (back) { Play(tick); Open(roleBack); }
                    break;
                case Page.Credits:
                    if (ok || back) { Play(tick); Open(Page.Main); }
                    break;
                case Page.Coop:
                    if (back) { Play(tick); Open(Page.Main); }
                    break;
            }
        }

        void Move(ref int index, int step, int count)
        {
            index = (index + step + count) % count;
            Play(tick);
        }

        void Activate(int i)
        {
            Play(choose);
            switch (i)
            {
                case 0: TryStart(LevelCatalogue.ContinueIndex()); break;
                case 1: levelFocus = LevelCatalogue.ContinueIndex(); Open(Page.Levels); break;
                case 2: Open(Page.Coop); break;
                case 3: SettingsPanel.Show(true); break;
                case 4: Open(Page.Credits); break;
                case 5: Application.Quit(); break;
            }
        }

        void TryStart(int index)
        {
            if (!LevelCatalogue.IsUnlocked(index)) return;
            Play(choose);
            roleBack = page == Page.Levels ? Page.Levels : Page.Main;
            roleFocus = (int)Roles.Chosen;
            Open(Page.Role);
        }

        /// <summary>Starts the level with the chosen role. Only level 1 exists in the build; the
        /// catalogue index picks the scene once there are more.</summary>
        void StartAs(int role)
        {
            Roles.Chosen = (Role)role;
            Play(choose);
            startAt = Time.unscaledTime + 0.6f;
        }

        void StartLevel(NetSession ns)
        {
            startAt = -1f;
            titleSeen = true;
            ns.enabled = true;
            if (playAlone != null) playAlone.Invoke(ns, null);
            else Debug.LogError("Stan's Game: NetSession.PlayAlone not found; the menu cannot start the level");
        }

        /// <summary>Objectives of the focused level, read from the loaded level's own mission phases.</summary>
        void BuildBriefing()
        {
            phaseTitles.Clear();
            phaseGoals.Clear();
            LevelInfo l = LevelCatalogue.All[levelFocus];
            MissionDirector md = MissionDirector.Instance;
            if (!l.Available || l != MissionWatch.Current || md == null || md.phases == null) return;
            foreach (MissionDirector.Phase ph in md.phases)
            {
                if (ph == null) continue;
                phaseTitles.Add(ph.title);
                phaseGoals.Add(ph.objective);
            }
        }

        static void Play(int sound)
        {
            if (sound == choose) Ui.PlayChoose(); else Ui.PlayTick();
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (page == Page.None) return;
            GUI.depth = -100;   // over the level's HUD, under the settings window
            Ui.ApplyGameFont();
            float s = Ui.MenuScale;
            Event e = Event.current;

            if (page == Page.Coop)
            {
                DrawCoopBack(s, e);
                return;
            }

            float fade = Mathf.Clamp01((Time.unscaledTime - pageOpened) / 0.25f);
            if (page == Page.Title) Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
            Ui.Backdrop(1f);

            switch (page)
            {
                case Page.Title: DrawTitle(s, fade); break;
                case Page.Main: DrawMain(s, fade, e); break;
                case Page.Levels: DrawLevels(s, fade, e); break;
                case Page.Role: DrawRoles(s, fade, e); break;
                case Page.Credits: DrawCredits(s, fade, e); break;
            }

            if (startAt >= 0f)
                Ui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 1f - Mathf.Clamp01((startAt - Time.unscaledTime) / 0.6f)));
        }

        bool MouseMovedTo(Rect r, Event e)
        {
            if (e.type != EventType.Repaint || !r.Contains(e.mousePosition)) return false;
            bool moved = (e.mousePosition - lastMouse).sqrMagnitude > 1f;
            return moved;
        }

        void RememberMouse(Event e)
        {
            if (e.type == EventType.Repaint) lastMouse = e.mousePosition;
        }

        bool Clicked(Rect r, Event e)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition) && startAt < 0f && !SettingsPanel.Open)
            {
                e.Use();
                return true;
            }
            return false;
        }

        void DrawTitle(float s, float fade)
        {
            float h = Screen.height, w = Screen.width;
            float y = h * 0.30f;
            Ui.Text(new Rect(0f, y, w, 140f * s), "STAN'S GAME", UiFont.Display, 132f * s, Ui.A(Ui.Paper, fade), TextAnchor.MiddleCenter);
            y += 146f * s;
            Ui.Rule(w * 0.5f - 60f * s, y, 120f * s, s, fade);
            y += 22f * s;
            Ui.Text(new Rect(0f, y, w, 40f * s), "PEGASUS BRIDGE   ·   NIGHT OF 5 - 6 JUNE 1944", UiFont.Body, 26f * s, Ui.A(Ui.Brass, fade), TextAnchor.MiddleCenter, false, false, true);
            float pulse = 0.45f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
            Ui.Text(new Rect(0f, h * 0.72f, w, 40f * s), Ui.Prompt("PRESS ANY KEY", "PRESS A"), UiFont.Body, 24f * s, Ui.A(Ui.Paper, fade * pulse), TextAnchor.MiddleCenter, false, false, true);
            Ui.Text(new Rect(0f, h - 56f * s, w - 60f * s, 30f * s), "A game by Joan and Stan", UiFont.Type, 18f * s, Ui.A(Ui.Dim, fade), TextAnchor.MiddleRight);
        }

        void DrawHeader(float s, float fade, string text)
        {
            float x0 = 140f * s;
            Ui.Text(new Rect(x0, 96f * s, 1400f * s, 76f * s), text, UiFont.Display, 64f * s, Ui.A(Ui.Paper, fade), TextAnchor.MiddleLeft);
            Ui.Rule(x0 + 2f * s, 178f * s, 90f * s, s, fade);
        }

        void DrawMain(float s, float fade, Event e)
        {
            float x0 = 140f * s;
            Ui.Text(new Rect(x0, 120f * s, 1400f * s, 110f * s), "STAN'S GAME", UiFont.Display, 104f * s, Ui.A(Ui.Paper, fade), TextAnchor.MiddleLeft);
            Ui.Text(new Rect(x0 + 4f * s, 230f * s, 1000f * s, 34f * s), "PEGASUS BRIDGE   ·   NIGHT OF 5 - 6 JUNE 1944", UiFont.Body, 24f * s, Ui.A(Ui.Brass, fade), TextAnchor.MiddleLeft, false, false, true);
            Ui.Rule(x0 + 4f * s, 276f * s, 90f * s, s, fade);

            for (int i = 0; i < MainItems.Length; i++)
            {
                Rect r = new Rect(x0 - 30f * s, 330f * s + i * 64f * s, 560f * s, 52f * s);
                if (MouseMovedTo(r, e) && focus != i) { focus = i; Play(tick); }
                if (Clicked(r, e)) { focus = i; Activate(i); }
                bool on = focus == i;
                if (on)
                {
                    Ui.Fill(r, Ui.A(Ui.Paper, 0.07f * fade));
                    Ui.Fill(new Rect(r.x, r.y + 8f * s, 5f * s, r.height - 16f * s), Ui.A(Ui.Brass, fade));
                }
                Ui.Text(new Rect(x0 + (on ? 12f * s : 0f), r.y, 520f * s, r.height), MainItems[i], UiFont.Body, 34f * s,
                        Ui.A(Ui.Paper, (on ? 1f : 0.55f) * fade), TextAnchor.MiddleLeft, false, false, true);
            }
            RememberMouse(e);

            Ui.Text(new Rect(x0, 330f * s + MainItems.Length * 64f * s + 20f * s, 700f * s, 50f * s),
                focus == 0 ? continueHelp : MainHelp[focus], UiFont.Body, 20f * s, Ui.A(Ui.Dim, fade), TextAnchor.UpperLeft, false, true);
            DrawFooter(s, fade, false);
        }

        void DrawLevels(float s, float fade, Event e)
        {
            float x0 = 140f * s;
            DrawHeader(s, fade, "LEVEL SELECT");
            LevelInfo[] all = LevelCatalogue.All;
            for (int i = 0; i < all.Length; i++)
            {
                LevelInfo l = all[i];
                bool open = LevelCatalogue.IsUnlocked(i);
                LevelRecord rec = Progress.Get(l.Id);
                Rect r = new Rect(x0, 220f * s + i * 150f * s, 640f * s, 130f * s);
                if (MouseMovedTo(r, e) && levelFocus != i) { levelFocus = i; Play(tick); BuildBriefing(); }
                if (Clicked(r, e)) { if (levelFocus == i) TryStart(i); else { levelFocus = i; Play(tick); BuildBriefing(); } }
                bool on = levelFocus == i;
                Ui.Fill(r, Ui.A(Ui.Night, (on ? 0.9f : 0.6f) * fade));
                if (on)
                {
                    Ui.Fill(r, Ui.A(Ui.Paper, 0.05f * fade));
                    Ui.Fill(new Rect(r.x, r.y, 5f * s, r.height), Ui.A(Ui.Brass, fade));
                }
                float dim = open ? 1f : 0.45f;
                Ui.Text(new Rect(r.x + 10f * s, r.y, 100f * s, r.height), l.Number.ToString("00"), UiFont.Display, 56f * s, Ui.A(Ui.Brass, dim * fade), TextAnchor.MiddleCenter);
                Ui.Text(new Rect(r.x + 120f * s, r.y + 14f * s, 500f * s, 38f * s), l.Title.ToUpperInvariant(), UiFont.Body, 28f * s, Ui.A(Ui.Paper, dim * fade), TextAnchor.MiddleLeft, false, false, true);
                Ui.Text(new Rect(r.x + 120f * s, r.y + 54f * s, 500f * s, 26f * s), string.IsNullOrEmpty(l.Date) ? l.Place : l.Place + "   ·   " + l.Date,
                        UiFont.Body, 18f * s, Ui.A(Ui.Dim, dim * fade), TextAnchor.MiddleLeft);
                string status;
                Color sc;
                if (!l.Available) { status = "LOCKED   ·   IN DEVELOPMENT"; sc = Ui.Dim; }
                else if (!open) { status = "LOCKED   ·   COMPLETE LEVEL " + (l.Number - 1) + " FIRST"; sc = Ui.Dim; }
                else if (rec.completed) { status = "COMPLETED   ·   BEST " + Progress.FormatTime(rec.bestTimeSeconds); sc = Ui.Green; }
                else { status = "NOT COMPLETED YET"; sc = Ui.A(Ui.Paper, 0.8f); }
                Ui.Text(new Rect(r.x + 120f * s, r.y + 86f * s, 500f * s, 26f * s), status, UiFont.Body, 17f * s, Ui.A(sc, fade), TextAnchor.MiddleLeft, false, false, true);
            }
            RememberMouse(e);

            // Briefing for the focused level, typed on a sheet.
            LevelInfo f = all[levelFocus];
            float bx = x0 + 700f * s, bw = Mathf.Min(780f * s, Screen.width - bx - 80f * s);
            float pad = 34f * s, tw = bw - 2f * pad;
            float sumH = Ui.Height(f.Summary, UiFont.Type, 20f * s, tw);
            float objH = 0f;
            float line = Ui.Height("Ag", UiFont.Type, 18f * s, tw);
            for (int i = 0; i < phaseTitles.Count; i++)
                objH += line + Ui.Height(phaseGoals[i], UiFont.Type, 17f * s, tw - 34f * s) + 8f * s;
            float cardH = pad + 40f * s + 30f * s + 18f * s + sumH + (phaseTitles.Count > 0 ? 24f * s + 34f * s + objH : 0f) + pad;
            cardH = Mathf.Min(cardH, Screen.height - 220f * s - 90f * s);
            Rect card = new Rect(bx, 220f * s, bw, cardH);
            Ui.PaperCard(card, fade, s);
            float y = card.y + pad;
            float cx = card.x + pad;
            Ui.Text(new Rect(cx, y - 6f * s, tw, 26f * s), "BRIEFING   ·   SECRET", UiFont.Type, 15f * s, Ui.A(new Color(0.55f, 0.12f, 0.06f), fade), TextAnchor.MiddleRight, false, false, true);
            Ui.Text(new Rect(cx, y, tw, 40f * s), f.Title.ToUpperInvariant(), UiFont.Display, 34f * s, Ui.A(Ui.Ink, fade), TextAnchor.MiddleLeft);
            y += 40f * s;
            Ui.Text(new Rect(cx, y, tw, 30f * s), string.IsNullOrEmpty(f.Date) ? f.Place : f.Place + "   ·   " + f.Date, UiFont.Type, 18f * s, Ui.A(Ui.Ink, 0.75f * fade), TextAnchor.MiddleLeft, false, false, true);
            y += 30f * s;
            Ui.Fill(new Rect(cx, y + 6f * s, tw, Mathf.Max(1f, 1.5f * s)), Ui.A(Ui.Ink, 0.45f * fade));
            y += 18f * s;
            Ui.Text(new Rect(cx, y, tw, sumH), f.Summary, UiFont.Type, 20f * s, Ui.A(Ui.Ink, fade), TextAnchor.UpperLeft, false, true);
            y += sumH + 24f * s;
            if (phaseTitles.Count > 0)
            {
                Ui.Text(new Rect(cx, y, tw, 26f * s), "OBJECTIVES", UiFont.Type, 18f * s, Ui.A(new Color(0.55f, 0.12f, 0.06f), fade), TextAnchor.MiddleLeft, false, false, true);
                y += 34f * s;
                float indent = 34f * s;
                for (int i = 0; i < phaseTitles.Count && y < card.yMax - pad; i++)
                {
                    Ui.Text(new Rect(cx, y, indent, line), (i + 1) + ".", UiFont.Type, 18f * s, Ui.A(Ui.Ink, fade), TextAnchor.UpperLeft, false, false, true);
                    Ui.Text(new Rect(cx + indent, y, tw - indent, line), phaseTitles[i], UiFont.Type, 18f * s, Ui.A(Ui.Ink, fade), TextAnchor.UpperLeft, false, false, true);
                    y += line;
                    if (!string.IsNullOrEmpty(phaseGoals[i]))
                    {
                        float gh = Ui.Height(phaseGoals[i], UiFont.Type, 17f * s, tw - indent);
                        Ui.Text(new Rect(cx + indent, y, tw - indent, gh), phaseGoals[i], UiFont.Type, 17f * s, Ui.A(Ui.Ink, 0.75f * fade), TextAnchor.UpperLeft, false, true);
                        y += gh;
                    }
                    y += 8f * s;
                }
            }
            DrawFooter(s, fade, true);
        }

        void DrawRoles(float s, float fade, Event e)
        {
            float x0 = 140f * s;
            DrawHeader(s, fade, "CHOOSE YOUR ROLE");
            Ui.Text(new Rect(x0, 192f * s, 1600f * s, 30f * s), "D Company, 2nd Oxfordshire and Buckinghamshire Light Infantry   ·   glider-borne   ·   6 June 1944",
                    UiFont.Body, 19f * s, Ui.A(Ui.Brass, fade), TextAnchor.MiddleLeft);

            int n = Roles.Cards.Length;
            float gap = 30f * s;
            float cw = Mathf.Min(500f * s, (Screen.width - 2f * x0 - (n - 1) * gap) / n);
            float top = 250f * s, ch = 480f * s;
            float pad = 26f * s, tw = cw - 2f * pad;
            for (int i = 0; i < n; i++)
            {
                Roles.Card c = Roles.Cards[i];
                Rect r = new Rect(x0 + i * (cw + gap), top, cw, ch);
                if (MouseMovedTo(r, e) && roleFocus != i) { roleFocus = i; Play(tick); }
                if (Clicked(r, e)) { if (roleFocus == i) StartAs(i); else { roleFocus = i; Play(tick); } }
                bool on = roleFocus == i;
                // The chosen role is the service record on paper; the others stay dark.
                Color ink, soft, accent;
                if (on)
                {
                    Ui.PaperCard(r, fade, s);
                    ink = Ui.A(Ui.Ink, fade); soft = Ui.A(Ui.Ink, 0.8f * fade); accent = Ui.A(new Color(0.55f, 0.12f, 0.06f), fade);
                }
                else
                {
                    Ui.Fill(r, Ui.A(Ui.Night, 0.75f * fade));
                    ink = Ui.A(Ui.Paper, 0.75f * fade); soft = Ui.A(Ui.Paper, 0.55f * fade); accent = Ui.A(Ui.Brass, 0.7f * fade);
                }
                float y = r.y + 24f * s;
                Ui.Text(new Rect(r.x + pad, y, tw, 24f * s), c.Tag.ToUpperInvariant(), UiFont.Type, 16f * s, accent, TextAnchor.MiddleLeft, false, false, true);
                y += 28f * s;
                Ui.Text(new Rect(r.x + pad, y, tw, 44f * s), c.Name.ToUpperInvariant(), UiFont.Display, 32f * s, ink, TextAnchor.MiddleLeft);
                y += 56f * s;
                float hh = Ui.Height(c.History, UiFont.Body, 18f * s, tw);
                Ui.Text(new Rect(r.x + pad, y, tw, hh), c.History, UiFont.Body, 18f * s, soft, TextAnchor.UpperLeft, false, true);
                y += hh + 20f * s;
                Ui.Text(new Rect(r.x + pad, y, tw, 24f * s), "KIT", UiFont.Type, 16f * s, accent, TextAnchor.MiddleLeft, false, false, true);
                y += 28f * s;
                foreach (string k in c.Kit)
                {
                    float kh = Ui.Height(k, UiFont.Body, 18f * s, tw - 18f * s);
                    Ui.Text(new Rect(r.x + pad, y, 18f * s, kh), "·", UiFont.Body, 18f * s, ink, TextAnchor.UpperLeft, false, false, true);
                    Ui.Text(new Rect(r.x + pad + 18f * s, y, tw - 18f * s, kh), k, UiFont.Body, 18f * s, ink, TextAnchor.UpperLeft, false, true);
                    y += kh + 3f * s;
                }
                y += 16f * s;
                Ui.Text(new Rect(r.x + pad, y, tw, 24f * s), "IN THE FIELD", UiFont.Type, 16f * s, accent, TextAnchor.MiddleLeft, false, false, true);
                y += 28f * s;
                float ah = Ui.Height(c.Ability, UiFont.Body, 18f * s, tw);
                Ui.Text(new Rect(r.x + pad, y, tw, ah), c.Ability, UiFont.Body, 18f * s, ink, TextAnchor.UpperLeft, false, true);
            }
            RememberMouse(e);

            Ui.Hints(x0, top + ch + 24f * s, 30f * s, new string[]
            {
                KeyLabel.Of(Key.T), "every role calls a stretcher-bearer", KeyLabel.Of(Key.B), "calls for ammunition",
            }, 0.85f * fade);
            DrawFooter(s, fade, new string[] { Ui.Prompt("← →", "D-PAD"), "select", Ui.Prompt("ENTER", "A"), "start", Ui.Prompt("ESC", "B"), "back" });
        }

        void DrawCredits(float s, float fade, Event e)
        {
            float h = Screen.height, w = Screen.width;
            string[] fonts = Ui.Credits;
            int lines = CreditLines.Length + 2 + fonts.Length;
            float lh = 36f * s;
            float y = h * 0.5f - lines * lh * 0.5f;
            for (int i = 0; i < CreditLines.Length; i++)
            {
                if (i == 0) Ui.Text(new Rect(0f, y - 14f * s, w, 60f * s), CreditLines[i], UiFont.Display, 54f * s, Ui.A(Ui.Paper, fade), TextAnchor.MiddleCenter);
                else Ui.Text(new Rect(0f, y, w, 34f * s), CreditLines[i], UiFont.Body, 22f * s, i == 1 ? Ui.A(Ui.Brass, fade) : Ui.A(Ui.Paper, 0.8f * fade), TextAnchor.MiddleCenter, false, false, i == 1);
                y += lh;
            }
            y += lh;
            Ui.Text(new Rect(0f, y, w, 30f * s), "TYPE AND TEXTURES", UiFont.Body, 18f * s, Ui.A(Ui.Brass, fade), TextAnchor.MiddleCenter, false, false, true);
            y += lh;
            foreach (string f in fonts)
            {
                Ui.Text(new Rect(0f, y, w, 30f * s), f, UiFont.Body, 18f * s, Ui.A(Ui.Dim, fade), TextAnchor.MiddleCenter);
                y += 30f * s;
            }
            if (Clicked(new Rect(0f, 0f, w, h), e)) { Play(tick); Open(Page.Main); }
            DrawFooter(s, fade, true);
        }

        void DrawCoopBack(float s, Event e)
        {
            Rect r = new Rect(24f * s, 20f * s, 260f * s, 48f * s);
            bool over = r.Contains(e.mousePosition);
            Ui.Fill(r, Ui.A(Ui.Night, over ? 0.95f : 0.8f));
            Ui.Fill(new Rect(r.x, r.y, 4f * s, r.height), Ui.Brass);
            Ui.Text(new Rect(r.x + 18f * s, r.y, r.width, r.height), "‹   MAIN MENU   (" + Ui.Prompt("ESC", "B") + ")", UiFont.Body, 19f * s, Ui.Paper, TextAnchor.MiddleLeft, false, false, true);
            if (Clicked(r, e)) { Play(tick); Open(Page.Main); }
        }

        void DrawFooter(float s, float fade, bool hasBack)
        {
            DrawFooter(s, fade, hasBack
                ? new string[] { Ui.Prompt("↑ ↓", "D-PAD"), "select", Ui.Prompt("ENTER", "A"), "confirm", Ui.Prompt("ESC", "B"), "back" }
                : new string[] { Ui.Prompt("↑ ↓", "D-PAD"), "select", Ui.Prompt("ENTER", "A"), "confirm" });
        }

        void DrawFooter(float s, float fade, string[] hints)
        {
            float y = Screen.height - 64f * s;
            Ui.Hints(140f * s, y, 30f * s, hints, 0.75f * fade);
            Ui.Text(new Rect(0f, y, Screen.width - 60f * s, 30f * s), "DEMO 0.1", UiFont.Type, 16f * s, Ui.A(Ui.Dim, fade), TextAnchor.MiddleRight);
        }
    }
}
