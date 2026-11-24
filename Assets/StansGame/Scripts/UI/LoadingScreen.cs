using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StansGame
{
    /// <summary>
    /// Level template 6: a loading screen for every level - the title, the place, the date and the hour, and the
    /// briefing, on a dark card while the level loads (in the background, so the screen doesn't freeze). A wartime
    /// photograph is shown if Resources/Loading/&lt;scene&gt; exists (e.g. the RAF photograph of the bridges).
    /// Use LoadingScreen.Load("PegasusBridge") instead of SceneManager.LoadScene.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        string title = "", place = "", when = "", briefing = "";
        Texture2D photo;
        float alpha = 1f, shownAt;
        bool done;

        public static void Load(string scene)
        {
            var go = new GameObject("Loading screen");
            DontDestroyOnLoad(go);
            var ls = go.AddComponent<LoadingScreen>();
            foreach (var m in LevelSelect.Missions)
                if (m.scene == scene) { ls.title = m.title; ls.place = m.place; ls.when = m.when; ls.briefing = m.briefing; break; }
            if (string.IsNullOrEmpty(ls.title)) ls.title = scene == "ProvingGround" ? "The Proving Ground" : scene;
            if (scene == "ProvingGround") { ls.place = "A training field in England"; ls.when = "Spring 1944"; ls.briefing = "Try the weapons and the vehicles of both sides before you go to Normandy."; }
            ls.photo = Resources.Load<Texture2D>("Loading/" + scene);
            ls.StartCoroutine(ls.Run(scene));
        }

        IEnumerator Run(string scene)
        {
            shownAt = Time.realtimeSinceStartup;
            yield return null; yield return null;   // let the card be drawn first
            var op = SceneManager.LoadSceneAsync(scene);
            if (op == null) { Destroy(gameObject); yield break; }
            while (!op.isDone) yield return null;
            // at least two seconds to read the card, then fade
            while (Time.realtimeSinceStartup - shownAt < 2f) yield return null;
            done = true;
            while (alpha > 0f) { alpha -= Time.unscaledDeltaTime * 1.5f; yield return null; }
            Destroy(gameObject);
        }

        static Texture2D px;
        void OnGUI()
        {
            if (!px) { px = new Texture2D(1, 1); px.SetPixel(0, 0, Color.white); px.Apply(); }
            GUI.depth = -1000;
            var old = GUI.color;
            GUI.color = new Color(0.06f, 0.06f, 0.07f, alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), px);
            float s = Screen.height / 1080f;
            if (photo)
            {
                GUI.color = new Color(0.85f, 0.85f, 0.85f, alpha * 0.9f);
                float h = Screen.height * 0.5f, w = h * photo.width / Mathf.Max(1f, photo.height);
                GUI.DrawTexture(new Rect((Screen.width - w) / 2f, Screen.height * 0.08f, w, h), photo, ScaleMode.ScaleToFit);
            }
            float y = photo ? Screen.height * 0.62f : Screen.height * 0.32f;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            var gold = new Color(0.80f, 0.59f, 0.29f, alpha);
            var big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(54 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            big.normal.textColor = new Color(0.95f, 0.93f, 0.90f, alpha);
            var mid = new GUIStyle(big) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Normal };
            mid.normal.textColor = gold;
            var small = new GUIStyle(mid) { fontSize = Mathf.RoundToInt(20 * s), wordWrap = true };
            small.normal.textColor = new Color(0.75f, 0.72f, 0.66f, alpha);
            GUI.Label(new Rect(0, y, Screen.width, 70 * s), title.ToUpperInvariant(), big);
            GUI.Label(new Rect(0, y + 70 * s, Screen.width, 36 * s), $"{place}   ·   {when}", mid);
            GUI.Label(new Rect(Screen.width * 0.2f, y + 120 * s, Screen.width * 0.6f, 160 * s), briefing, small);
            if (!done)
            {
                float t = Time.realtimeSinceStartup;
                GUI.Label(new Rect(0, Screen.height - 80 * s, Screen.width, 30 * s), "Loading" + new string('.', 1 + (int)(t * 2f) % 3), mid);
            }
            GUI.color = old;
        }
    }

    /// <summary>
    /// Level template 6: the settings that matter - difficulty and the volume - kept between games (PlayerPrefs).
    /// Difficulty changes how hard the enemy's hits land on the player: Easy 60%, Normal 100%, Hard 140%.
    /// </summary>
    public static class GameSettings
    {
        public static readonly string[] DifficultyNames = { "Easy", "Normal", "Hard" };
        public static int Difficulty { get => PlayerPrefs.GetInt("stans.difficulty", 1); set { PlayerPrefs.SetInt("stans.difficulty", Mathf.Clamp(value, 0, 2)); PlayerPrefs.Save(); } }
        public static float Volume { get => PlayerPrefs.GetFloat("stans.volume", 1f); set { PlayerPrefs.SetFloat("stans.volume", Mathf.Clamp01(value)); AudioListener.volume = Mathf.Clamp01(value); } }
        public static float DamageToPlayer => Difficulty == 0 ? 0.6f : Difficulty == 2 ? 1.4f : 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Apply() => AudioListener.volume = Volume;

        /// <summary>The settings rows for a GUILayout menu (the in-game Esc menu).</summary>
        public static void Rows()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Difficulty", GUILayout.Width(90));
            int d = GUILayout.Toolbar(Difficulty, DifficultyNames, GUILayout.Height(26));
            if (d != Difficulty) Difficulty = d;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Volume {Mathf.RoundToInt(Volume * 100)}%", GUILayout.Width(90));
            float v = GUILayout.HorizontalSlider(Volume, 0f, 1f, GUILayout.Height(20));
            if (Mathf.Abs(v - Volume) > 0.001f) Volume = v;
            GUILayout.EndHorizontal();
        }
    }
}
