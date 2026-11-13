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
        enum Page { None, Title, Main, Levels, Credits, Coop }

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly MethodInfo playAlone = typeof(NetSession).GetMethod("PlayAlone", Any);

        static readonly string[] MainItems = { "PLAY", "LEVEL SELECT", "CO-OP", "SETTINGS", "CREDITS", "QUIT" };
        static readonly string[] MainHelp =
        {
            "", // filled in with the level to continue
            "Pick a mission. Finish one to open the next.",
            "Play with friends: host on your home network or online with a join code.",
            "View, field of view, mouse sensitivity, effects and volume.",
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
            "Terrain from IGN LiDAR HD",
            "",
            "Full credits for every asset will follow",
            "once the project source is in the repository.",
        };

        static bool titleSeen;

        Page page = Page.None;
        int focus;
        int levelFocus;
        float pageOpened;
        float startAt = -1f;
        Vector2 lastMouse = new Vector2(-1f, -1f);
        readonly List<string> phaseTitles = new List<string>();
        readonly List<string> phaseGoals = new List<string>();
        string continueHelp;

        AudioSource sfx;
        AudioClip tick, choose;

        static Texture2D white, gradient;
        static GUIStyle titleBig, titleSmall, item, help, header, cardNumber, cardTitle, cardLine, body, footer, centre;
        static readonly Color Accent = new Color(0.88f, 0.74f, 0.45f);

        /// <summary>True while one of the menu pages (not Co-op) is on screen.</summary>
        public static bool Showing { get; private set; }

        void Awake()
        {
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            sfx.volume = 0.35f;
            tick = Tone("menu tick", 1400f, 1400f, 0.035f);
            choose = Tone("menu choose", 660f, 990f, 0.12f);
        }

        // ------------------------------------------------------------------ flow

        void Update()
        {
            NetSession ns = NetSession.Instance;
            bool inMenu = ns != null && Net.InMenu;
            if (!inMenu)
            {
                if (page != Page.None) page = Page.None;
                if (ns != null && !ns.enabled) ns.enabled = true;
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
                if (any && Time.unscaledTime - pageOpened > 0.4f)
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
            bool back = (k != null && k.escapeKey.wasPressedThisFrame) || (g != null && g.buttonEast.wasPressedThisFrame);

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
            // Only level 1 exists in the build; the catalogue index picks the scene once there are more.
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

        void Play(AudioClip c)
        {
            if (c != null && sfx != null) sfx.PlayOneShot(c);
        }

        static AudioClip Tone(string name, float f0, float f1, float seconds)
        {
            int rate = 44100, n = Mathf.CeilToInt(rate * seconds);
            float[] d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                phase += 2f * Mathf.PI * Mathf.Lerp(f0, f1, t) / rate;
                d[i] = Mathf.Sin(phase) * Mathf.Exp(-5f * t) * Mathf.Min(1f, i / 60f) * 0.6f;
            }
            AudioClip c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        // ------------------------------------------------------------------ drawing

        static void EnsureStyles()
        {
            if (white == null)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (gradient == null)
            {
                gradient = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                gradient.wrapMode = TextureWrapMode.Clamp;
                for (int x = 0; x < 256; x++)
                    gradient.SetPixel(x, 0, new Color(0f, 0f, 0f, Mathf.Lerp(0.9f, 0.35f, Mathf.SmoothStep(0f, 1f, x / 255f))));
                gradient.Apply();
            }
            if (titleBig != null) return;
            GUIStyle b = new GUIStyle(GUI.skin.label);
            b.wordWrap = false;
            b.richText = false;
            b.normal.textColor = Color.white;
            titleBig = new GUIStyle(b); titleBig.fontStyle = FontStyle.Bold;
            titleSmall = new GUIStyle(b);
            item = new GUIStyle(b); item.fontStyle = FontStyle.Bold; item.alignment = TextAnchor.MiddleLeft;
            help = new GUIStyle(b); help.wordWrap = true;
            header = new GUIStyle(b); header.fontStyle = FontStyle.Bold;
            cardNumber = new GUIStyle(b); cardNumber.fontStyle = FontStyle.Bold; cardNumber.alignment = TextAnchor.MiddleCenter;
            cardTitle = new GUIStyle(b); cardTitle.fontStyle = FontStyle.Bold;
            cardLine = new GUIStyle(b);
            body = new GUIStyle(b); body.wordWrap = true;
            footer = new GUIStyle(b);
            centre = new GUIStyle(b); centre.alignment = TextAnchor.MiddleCenter;
        }

        static void Sizes(float s)
        {
            titleBig.fontSize = Mathf.RoundToInt(76f * s);
            titleSmall.fontSize = Mathf.RoundToInt(22f * s);
            item.fontSize = Mathf.RoundToInt(32f * s);
            help.fontSize = Mathf.RoundToInt(18f * s);
            header.fontSize = Mathf.RoundToInt(44f * s);
            cardNumber.fontSize = Mathf.RoundToInt(48f * s);
            cardTitle.fontSize = Mathf.RoundToInt(28f * s);
            cardLine.fontSize = Mathf.RoundToInt(17f * s);
            body.fontSize = Mathf.RoundToInt(19f * s);
            footer.fontSize = Mathf.RoundToInt(16f * s);
            centre.fontSize = Mathf.RoundToInt(22f * s);
        }

        void OnGUI()
        {
            if (page == Page.None) return;
            GUI.depth = -100;   // over the level's HUD, under the settings window
            EnsureStyles();
            float s = Mathf.Max(0.6f, Screen.height / 1080f);
            Sizes(s);
            Event e = Event.current;
            Color keep = GUI.color;

            if (page == Page.Coop)
            {
                DrawCoopBack(s, e);
                GUI.color = keep;
                return;
            }

            float fade = Mathf.Clamp01((Time.unscaledTime - pageOpened) / 0.25f);
            if (e.type == EventType.Repaint)
            {
                if (page == Page.Title)
                {
                    GUI.color = new Color(0f, 0f, 0f, 0.7f);
                    GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), white);
                }
                else
                {
                    GUI.color = Color.white;
                    GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), gradient);
                }
            }

            switch (page)
            {
                case Page.Title: DrawTitle(s, fade); break;
                case Page.Main: DrawMain(s, fade, e); break;
                case Page.Levels: DrawLevels(s, fade, e); break;
                case Page.Credits: DrawCredits(s, fade, e); break;
            }

            if (startAt >= 0f && e.type == EventType.Repaint)
            {
                GUI.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01((startAt - Time.unscaledTime) / 0.6f));
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), white);
            }
            GUI.color = keep;
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
            GUI.color = new Color(1f, 1f, 1f, fade);
            titleBig.fontSize = Mathf.RoundToInt(104f * s);
            Label(new Rect(0f, h * 0.30f, w, 130f * s), "STAN'S GAME", titleBig, TextAnchor.MiddleCenter);
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, fade);
            Label(new Rect(0f, h * 0.30f + 130f * s, w, 40f * s), "PEGASUS BRIDGE   ·   6 JUNE 1944", titleSmall, TextAnchor.MiddleCenter);
            GUI.color = new Color(1f, 1f, 1f, fade * (0.45f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f)));
            Label(new Rect(0f, h * 0.72f, w, 40f * s), "PRESS ANY KEY", centre, TextAnchor.MiddleCenter);
            GUI.color = new Color(1f, 1f, 1f, 0.5f * fade);
            Label(new Rect(0f, h - 50f * s, w - 40f * s, 30f * s), "A game by Joan and Stan", footer, TextAnchor.MiddleRight);
        }

        void DrawHeader(float s, float fade, string text)
        {
            float x0 = 140f * s;
            GUI.color = new Color(1f, 1f, 1f, fade);
            Label(new Rect(x0, 110f * s, 900f * s, 60f * s), text, header, TextAnchor.MiddleLeft);
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, fade);
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(new Rect(x0, 178f * s, 90f * s, 3f * s), white);
        }

        void DrawMain(float s, float fade, Event e)
        {
            float x0 = 140f * s;
            GUI.color = new Color(1f, 1f, 1f, fade);
            Label(new Rect(x0, 130f * s, 1200f * s, 96f * s), "STAN'S GAME", titleBig, TextAnchor.MiddleLeft);
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, fade);
            Label(new Rect(x0 + 4f * s, 226f * s, 900f * s, 34f * s), "PEGASUS BRIDGE   ·   6 JUNE 1944", titleSmall, TextAnchor.MiddleLeft);
            if (e.type == EventType.Repaint) GUI.DrawTexture(new Rect(x0 + 4f * s, 274f * s, 90f * s, 3f * s), white);

            for (int i = 0; i < MainItems.Length; i++)
            {
                Rect r = new Rect(x0 - 30f * s, 340f * s + i * 64f * s, 560f * s, 52f * s);
                if (MouseMovedTo(r, e) && focus != i) { focus = i; Play(tick); }
                if (Clicked(r, e)) { focus = i; Activate(i); }
                bool on = focus == i;
                if (on && e.type == EventType.Repaint)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.06f * fade);
                    GUI.DrawTexture(r, white);
                    GUI.color = new Color(Accent.r, Accent.g, Accent.b, fade);
                    GUI.DrawTexture(new Rect(r.x, r.y + 8f * s, 5f * s, r.height - 16f * s), white);
                }
                GUI.color = new Color(1f, 1f, 1f, (on ? 1f : 0.55f) * fade);
                Label(new Rect(x0 + (on ? 12f * s : 0f), r.y, 520f * s, r.height), MainItems[i], item, TextAnchor.MiddleLeft);
            }
            RememberMouse(e);

            GUI.color = new Color(1f, 1f, 1f, 0.75f * fade);
            Label(new Rect(x0, 340f * s + MainItems.Length * 64f * s + 24f * s, 700f * s, 50f * s),
                focus == 0 ? continueHelp : MainHelp[focus], help, TextAnchor.UpperLeft);
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
                if (e.type == EventType.Repaint)
                {
                    GUI.color = new Color(0f, 0f, 0f, (on ? 0.7f : 0.45f) * fade);
                    GUI.DrawTexture(r, white);
                    if (on)
                    {
                        GUI.color = new Color(Accent.r, Accent.g, Accent.b, fade);
                        GUI.DrawTexture(new Rect(r.x, r.y, 5f * s, r.height), white);
                    }
                }
                float dim = open ? 1f : 0.45f;
                GUI.color = new Color(Accent.r, Accent.g, Accent.b, dim * fade);
                Label(new Rect(r.x + 10f * s, r.y, 100f * s, r.height), l.Number.ToString("00"), cardNumber, TextAnchor.MiddleCenter);
                GUI.color = new Color(1f, 1f, 1f, dim * fade);
                Label(new Rect(r.x + 120f * s, r.y + 16f * s, 500f * s, 36f * s), l.Title.ToUpperInvariant(), cardTitle, TextAnchor.MiddleLeft);
                GUI.color = new Color(1f, 1f, 1f, 0.7f * dim * fade);
                Label(new Rect(r.x + 120f * s, r.y + 54f * s, 500f * s, 26f * s), string.IsNullOrEmpty(l.Date) ? l.Place : l.Place + "   ·   " + l.Date, cardLine, TextAnchor.MiddleLeft);
                string status;
                Color sc;
                if (!l.Available) { status = "LOCKED   ·   IN DEVELOPMENT"; sc = new Color(1f, 1f, 1f, 0.5f); }
                else if (!open) { status = "LOCKED   ·   COMPLETE LEVEL " + (l.Number - 1) + " FIRST"; sc = new Color(1f, 1f, 1f, 0.5f); }
                else if (rec.completed) { status = "COMPLETED   ·   BEST " + Progress.FormatTime(rec.bestTimeSeconds); sc = new Color(0.55f, 0.85f, 0.5f); }
                else { status = "NOT COMPLETED YET"; sc = new Color(1f, 1f, 1f, 0.8f); }
                GUI.color = new Color(sc.r, sc.g, sc.b, sc.a * fade);
                Label(new Rect(r.x + 120f * s, r.y + 86f * s, 500f * s, 26f * s), status, cardLine, TextAnchor.MiddleLeft);
            }
            RememberMouse(e);

            // Briefing for the focused level
            LevelInfo f = all[levelFocus];
            float bx = x0 + 700f * s, bw = Mathf.Min(760f * s, Screen.width - bx - 100f * s);
            GUI.color = new Color(1f, 1f, 1f, fade);
            Label(new Rect(bx, 220f * s, bw, 44f * s), f.Title.ToUpperInvariant(), cardTitle, TextAnchor.MiddleLeft);
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, fade);
            Label(new Rect(bx, 262f * s, bw, 28f * s), string.IsNullOrEmpty(f.Date) ? f.Place : f.Place + "   ·   " + f.Date, cardLine, TextAnchor.MiddleLeft);
            GUI.color = new Color(1f, 1f, 1f, 0.85f * fade);
            float sumH = body.CalcHeight(new GUIContent(f.Summary), bw);
            GUI.Label(new Rect(bx, 304f * s, bw, sumH), f.Summary, body);
            float y = 304f * s + sumH + 24f * s;
            if (phaseTitles.Count > 0)
            {
                GUI.color = new Color(Accent.r, Accent.g, Accent.b, fade);
                Label(new Rect(bx, y, bw, 26f * s), "OBJECTIVES", cardLine, TextAnchor.MiddleLeft);
                y += 34f * s;
                float indent = 30f * s;
                float line = help.CalcHeight(new GUIContent("Ag"), bw);
                for (int i = 0; i < phaseTitles.Count; i++)
                {
                    GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.9f * fade);
                    Label(new Rect(bx, y, indent, line), (i + 1).ToString(), help, TextAnchor.UpperLeft);
                    GUI.color = new Color(1f, 1f, 1f, fade);
                    Label(new Rect(bx + indent, y, bw - indent, line), phaseTitles[i], help, TextAnchor.UpperLeft);
                    y += line;
                    if (!string.IsNullOrEmpty(phaseGoals[i]))
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.65f * fade);
                        float gh = help.CalcHeight(new GUIContent(phaseGoals[i]), bw - indent);
                        GUI.Label(new Rect(bx + indent, y, bw - indent, gh), phaseGoals[i], help);
                        y += gh;
                    }
                    y += 8f * s;
                }
            }
            DrawFooter(s, fade, true);
        }

        void DrawCredits(float s, float fade, Event e)
        {
            float h = Screen.height, w = Screen.width;
            float y = h * 0.5f - CreditLines.Length * 36f * s * 0.5f;
            for (int i = 0; i < CreditLines.Length; i++)
            {
                GUI.color = i == 0 ? new Color(1f, 1f, 1f, fade) : i == 1 ? new Color(Accent.r, Accent.g, Accent.b, fade) : new Color(1f, 1f, 1f, 0.8f * fade);
                Label(new Rect(0f, y + i * 36f * s, w, 34f * s), CreditLines[i], centre, TextAnchor.MiddleCenter);
            }
            if (Clicked(new Rect(0f, 0f, w, h), e)) { Play(tick); Open(Page.Main); }
            DrawFooter(s, fade, true);
        }

        void DrawCoopBack(float s, Event e)
        {
            Rect r = new Rect(24f * s, 20f * s, 220f * s, 44f * s);
            if (e.type == EventType.Repaint)
            {
                GUI.color = new Color(0f, 0f, 0f, r.Contains(e.mousePosition) ? 0.8f : 0.55f);
                GUI.DrawTexture(r, white);
                GUI.color = Accent;
                GUI.DrawTexture(new Rect(r.x, r.y, 4f * s, r.height), white);
            }
            GUI.color = Color.white;
            Label(new Rect(r.x + 18f * s, r.y, r.width, r.height), "<   MAIN MENU   (Esc)", footer, TextAnchor.MiddleLeft);
            if (Clicked(r, e)) { Play(tick); Open(Page.Main); }
        }

        void DrawFooter(float s, float fade, bool hasBack)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.55f * fade);
            string text = hasBack ? "Up / Down  select      Enter  confirm      Esc  back" : "Up / Down  select      Enter  confirm";
            Label(new Rect(140f * s, Screen.height - 64f * s, 900f * s, 30f * s), text, footer, TextAnchor.MiddleLeft);
            Label(new Rect(0f, Screen.height - 64f * s, Screen.width - 60f * s, 30f * s), "Demo 0.1", footer, TextAnchor.MiddleRight);
        }

        static void Label(Rect r, string text, GUIStyle style, TextAnchor anchor)
        {
            style.alignment = anchor;
            GUI.Label(r, text, style);
        }
    }
}
