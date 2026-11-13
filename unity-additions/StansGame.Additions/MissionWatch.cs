using System.Reflection;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Marks a level as completed in <see cref="Progress"/> when its mission reaches the last phase and
    /// the closing film (the Finale for level 1) has ended or was skipped, and shows a short
    /// "mission complete" card with the time. Leaving earlier does not count. Every PC records its own
    /// completion, so co-op clients unlock the next level too.
    /// </summary>
    public sealed class MissionWatch : MonoBehaviour
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo finaleDone = typeof(CinematicDirector).GetField("finaleDone", Any);

        /// <summary>The catalogue entry for the level that is loaded (only level 1 exists in the build).</summary>
        public static LevelInfo Current { get { return LevelCatalogue.All[LevelCatalogue.IndexOf("pegasus-bridge")]; } }

        MissionDirector watched;
        float controlSince = -1f;
        bool done;
        float cardUntil;
        string cardTime;
        bool cardBest;

        static GUIStyle big, small;
        static Texture2D white;

        void Update()
        {
            MissionDirector md = MissionDirector.Instance;
            if (md != watched)
            {
                // New level load (first start, or Leave reloaded the scene): start over.
                watched = md;
                controlSince = -1f;
                done = false;
                cardUntil = 0f;
            }
            if (md == null || Net.InMenu || Bootcamp.Active) { controlSince = -1f; return; }

            SoldierController p = AdditionsBootstrap.LocalPlayer();
            if (controlSince < 0f && p != null && p.enabled && !CinematicDirector.Playing) controlSince = Time.realtimeSinceStartup;
            if (done || controlSince < 0f || md.phases == null || md.phases.Length == 0) return;

            CinematicDirector cd = CinematicDirector.Instance;
            bool lastPhase = md.CurrentPhase >= md.phases.Length - 1;
            bool filmOver = cd != null && finaleDone != null && (bool)finaleDone.GetValue(cd) && !CinematicDirector.Playing;
            if (!lastPhase || !filmOver) return;

            done = true;
            float seconds = Time.realtimeSinceStartup - controlSince;
            cardBest = Progress.MarkCompleted(Current.Id, seconds);
            cardTime = Progress.FormatTime(seconds);
            cardUntil = Time.realtimeSinceStartup + 10f;
            Debug.Log("Stan's Game: level '" + Current.Id + "' completed in " + cardTime + (cardBest ? " (best)" : ""));
        }

        void OnGUI()
        {
            GUI.depth = -50;
            if (Time.realtimeSinceStartup > cardUntil || Net.InMenu || Event.current.type != EventType.Repaint) return;
            float s = Screen.height / 1080f;
            if (white == null)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label);
                big.alignment = TextAnchor.MiddleCenter;
                big.fontStyle = FontStyle.Bold;
                small = new GUIStyle(big);
                small.fontStyle = FontStyle.Normal;
            }
            big.fontSize = Mathf.RoundToInt(46f * s);
            small.fontSize = Mathf.RoundToInt(20f * s);

            float a = Mathf.Clamp01((cardUntil - Time.realtimeSinceStartup) / 1.5f);
            Color keep = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f * a);
            GUI.DrawTexture(new Rect(0f, Screen.height * 0.36f, Screen.width, 190f * s), white);
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Label(new Rect(0f, Screen.height * 0.36f + 24f * s, Screen.width, 60f * s), "MISSION COMPLETE", big);
            GUI.color = new Color(0.88f, 0.78f, 0.56f, a);
            GUI.Label(new Rect(0f, Screen.height * 0.36f + 90f * s, Screen.width, 30f * s),
                Current.Title + "   ·   " + cardTime + (cardBest ? "   ·   best time" : ""), small);
            GUI.color = new Color(1f, 1f, 1f, 0.7f * a);
            GUI.Label(new Rect(0f, Screen.height * 0.36f + 128f * s, Screen.width, 30f * s), "Esc, then Leave the game, returns to the main menu", small);
            GUI.color = keep;
        }
    }
}
