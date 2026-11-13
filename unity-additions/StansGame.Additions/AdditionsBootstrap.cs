using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Starts itself when the game loads (no scene or prefab changes needed) and adds
    /// PlayerView, HitFeedback, HealthHud and Mantle to the local player whenever one appears,
    /// including after a respawn, and AiUnstick to every AI soldier. Also runs the main menu,
    /// level select and level completion tracking (MainMenu, MissionWatch), the glider exit
    /// (GliderExit) and the objective marker and banner (ObjectiveHud).
    /// </summary>
    public sealed class AdditionsBootstrap : MonoBehaviour
    {
        static AdditionsBootstrap instance;
        float nextScan;
        float nextAiScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance != null) return;
            GameObject go = new GameObject("StansGame.Additions");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<AdditionsBootstrap>();
            go.AddComponent<SettingsPanel>();
            go.AddComponent<MainMenu>();
            go.AddComponent<MissionWatch>();
            go.AddComponent<GliderExit>();
            go.AddComponent<ObjectiveHud>();
            AudioListener.volume = GameSettings.MasterVolume;
            Debug.Log("Stan's Game: additions loaded (first-person view, hit feedback, health bar, F10 settings, climbing over low obstacles, AI unstick, main menu and level select, glider exit, objective markers)");
        }

        /// <summary>The player this PC controls, or null (menu, between respawns).</summary>
        public static SoldierController LocalPlayer()
        {
            MissionDirector md = MissionDirector.Instance;
            if (md == null || md.player == null || md.player.cam == null) return null;
            return md.player;
        }

        void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.25f;

            SoldierController p = LocalPlayer();
            if (p == null) return;
            if (p.GetComponent<PlayerView>() == null) p.gameObject.AddComponent<PlayerView>();
            if (p.GetComponent<HitFeedback>() == null) p.gameObject.AddComponent<HitFeedback>();
            if (p.GetComponent<HealthHud>() == null) p.gameObject.AddComponent<HealthHud>();
            if (p.GetComponent<Mantle>() == null) p.gameObject.AddComponent<Mantle>();
            FriendlyPassThrough.SetPlayer(p);

            // AI soldiers are spawned during the mission (glider sections, waves), so look for new ones.
            if (Time.unscaledTime >= nextAiScan)
            {
                nextAiScan = Time.unscaledTime + 1f;
                foreach (SoldierAI ai in FindObjectsByType<SoldierAI>())
                    if (ai.GetComponent<AiUnstick>() == null)
                    {
                        ai.gameObject.AddComponent<AiUnstick>();
                        FriendlyPassThrough.AddSoldier(ai);
                    }
            }
        }
    }
}
