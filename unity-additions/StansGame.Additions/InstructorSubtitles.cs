using System;
using System.Reflection;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Subtitle bar for the bootcamp instructor: while <see cref="InstructorVoice.Current"/> is set, the
    /// speaker (bold, brass) and the line (white, word-wrapped) are drawn bottom centre on a dark panel,
    /// fading in over 0.15 s and out over the last 0.3 s. It sits about 18 % of the screen height above the
    /// bottom in game, clear of the health panel and the weapon bar, and 7 % in the menu, under its items. Switch it off with <see cref="Enabled"/>
    /// (PlayerPrefs <c>stans.subtitles</c>, default on).
    ///
    /// When the squad's subtitle bar is in the build (<c>SquadVoice.ShowSubtitle(speaker, text, seconds)</c>,
    /// found by reflection) and the local player is in control, the line is handed to that bar instead,
    /// once, for the length of the clip, so there is one subtitle bar. The squad bar is drawn only while the
    /// player is in control, so in the menu or with no player this class draws its own. Any reflection
    /// failure falls back to the own bar.
    /// </summary>
    public sealed class InstructorSubtitles : MonoBehaviour
    {
        const string Key = "stans.subtitles";
        const float FadeIn = 0.15f, FadeOut = 0.3f;
        static readonly Color Accent = new Color(0.88f, 0.74f, 0.45f);

        /// <summary>Subtitles on or off. Saved when changed; default on.</summary>
        public static bool Enabled
        {
            get { return PlayerPrefs.GetInt(Key, 1) != 0; }
            set
            {
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>True when the line on screen was handed to the squad's subtitle bar.</summary>
        public static bool OnSquadBar { get; private set; }

        static Texture2D white;
        static GUIStyle speakerStyle, textStyle;

        // Shared bar (SquadVoice.ShowSubtitle), looked up once.
        static bool looked, warned;
        static MethodInfo showSubtitle;
        InstructorVoice.Spoken handed;

        // The bootcamp's own panel, when it shows the spoken line itself (Bootcamp.DrawsInstructorSubtitle).
        static bool lookedBootcamp;
        static PropertyInfo drawsProp;
        static FieldInfo drawsField;

        /// <summary>True while the bootcamp's panel shows the spoken line itself, so no second subtitle is drawn.</summary>
        public static bool BootcampShowsLine
        {
            get
            {
                if (!lookedBootcamp)
                {
                    lookedBootcamp = true;
                    Type t = typeof(InstructorSubtitles).Assembly.GetType("StansGame.Additions.Bootcamp");
                    if (t != null)
                    {
                        BindingFlags st = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                        drawsProp = t.GetProperty("DrawsInstructorSubtitle", st);
                        if (drawsProp != null && drawsProp.PropertyType != typeof(bool)) drawsProp = null;
                        drawsField = t.GetField("DrawsInstructorSubtitle", st);
                        if (drawsField != null && drawsField.FieldType != typeof(bool)) drawsField = null;
                    }
                }
                try
                {
                    if (drawsProp != null) return (bool)drawsProp.GetValue(null, null);
                    if (drawsField != null) return (bool)drawsField.GetValue(null);
                }
                catch (Exception) { drawsProp = null; drawsField = null; }
                return false;
            }
        }

        void Update()
        {
            InstructorVoice.Spoken cur = InstructorVoice.Current;
            if (cur == null || !Enabled || BootcampShowsLine) { OnSquadBar = false; return; }
            if (cur == handed) return;
            OnSquadBar = false;
            if (!LookUp() || !PlayerInControl() || string.IsNullOrEmpty(cur.Text)) return;
            try
            {
                float seconds = Mathf.Max(0.5f, cur.Until - Time.unscaledTime);
                showSubtitle.Invoke(null, new object[] { cur.Speaker ?? "", cur.Text, seconds });
                handed = cur;
                OnSquadBar = true;
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        static bool PlayerInControl()
        {
            SoldierController p = AdditionsBootstrap.LocalPlayer();
            return p != null && p.enabled;
        }

        /// <summary>Finds SquadVoice.ShowSubtitle once. Returns true when it can be used.</summary>
        static bool LookUp()
        {
            if (looked) return showSubtitle != null;
            looked = true;
            try
            {
                Type t = typeof(InstructorSubtitles).Assembly.GetType("StansGame.Additions.SquadVoice");
                if (t == null) return false;   // not in this build: own bar
                showSubtitle = t.GetMethod("ShowSubtitle", BindingFlags.Public | BindingFlags.Static, null,
                    new Type[] { typeof(string), typeof(string), typeof(float) }, null);
                if (showSubtitle == null) throw new InvalidOperationException("SquadVoice.ShowSubtitle(string, string, float) not found");
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
            return showSubtitle != null;
        }

        static void Fail(string why)
        {
            showSubtitle = null;
            if (warned) return;
            warned = true;
            Debug.LogWarning("Stan's Game: instructor subtitles could not use the squad subtitle bar (" + why + "); drawing their own");
        }

        static void EnsureStyles(float s)
        {
            if (white == null)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (speakerStyle == null)
            {
                speakerStyle = new GUIStyle(GUI.skin.label);
                speakerStyle.fontStyle = FontStyle.Bold;
                speakerStyle.alignment = TextAnchor.MiddleLeft;
                speakerStyle.wordWrap = false;
                textStyle = new GUIStyle(speakerStyle);
                textStyle.fontStyle = FontStyle.Normal;
                textStyle.alignment = TextAnchor.UpperLeft;
                textStyle.wordWrap = true;
                speakerStyle.normal.textColor = Color.white;   // GUI.color tints it; the skin's grey would dim it
                textStyle.normal.textColor = Color.white;
            }
            speakerStyle.fontSize = Mathf.RoundToInt(22f * s);
            textStyle.fontSize = Mathf.RoundToInt(24f * s);
        }

        void OnGUI()
        {
            GUI.depth = -150;   // over the HUD and the main menu (the bootcamp offer), under the settings window; set on every event, Unity sorts on Layout
            if (Event.current.type != EventType.Repaint) return;
            if (OnSquadBar || !Enabled || BootcampShowsLine) return;
            InstructorVoice.Spoken cur = InstructorVoice.Current;
            if (cur == null || string.IsNullOrEmpty(cur.Text)) return;

            float now = Time.unscaledTime;
            float a = Mathf.Clamp01((now - cur.Started) / FadeIn) * Mathf.Clamp01((cur.Until - now) / FadeOut);
            if (a <= 0f) return;

            float s = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f);
            EnsureStyles(s);
            Color old = GUI.color;

            float pad = 14f * s;
            float maxW = Screen.width * 0.6f;
            float innerW = maxW - 2f * pad;
            float textH = textStyle.CalcHeight(new GUIContent(cur.Text), innerW);
            float speakerH = 30f * s;
            // Shrink the panel to the text when it is short.
            Vector2 one = textStyle.CalcSize(new GUIContent(cur.Text));
            Vector2 nm = speakerStyle.CalcSize(new GUIContent(cur.Speaker ?? ""));
            float contentW = Mathf.Min(innerW, Mathf.Max(one.x, nm.x) + 4f);
            float panelW = contentW + 2f * pad;
            if (one.x > innerW) { contentW = innerW; panelW = maxW; }
            float panelH = pad + speakerH + textH + pad;
            float x = (Screen.width - panelW) * 0.5f;
            float lift = PlayerInControl() ? 0.18f : 0.07f;   // in the menu: under the title screen's "press any key" and the menu items
            float y = Screen.height - Screen.height * lift - panelH;

            GUI.color = new Color(0.04f, 0.04f, 0.05f, 0.72f * a);
            GUI.DrawTexture(new Rect(x, y, panelW, panelH), white);
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.9f * a);
            GUI.DrawTexture(new Rect(x, y, 3f * s, panelH), white);

            Rect rs = new Rect(x + pad, y + pad, contentW, speakerH);
            Rect rt = new Rect(x + pad, y + pad + speakerH, contentW, textH);
            string who = cur.Speaker ?? "";

            GUI.color = new Color(0f, 0f, 0f, 0.85f * a);
            GUI.Label(new Rect(rs.x + 1f, rs.y + 1f, rs.width, rs.height), who, speakerStyle);
            GUI.Label(new Rect(rt.x + 1f, rt.y + 1f, rt.width, rt.height), cur.Text, textStyle);
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, a);
            GUI.Label(rs, who, speakerStyle);
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Label(rt, cur.Text, textStyle);

            GUI.color = old;
        }
    }
}
