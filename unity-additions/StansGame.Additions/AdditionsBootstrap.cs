using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Starts itself when the game loads (no scene or prefab changes needed) and adds
    /// PlayerView, WeaponFeel, HitFeedback, HealthHud, Mantle, RoleKit and SquadCalls to the local player whenever one appears,
    /// including after a respawn, and AiUnstick to every AI soldier. Also runs the main menu,
    /// level select and level completion tracking (MainMenu, MissionWatch), the glider exit
    /// (GliderExit), the objective marker and banner (ObjectiveHud), and the graphics: night lighting and
    /// sky (Atmosphere, NightSky), grass and leaves (Foliage) and flare lights (FlareLights).
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
            go.AddComponent<Atmosphere>();
            go.AddComponent<Foliage>();
            go.AddComponent<FlareLights>();
            go.AddComponent<RagdollWrists>();
            go.AddComponent<Bootcamp>();
            TracerFix.Apply();
            AudioListener.volume = GameSettings.MasterVolume;
            Debug.Log("Stan's Game: additions loaded (first-person view, weapon feel, hit feedback, health bar, F10 settings, climbing over low obstacles, AI unstick, main menu and level select, glider exit, objective markers, roles, calls for a medic or ammunition, relaxed wrists on the dead, bootcamp, night sky and lighting, detailed foliage, flares)");
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
            if (p.GetComponent<WeaponFeel>() == null) p.gameObject.AddComponent<WeaponFeel>();
            if (p.GetComponent<HitFeedback>() == null) p.gameObject.AddComponent<HitFeedback>();
            if (p.GetComponent<HealthHud>() == null) p.gameObject.AddComponent<HealthHud>();
            if (p.GetComponent<Mantle>() == null) p.gameObject.AddComponent<Mantle>();
            if (p.GetComponent<RoleKit>() == null) p.gameObject.AddComponent<RoleKit>();
            if (p.GetComponent<SquadCalls>() == null) p.gameObject.AddComponent<SquadCalls>();
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
