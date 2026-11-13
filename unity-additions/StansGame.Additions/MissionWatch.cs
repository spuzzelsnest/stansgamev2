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
        public static LevelInfo Current { get { return LevelCatalogue.All[0]; } }

        MissionDirector watched;
        float controlSince = -1f;
        bool done;
        float cardUntil;
        string cardTime;
        bool cardBest;


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
            if (md == null || Net.InMenu) { controlSince = -1f; return; }

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

        /// <summary>The "mission complete" card, typed as a signal on a sheet of paper.</summary>
        void OnGUI()
        {
            GUI.depth = -50;
            if (Time.realtimeSinceStartup > cardUntil || Net.InMenu || Event.current.type != EventType.Repaint) return;
            Ui.ApplyGameFont();
            float s = Ui.MenuScale;
            float a = Mathf.Clamp01((cardUntil - Time.realtimeSinceStartup) / 1.5f) * Mathf.Clamp01((10.2f - (cardUntil - Time.realtimeSinceStartup)) / 0.4f);
            float w = 760f * s, h = 300f * s;
            Rect card = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.30f, w, h);
            Ui.PaperCard(card, a, s);
            float pad = 40f * s, x = card.x + pad, iw = w - 2f * pad, y = card.y + 26f * s;
            Ui.Text(new Rect(x, y, iw, 26f * s), "SIGNAL   ·   D COY 2 OX AND BUCKS   ·   " + MissionDirectorClock(), UiFont.Type, 16f * s, Ui.A(Ui.Ink, 0.65f * a), TextAnchor.MiddleLeft, false, false, true);
            y += 34f * s;
            Ui.Text(new Rect(x, y, iw, 70f * s), "MISSION COMPLETE", UiFont.Display, 60f * s, Ui.A(Ui.Ink, a), TextAnchor.MiddleLeft);
            y += 76f * s;
            Ui.Fill(new Rect(x, y, iw, Mathf.Max(1f, 2f * s)), Ui.A(Ui.Ink, 0.5f * a));
            y += 14f * s;
            Ui.Text(new Rect(x, y, iw, 32f * s), Current.Title.ToUpperInvariant() + "   ·   TIME " + cardTime, UiFont.Type, 24f * s, Ui.A(Ui.Ink, a), TextAnchor.MiddleLeft, false, false, true);
            if (cardBest) Ui.Text(new Rect(x, y, iw, 32f * s), "BEST TIME", UiFont.Type, 22f * s, Ui.A(new Color(0.55f, 0.12f, 0.06f), a), TextAnchor.MiddleRight, false, false, true);
            y += 40f * s;
            Ui.Text(new Rect(x, y, iw, 28f * s), "Ham and Jam. The bridges are held.", UiFont.Type, 19f * s, Ui.A(Ui.Ink, 0.85f * a), TextAnchor.MiddleLeft);
            Ui.Text(new Rect(x, card.yMax - 44f * s, iw, 26f * s), KeyLabel.Of(UnityEngine.InputSystem.Key.Escape) + ", then Leave mission, returns to the main menu", UiFont.Type, 16f * s, Ui.A(Ui.Ink, 0.6f * a), TextAnchor.MiddleLeft);
        }

        static string MissionDirectorClock()
        {
            MissionDirector md = MissionDirector.Instance;
            return md != null ? md.ClockText : "";
        }
    }
}
