using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// Bootcamp: a short training level (about 5 minutes, solo) that teaches every control the game has,
    /// set at D Company's rehearsal ground at Countess Wear, Exeter, spring 1944. Nine stations, each with
    /// a line from the sergeant-major, a checklist with the player's own key labels (ZQSD on AZERTY) and a
    /// marker; a station ticks off only when the player has actually done it.
    ///
    /// It runs inside the level 1 scene, because the demo build has no other: started from the menu, the
    /// night mission is held (no Intro film, no gliders, the mission director and every enemy switched
    /// off), the player is put on a training ground built at run time (<see cref="TrainingGround"/>) in a
    /// quiet corner of the map, and the lighting is set to day. Leaving reloads the level as before.
    /// Offered once before the first Play, always in the level select, never required.
    /// </summary>
    public sealed class Bootcamp : MonoBehaviour
    {
        public const string Id = "bootcamp";
        const string OfferedKey = "stans.bootcampOffered";
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static readonly FieldInfo ridingField = typeof(MissionDirector).GetField("riding", Any);
        static readonly FieldInfo climbingField = typeof(SoldierController).GetField("climbing", Any);
        static readonly MethodInfo leaveMethod = typeof(NetSession).GetMethod("Leave", Any);
        static readonly PropertyInfo deadProp = typeof(Health).GetProperty("IsDead", Any);
        static readonly PropertyInfo currentProp = typeof(Health).GetProperty("Current", Any);

        enum State { Off, Requested, Running, Finished }

        static Bootcamp instance;
        static State state;
        static bool continueToLevel;
        static MissionDirector continueFrom;

        /// <summary>True from the moment the bootcamp is started in the menu until the level is reloaded.</summary>
        public static bool Active { get { return state != State.Off; } }

        /// <summary>The station in progress (0-based), or -1 when the bootcamp is not running.</summary>
        public static int Station { get { return state == State.Running && instance != null ? instance.station : -1; } }

        /// <summary>True while the sergeant-major's praise for the last station is on screen (the next station's checks wait).</summary>
        public static bool Praising { get { return instance != null && Time.time < instance.praiseUntil; } }

        /// <summary>True on the end card.</summary>
        public static bool Finished { get { return state == State.Finished; } }

        /// <summary>The training ground while the bootcamp runs, else null.</summary>
        public static TrainingGround Ground { get { return Active && instance != null ? instance.ground : null; } }

        /// <summary>The soldiers on the training ground (test probes look at them).</summary>
        public static TrainingCrowd Crowd { get { return Active && instance != null ? instance.crowd : null; } }

        // ------------------------------------------------------------------ stations

        public static readonly string[] StationTitles =
        {
            "Fall in", "Assault course", "Under the wire", "The canal", "The range", "Grenades", "PIAT", "Field dressing", "The bridge",
        };

        public static readonly string[] StationGoals =
        {
            "Look around, switch the view, walk to the flag.",
            "Sprint, jump the ditch, climb the wall.",
            "Kneel, go prone, crawl under the wire, kneel behind cover.",
            "Swim across and climb out on the far bank.",
            "Sten from the hip, rifle through the sights, reload, switch weapons.",
            "Select the grenades and throw one into the pit.",
            "Pick up the PIAT, aim it and hit the tank.",
            "Take the drill wound and put on a field dressing.",
            "Timed run: clear the defenders, take the bridge, send Ham and Jam.",
        };

        static readonly string[] Lines =
        {
            "Right, you lot, eyes front! Look left, look right, then double over to my flag.",
            "Over the start line, sprint for the ditch, jump it, and over that wall. Move yourself!",
            "Wire! Get down on your belly and crawl under it. Then kneel behind the sandbags.",
            "No bridge for you, lad. Through the water, and out the far side.",
            "This is the range. Sten first, from the hip. Then the rifle, through the sights. Make every round count.",
            "Grenades. Lob it into the pit and get your head down. Into the pit, mind, not at your feet.",
            "The PIAT. Heavy, slow, and it stops a tank. Pick it up, aim high, and put a bomb into that hulk.",
            "You've caught one, lad. Only a drill, but let's see you dress it.",
            "That's your bridge, and you're against the clock. Over the start line, drop the defenders, take it, and send Ham and Jam. Swap that PIAT for a Sten off the table.",
        };

        /// <summary>PlayerPrefs key for the best time of the bridge run (seconds).</summary>
        public const string BestRunKey = "stans.bootcampBestRun";

        // Where each station starts (local field coordinates x, z, heading); used for respawns and skips.
        static readonly Vector3[] Checkpoints =
        {
            new Vector3(0f, 0f, 0f), new Vector3(0f, 11f, 0f), new Vector3(0f, 48.5f, 0f), new Vector3(0f, 65f, 0f),
            new Vector3(2f, 94f, 90f), new Vector3(2f, 116f, 90f), new Vector3(2f, 132f, 90f), new Vector3(0f, 140f, 0f), new Vector3(0f, 151f, 0f),
        };

        TrainingGround ground;
        TrainingCrowd crowd;
        MissionDirector md;
        SoldierController player;
        int station;
        bool[] ticks = new bool[8];
        float stationStart, startTime, finishTime, praiseUntil, lastSkip;
        string praise;


        // per-station working state
        float startYaw, sprintFor, jumpPressAt = -10f, jumpFromY;
        bool swum, inWire, wasSecond, wounded, lookedLeft, lookedRight;
        int nearHits, farHits, viewSwitches, dressingsAtWound;
        float woundAt, holdH;
        readonly Dictionary<Health, float> downSince = new Dictionary<Health, float>();
        Light hulkFire;

        // pop-up targets: lowered ones lie flat with their board's collider off
        static readonly HashSet<Health> lowered = new HashSet<Health>();
        Health popUp;
        float popUntil, popNextAt;
        AudioClip clack;

        // the timed bridge run
        float runStart = -1f, runTime = -1f;
        int defendersDown;
        bool rerun;
        bool behindLine;

        // menu offer
        public static bool OfferOpen { get; private set; }
        /// <summary>True while the offer is open and in the frame it closed, so the menu does not act on the same key or click.</summary>
        public static bool OfferBusy { get { return OfferOpen || offerClosedFrame == Time.frameCount; } }
        static Action<int> offerStart;
        static int offerFocus;
        static float offerOpened;
        static int offerClosedFrame = -1;
        static Vector2 offerMouse = new Vector2(-1f, -1f);

        AudioSource sfx;
        AudioClip whistle, bugle, tick;
        static Texture2D white;
        static GUIStyle small, title, line, item, card, cardBig;
        static readonly Color Accent = new Color(0.88f, 0.74f, 0.45f);

        // ------------------------------------------------------------------ menu API

        /// <summary>True when the first Play should ask "bootcamp first?": never asked before and never done.</summary>
        public static bool ShouldOffer()
        {
            return PlayerPrefs.GetInt(OfferedKey, 0) == 0 && !Progress.Get(Id).completed;
        }

        /// <summary>Shows the one-time "bootcamp first?" question over the main menu. <paramref name="start"/> gets the catalogue index to start.</summary>
        public static void Offer(Action<int> start)
        {
            PlayerPrefs.SetInt(OfferedKey, 1);
            PlayerPrefs.Save();
            offerStart = start;
            offerFocus = 0;
            offerOpened = Time.unscaledTime;
            OfferOpen = true;
        }

        /// <summary>Called by the menu just before it starts the session: holds the night mission so no film or glider starts.</summary>
        public static void Request()
        {
            state = State.Requested;
            continueToLevel = false;
            CinematicDirector cd = CinematicDirector.Instance;
            if (cd != null) cd.enabled = false;
            MissionDirector m = MissionDirector.Instance;
            if (m != null)
            {
                if (m.gliders != null) foreach (GliderApproach g in m.gliders) if (g != null) g.enabled = false;
                m.enabled = false;
            }
            Debug.Log("Stan's Game: bootcamp requested, night mission held");
        }

        /// <summary>True once after the bootcamp ended with "on to level 1": the menu then starts level 1 straight away.</summary>
        public static bool TakeContinue()
        {
            if (!continueToLevel) return false;
            // Only once the level has been reloaded: Net.InMenu is briefly true while the old session shuts down.
            MissionDirector m = MissionDirector.Instance;
            if (m == null || (object)m == (object)continueFrom) return false;
            continueToLevel = false;
            continueFrom = null;
            return true;
        }

        // ------------------------------------------------------------------ setup

        void Awake()
        {
            instance = this;
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            whistle = Whistle();
            bugle = Bugle();
            tick = Tone("bootcamp tick", 1100f, 1500f, 0.06f, 0.35f);
            clack = Clack();
        }

        /// <summary>True when a target is standing up (not hit, not lowered): what a player can shoot at.</summary>
        public static bool TargetUp(Health h)
        {
            return h != null && !h.IsDead && !lowered.Contains(h);
        }

        /// <summary>The bridge run's clock: seconds so far, the final time once done, or -1 before the start line.</summary>
        public static float RunClock
        {
            get
            {
                if (instance == null) return -1f;
                if (instance.runTime >= 0f) return instance.runTime;
                return instance.runStart >= 0f ? Time.time - instance.runStart : -1f;
            }
        }

        void Update()
        {
            if (OfferOpen) OfferKeys();
            if (state == State.Off) return;

            MissionDirector now = MissionDirector.Instance;
            if (state != State.Requested && (now != md || Net.InMenu))
            {
                // Left the game: the level was reloaded, so the training ground is gone.
                state = State.Off;
                ground = null;
                crowd = null;
                player = null;
                return;
            }
            if (state == State.Requested)
            {
                if (Net.InMenu || now == null || now.player == null) return;
                Begin(now);
                return;
            }

            KeepSceneQuiet();
            SoldierController p = md.player;
            if (p != player && p != null) TakeOver(p, false);
            if (player == null) return;
            Daylight();
            UpdateTargets();
            if (crowd != null) crowd.Tick(player.transform, LiveFireOn());

            if (state == State.Finished)
            {
                Keyboard k = Keyboard.current;
                if (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) && Time.time - finishTime > 1.5f)
                {
                    continueToLevel = true;
                    continueFrom = md;
                    Leave();
                }
                else if (k != null && k.rKey.wasPressedThisFrame && Time.time - finishTime > 1.0f && !SettingsPanel.Open)
                {
                    // Run the bridge again: back to the start line, defenders reset, the clock waits for the line.
                    rerun = true;
                    state = State.Running;
                    StartStation(StationTitles.Length - 1, true);
                }
                return;
            }

            Health h = player.GetComponent<Health>();
            if (h != null && h.IsDead) return;   // the game respawns the player; TakeOver puts them back
            if (!player.enabled || SettingsPanel.Open) return;

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.backspaceKey.wasPressedThisFrame && Time.time - lastSkip > 0.5f)
            {
                lastSkip = Time.time;
                Complete(true);
                return;
            }
            if (Time.time < praiseUntil) return;
            Check();
        }

        void Begin(MissionDirector m)
        {
            md = m;
            state = State.Running;
            md.enabled = false;
            CinematicDirector cd = CinematicDirector.Instance;
            if (cd != null) cd.enabled = false;
            foreach (SoldierAI ai in FindObjectsByType<SoldierAI>(FindObjectsInactive.Include)) ai.enabled = false;
            foreach (TankAI t in FindObjectsByType<TankAI>(FindObjectsInactive.Include)) t.enabled = false;
            GameObject amb = GameObject.Find("Night ambience");
            if (amb != null) amb.SetActive(false);

            ground = TrainingGround.Build();
            crowd = TrainingCrowd.Build(ground);
            lowered.Clear();
            downSince.Clear();
            popUp = null;
            rerun = false;
            foreach (Health h in ground.Defenders) if (h != null) lowered.Add(h);
            Daylight();
            station = 0;
            startTime = Time.time;
            TakeOver(md.player, true);
            StartStation(0, false);
            Debug.Log("Stan's Game: bootcamp started on the training ground at " + ground.Root.position.ToString("F1"));
        }

        /// <summary>Takes the player over from the mission's glider ride and puts them at the current station.</summary>
        void TakeOver(SoldierController p, bool first)
        {
            player = p;
            md.StopAllCoroutines();   // the glider ride the mission starts for every new local player
            if (ridingField != null) ridingField.SetValue(md, false);
            Vector3 c = Checkpoints[station];
            p.transform.rotation = ground.Facing(c.z);
            Place(p, ground.W(c.x, 0.1f, c.y));
            Loadout lo = p.GetComponent<Loadout>();
            if (lo != null)
            {
                if (lo.dressings < 1) lo.dressings = 1;
                TopUpGrenades(lo, 2);
            }
            if (!first)
            {
                // Respawned: put back what this station needs if it was already used up.
                if (station == 4 && (ground.Rifle == null) && !Has(lo, "Lee-Enfield")) ground.Rifle = TrainingGround.MakePickup("Lee-Enfield", ground.W(8.2f, 0.85f, 94f), ground.Facing(90f));
                if (station == 6 && (ground.Piat == null) && !Has(lo, "PIAT")) ground.Piat = TrainingGround.MakePickup("PIAT", ground.W(8.2f, 0.85f, 132f), ground.Facing(90f));
                if (station == 7) wounded = false;
                if (station == 0) { startYaw = float.NaN; stationStart = Time.time; }
                if (station == 8 && state == State.Running) StartStation(8, false);   // the run starts again
            }
            Debug.Log("Stan's Game: bootcamp player placed at station " + (station + 1) + (first ? "" : " (respawn)"));
        }

        static bool Has(Loadout lo, string name)
        {
            return lo != null && lo.primary != null && lo.primary.displayName == name;
        }

        static void TopUpGrenades(Loadout lo, int count)
        {
            if (lo.grenades == null) return;
            Shell mills = null;
            foreach (Shell s in Resources.FindObjectsOfTypeAll<Shell>())
                if (s.name == "Grenade_Mills") { mills = s; break; }
            if (mills == null) return;
            while (lo.grenades.Count < count) lo.grenades.Add(mills);
        }

        /// <summary>Keeps the night mission from waking up while the bootcamp runs.</summary>
        void KeepSceneQuiet()
        {
            if (md.enabled) md.enabled = false;
            CinematicDirector cd = CinematicDirector.Instance;
            if (cd != null && cd.enabled) cd.enabled = false;
        }

        /// <summary>The sky colour at the horizon, which the haze fades into (the sky dome uses it too).</summary>
        public static readonly Color HorizonColour = new Color(0.71f, 0.77f, 0.84f);

        static readonly List<Light> suns = new List<Light>();
        static float sunsFound = -10f;

        /// <summary>Daylight for the training ground: the moon becomes a spring morning sun, sky-lit ambient and haze.</summary>
        static void Daylight()
        {
            if (Time.time - sunsFound > 2f)
            {
                sunsFound = Time.time;
                suns.Clear();
                foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                    if (l.type == LightType.Directional) suns.Add(l);
            }
            foreach (Light l in suns)
            {
                if (l == null) continue;
                l.intensity = 1.4f;
                l.color = new Color(1f, 0.94f, 0.84f);
                l.transform.rotation = Quaternion.Euler(42f, 128f, 0f);
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.82f;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.64f, 0.76f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.52f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = HorizonColour;
            RenderSettings.fogDensity = 0.0042f;
            foreach (Camera c in Camera.allCameras)
            {
                c.clearFlags = CameraClearFlags.SolidColor;
                c.backgroundColor = HorizonColour;
            }
        }

        // ------------------------------------------------------------------ stations

        /// <summary>Moves the player. The character controller keeps its own copy of the position, so sync it,
        /// or the next move puts the player back where they were (seen on "run the bridge again").</summary>
        static void Place(SoldierController p, Vector3 world)
        {
            p.SetControlActive(true, new Nullable<Vector3>(world));
            Physics.SyncTransforms();
        }

        void StartStation(int s, bool teleport)
        {
            station = s;
            for (int i = 0; i < ticks.Length; i++) ticks[i] = false;
            stationStart = Time.time;
            sprintFor = 0f;
            jumpPressAt = -10f;
            swum = false;
            inWire = false;
            wasSecond = false;
            wounded = false;
            lookedLeft = false;
            lookedRight = false;
            viewSwitches = 0;
            nearHits = 0;
            farHits = 0;
            holdH = 0f;
            startYaw = float.NaN;   // taken in Check once the camera has settled on the player
            if (teleport && player != null)
            {
                Vector3 c = Checkpoints[s];
                player.transform.rotation = ground.Facing(c.z);
                Place(player, ground.W(c.x, 0.1f, c.y));
            }
            if (crowd != null) crowd.Station(s, teleport);
            Loadout lo = player != null ? player.GetComponent<Loadout>() : null;
            if (s == 5 && lo != null) TopUpGrenades(lo, 2);
            if (s == 7 && lo != null && lo.dressings < 1) lo.dressings = 1;
            popUp = null;
            popNextAt = Time.time + 1f;
            if (s == 8) ResetRun(lo);
            Play(whistle, 0.6f);
            Debug.Log("Stan's Game: bootcamp station " + (s + 1) + " " + StationTitles[s]);
        }

        int ItemCount(int s)
        {
            switch (s)
            {
                case 0: return 3;
                case 1: return 4;
                case 2: return 4;
                case 3: return 3;
                case 4: return 6;
                case 5: return 3;
                case 6: return 3;
                case 7: return 3;
                case 8: return 4;
            }
            return 0;
        }

        /// <summary>Sets up the bridge run: defenders down and waiting, a Sten on the table if the PIAT is still in hand.</summary>
        void ResetRun(Loadout lo)
        {
            runStart = -1f;
            runTime = -1f;
            behindLine = false;
            defendersDown = 0;
            foreach (Health h in ground.Defenders)
            {
                if (h == null) continue;
                if (h.IsDead) Revive(h);
                downSince.Remove(h);
                lowered.Add(h);
            }
            if (lo != null && Has(lo, "PIAT")) TrainingGround.MakePickup("Sten Mk II", ground.W(3.8f, 0.85f, 150.1f), ground.Facing(90f));
        }

        /// <summary>The Bren fires over the wire while the player is at it, until they are through.</summary>
        bool LiveFireOn()
        {
            if (state != State.Running || station != 2 || ticks[2] || Time.time < praiseUntil || player == null) return false;
            Vector3 l = ground.Local(player.transform.position);
            return l.z > 44f && l.z < 62f;
        }

        /// <summary>Stations whose items must be done in order.</summary>
        static bool Sequential(int s)
        {
            return s >= 4;
        }

        int FirstOpen()
        {
            int n = ItemCount(station);
            for (int i = 0; i < n; i++) if (!ticks[i]) return i;
            return n;
        }

        void Tick(int i)
        {
            if (ticks[i]) return;
            if (Sequential(station) && FirstOpen() != i) return;
            ticks[i] = true;
            Play(tick, 0.5f);
            Debug.Log("Stan's Game: bootcamp " + StationTitles[station] + " item " + (i + 1) + " done");
        }

        void Check()
        {
            Vector3 l = ground.Local(player.transform.position);
            CharacterController cc = player.GetComponent<CharacterController>();
            Loadout lo = player.GetComponent<Loadout>();
            Keyboard k = Keyboard.current;
            bool climbing = climbingField != null && (bool)climbingField.GetValue(player);
            float surface;
            bool swimming = Water.Swimming(player.transform.position, out surface);

            switch (station)
            {
                case 0:
                {
                    if (float.IsNaN(startYaw) && Time.time - stationStart > 0.4f) startYaw = YawNow();
                    float d = float.IsNaN(startYaw) ? 0f : Mathf.DeltaAngle(startYaw, YawNow());
                    if (d < -35f) lookedLeft = true;
                    if (d > 35f) lookedRight = true;
                    if (lookedLeft && lookedRight) Tick(0);
                    if (k != null && k.vKey.wasPressedThisFrame) { viewSwitches++; if (viewSwitches >= 2) Tick(1); }
                    if (Flat(l, 0f, 8f) < 2.2f) Tick(2);
                    break;
                }
                case 1:
                {
                    bool moving = k != null && (k.wKey.isPressed || k.aKey.isPressed || k.sKey.isPressed || k.dKey.isPressed);
                    if (moving && k.leftShiftKey.isPressed && cc != null && cc.isGrounded && l.z > 10f) sprintFor += Time.deltaTime;
                    if (sprintFor > 0.8f) Tick(0);
                    if (k != null && k.spaceKey.wasPressedThisFrame && cc != null && cc.isGrounded && !climbing) { jumpPressAt = Time.time; jumpFromY = player.transform.position.y; }
                    if (Time.time - jumpPressAt < 0.8f && !climbing && player.transform.position.y > jumpFromY + 0.25f && l.z > 22f && l.z < 34f) Tick(1);
                    if (climbing && l.z > TrainingGround.WallZ0 - 2f && l.z < TrainingGround.WallZ1 + 2f) Tick(2);
                    if (l.z > 46f && Mathf.Abs(l.x) < 6f) Tick(3);
                    break;
                }
                case 2:
                {
                    if (player.IsCrouching) Tick(0);
                    if (player.IsProne) Tick(1);
                    bool inZone = Mathf.Abs(l.x) < TrainingGround.WireHalfX + 0.3f && l.z > TrainingGround.WireZ0 && l.z < TrainingGround.WireZ1;
                    if (inZone && player.IsProne) inWire = true;
                    if (inWire && l.z > TrainingGround.WireZ1 + 0.2f) Tick(2);
                    if (ticks[2] && player.IsCrouching && Mathf.Abs(l.x) < 2.4f && l.z > TrainingGround.WireZ1 + 0.5f && l.z < TrainingGround.BagsZ) Tick(3);
                    break;
                }
                case 3:
                {
                    if (swimming) { swum = true; Tick(0); }
                    if (swimming && l.z > TrainingGround.PitZ1 - 2.5f) Tick(1);
                    if (swum && !swimming && cc != null && cc.isGrounded && l.z > TrainingGround.PitZ1 + 0.3f && l.y > -0.3f) { Tick(1); Tick(2); }
                    break;
                }
                case 4:
                {
                    if (Flat(l, 7f, 94f) < 1.8f) Tick(0);
                    if (ticks[0] && lo != null && lo.primary != null && lo.primary.displayName == "Lee-Enfield") Tick(2);
                    if (ticks[3] && player.CurrentWeapon != null && player.CurrentWeapon.IsReloading) Tick(4);
                    if (ticks[4] && lo != null)
                    {
                        if (lo.Selected == 1) wasSecond = true;
                        if (wasSecond && lo.Selected == 0) Tick(5);
                    }
                    break;
                }
                case 5:
                {
                    if (Flat(l, 7f, 116f) < 1.8f) Tick(0);
                    if (ticks[0] && lo != null && lo.Selected == 2) Tick(1);
                    if (ground.GrenadeDummy != null && ground.GrenadeDummy.IsDead) { Tick(0); Tick(1); Tick(2); }
                    if (lo != null && lo.grenades != null && lo.grenades.Count == 0 && !AnyGrenadeLive()) TopUpGrenades(lo, 2);
                    break;
                }
                case 6:
                {
                    if (Has(lo, "PIAT")) Tick(0);
                    if (ticks[0] && player.IsAiming && Has(lo, "PIAT")) Tick(1);
                    if (ground.Hulk != null && ground.Hulk.IsDead) { Tick(0); Tick(1); Tick(2); }
                    Weapon w = lo != null ? lo.primary : null;
                    if (w != null && w.displayName == "PIAT" && w.Ammo + w.SpareRounds == 0 && !w.IsReloading) w.SetAmmo(1, 2);
                    break;
                }
                case 7:
                {
                    if (!ticks[0] && Flat(l, -2f, 145f) < 2.2f)
                    {
                        Tick(0);
                        Wound(lo);
                    }
                    if (wounded && Time.time - woundAt > 1.5f) Tick(1);
                    if (wounded && lo != null && lo.dressings < dressingsAtWound) Tick(2);
                    break;
                }
                case 8:
                {
                    if (l.z < TrainingGround.RunStartZ - 0.3f) behindLine = true;
                    if (!ticks[0] && behindLine && l.z > TrainingGround.RunStartZ)
                    {
                        // Over the line: the clock starts and the defenders come up.
                        Tick(0);
                        runStart = Time.time;
                        foreach (Health h in ground.Defenders) if (h != null) lowered.Remove(h);
                        foreach (Health h in ground.Defenders) if (h != null) { Clack(h); break; }
                    }
                    defendersDown = 0;
                    foreach (Health h in ground.Defenders) if (h != null && h.IsDead) defendersDown++;
                    if (ticks[0] && defendersDown >= ground.Defenders.Count) Tick(1);
                    bool onDeck = Mathf.Abs(l.x) < 2.1f && l.z > 158.5f && l.z < 169.5f;
                    if (ticks[1] && onDeck) Tick(2);
                    if (ticks[2])
                    {
                        if (k != null && k.hKey.isPressed) holdH += Time.deltaTime;
                        else holdH = Mathf.Max(0f, holdH - Time.deltaTime * 2f);
                        if (holdH >= SendSeconds()) { runTime = Time.time - runStart; Tick(3); }
                    }
                    break;
                }
            }
            if (FirstOpen() >= ItemCount(station)) Complete(false);
        }

        void Wound(Loadout lo)
        {
            Health h = player.GetComponent<Health>();
            if (h == null) return;
            if (lo != null && lo.dressings < 1) lo.dressings = 1;
            dressingsAtWound = lo != null ? lo.dressings : 1;
            float target = h.maxHealth * 0.45f;
            if (h.Current > target)
            {
                DamageInfo info = new DamageInfo();
                info.amount = h.Current - target;
                info.point = player.transform.position + player.transform.forward * 3f + Vector3.up * 1.2f;
                info.direction = -player.transform.forward;
                info.force = 0f;
                h.TakeDamage(info);
            }
            wounded = true;
            woundAt = Time.time;
        }

        float SendSeconds()
        {
            RadioSet r = player != null ? player.GetComponentInChildren<RadioSet>() : null;
            return r != null && r.sendSeconds > 0.5f ? r.sendSeconds : 3f;
        }

        static bool AnyGrenadeLive()
        {
            foreach (Shell s in FindObjectsByType<Shell>(FindObjectsInactive.Exclude))
                if (s != null && s.fuse > 0f) return true;
            return false;
        }

        float YawNow()
        {
            if (player == null) return 0f;
            return player.cam != null ? player.cam.transform.eulerAngles.y : player.transform.eulerAngles.y;
        }

        static float Flat(Vector3 l, float x, float z)
        {
            return new Vector2(l.x - x, l.z - z).magnitude;
        }

        void Complete(bool skipped)
        {
            int n = ItemCount(station);
            for (int i = 0; i < n; i++) ticks[i] = true;
            if (station >= StationTitles.Length - 1)
            {
                Finish();
                return;
            }
            praise = skipped ? "Skipped. Next station: " + StationTitles[station + 1] + "."
                             : Praise(station) + "  Next station: " + StationTitles[station + 1] + ".";
            praiseUntil = Time.time + (skipped ? 0.8f : 2.6f);
            int next = station + 1;
            StartStation(next, skipped);
        }

        static string Praise(int s)
        {
            switch (s)
            {
                case 0: return "Not bad. For a civilian.";
                case 1: return "Good. Now get your breath back.";
                case 2: return "Wire's no excuse. Well done.";
                case 3: return "Wet, but you're over.";
                case 4: return "Shooting like a soldier now.";
                case 5: return "That's how you post a grenade.";
                case 6: return "Tank's done for. Good shot.";
                case 7: return "Patched up and back in the fight.";
            }
            return "Good.";
        }

        void Finish()
        {
            state = State.Finished;
            finishTime = Time.time;
            runLine = "";
            if (runTime > 0f)
            {
                float best = PlayerPrefs.GetFloat(BestRunKey, 0f);
                bool newBest = best <= 0f || runTime < best;
                if (newBest) { PlayerPrefs.SetFloat(BestRunKey, runTime); PlayerPrefs.Save(); best = runTime; }
                runLine = "Bridge run " + FormatRun(runTime) + (newBest ? "   ·   new best" : "   ·   best " + FormatRun(best));
                Debug.Log("Stan's Game: bootcamp bridge run in " + FormatRun(runTime) + (newBest ? " (best)" : ""));
            }
            if (!rerun)
            {
                float seconds = finishTime - startTime;
                bool best = Progress.MarkCompleted(Id, seconds);
                praise = Progress.FormatTime(seconds) + (best ? "   ·   best time" : "");
                Debug.Log("Stan's Game: bootcamp completed in " + Progress.FormatTime(seconds));
            }
            Play(bugle, 0.7f);
        }

        string runLine = "";

        /// <summary>A run time as m:ss.t.</summary>
        public static string FormatRun(float seconds)
        {
            if (seconds < 0f) return "-";
            int tenths = Mathf.FloorToInt(seconds * 10f);
            return (tenths / 600) + ":" + ((tenths / 10) % 60).ToString("00") + "." + (tenths % 10);
        }

        void Leave()
        {
            NetSession ns = NetSession.Instance;
            if (ns != null && leaveMethod != null) leaveMethod.Invoke(ns, null);
        }

        // ------------------------------------------------------------------ targets

        /// <summary>
        /// Targets: on the range, while a shooting item is open, its targets pop up one at a time and drop
        /// again if not hit in time (4.5 s at 25 m, 6 s at 50 m); otherwise hit targets fall back on their
        /// hinge and come up after three seconds. The bridge defenders stay down once hit.
        /// </summary>
        void UpdateTargets()
        {
            if (ground == null) return;
            bool live = state == State.Running && station == 4 && Time.time >= praiseUntil;
            Track(ground.NearTargets, true, live && ticks[0] && !ticks[1]);
            Track(ground.FarTargets, false, live && ticks[2] && !ticks[3]);
            foreach (Health h in ground.Defenders) if (h != null) Show(h, h.IsDead || lowered.Contains(h));
            if (ground.GrenadeDummy != null) Fall(ground.GrenadeDummy, ground.GrenadeDummy.IsDead);
            if (ground.Hulk != null && ground.Hulk.IsDead && hulkFire == null)
            {
                if (ground.HulkTurret != null) ground.HulkTurret.localRotation = Quaternion.Euler(-8f, 35f, 6f);
                GameObject f = new GameObject("hulk fire");
                f.transform.SetParent(ground.Hulk.transform, false);
                f.transform.localPosition = new Vector3(0f, 2.4f, 0f);
                hulkFire = f.AddComponent<Light>();
                hulkFire.type = LightType.Point;
                hulkFire.color = new Color(1f, 0.55f, 0.2f);
                hulkFire.range = 12f;
            }
            if (hulkFire != null) hulkFire.intensity = 2.5f + Mathf.PerlinNoise(Time.time * 6f, 0f) * 2.5f;
        }

        void Track(List<Health> list, bool near, bool popping)
        {
            foreach (Health h in list)
            {
                if (h == null) continue;
                float since;
                bool down = downSince.TryGetValue(h, out since);
                if (h.IsDead && !down)
                {
                    downSince[h] = Time.time;
                    bool aiming = player != null && player.IsAiming;
                    if (state == State.Running && station == 4)
                    {
                        if (near && ticks[0] && !ticks[1] && !aiming) { nearHits++; if (nearHits >= 3) Tick(1); }
                        if (!near && ticks[2] && !ticks[3] && aiming) { farHits++; if (farHits >= 3) Tick(3); }
                    }
                    if (h == popUp) { popUp = null; popNextAt = Time.time + 0.8f; }
                }
                else if (h.IsDead && down && Time.time - since > (popping ? 0.7f : 3f))
                {
                    Revive(h);
                    downSince.Remove(h);
                    if (popping) lowered.Add(h);
                }
            }
            if (popping)
            {
                foreach (Health h in list) if (h != null && h != popUp && !h.IsDead) lowered.Add(h);
                if (popUp != null && list.Contains(popUp) && Time.time > popUntil)
                {
                    lowered.Add(popUp);   // missed: down it goes, the next one comes up
                    popUp = null;
                    popNextAt = Time.time + 0.6f;
                }
                if (popUp == null && Time.time >= popNextAt)
                {
                    List<Health> ready = new List<Health>();
                    foreach (Health h in list) if (h != null && !h.IsDead && h != lastPop) ready.Add(h);
                    if (ready.Count > 0)
                    {
                        popUp = ready[UnityEngine.Random.Range(0, ready.Count)];
                        lastPop = popUp;
                        lowered.Remove(popUp);
                        popUntil = Time.time + (near ? 4.5f : 6f);
                        Clack(popUp);
                    }
                }
            }
            else
            {
                // At the range a lane stays down until its turn is over, so the first pop-up is the first target seen.
                bool waiting = state == State.Running && station == 4 && (near ? !ticks[1] : !ticks[3]);
                foreach (Health h in list)
                {
                    if (h == null) continue;
                    if (waiting) lowered.Add(h); else lowered.Remove(h);
                }
                if (popUp != null && list.Contains(popUp)) popUp = null;
            }
            foreach (Health h in list) if (h != null) Show(h, h.IsDead || lowered.Contains(h));
        }

        Health lastPop;

        /// <summary>Raises or lowers a target; a lowered board cannot be hit.</summary>
        static void Show(Health h, bool down)
        {
            Fall(h, down);
            Collider c = h.GetComponentInChildren<Collider>(true);
            if (c != null && c.enabled == down) c.enabled = !down;
        }

        void Clack(Health h)
        {
            if (clack != null && h != null) AudioSource.PlayClipAtPoint(clack, h.transform.position + Vector3.up, 0.9f);
        }

        static void Fall(Health h, bool down)
        {
            Transform hinge = h.transform.Find("hinge");
            if (hinge == null) return;
            Quaternion want = Quaternion.Euler(down ? 85f : 0f, 0f, 0f);
            hinge.localRotation = Quaternion.RotateTowards(hinge.localRotation, want, 400f * Time.deltaTime);
        }

        static void Revive(Health h)
        {
            try
            {
                if (deadProp != null && deadProp.GetSetMethod(true) != null) deadProp.SetValue(h, false, null);
                if (currentProp != null && currentProp.GetSetMethod(true) != null) currentProp.SetValue(h, h.maxHealth, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Stan's Game: bootcamp could not raise a target (" + e.Message + ")");
            }
        }

        // ------------------------------------------------------------------ menu offer

        void OfferKeys()
        {
            Keyboard k = Keyboard.current;
            Gamepad g = Gamepad.current;
            if (Time.unscaledTime - offerOpened < 0.2f) return;
            bool up = (k != null && (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame)) || (g != null && g.dpad.up.wasPressedThisFrame);
            bool down = (k != null && (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame)) || (g != null && g.dpad.down.wasPressedThisFrame);
            bool ok = (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame)) || (g != null && g.buttonSouth.wasPressedThisFrame);
            bool back = (k != null && k.escapeKey.wasPressedThisFrame) || (g != null && g.buttonEast.wasPressedThisFrame);
            if (up || down) { offerFocus = 1 - offerFocus; Play(tick, 0.4f); }
            if (ok) ChooseOffer(offerFocus);
            else if (back) CloseOffer();
        }

        static void CloseOffer()
        {
            OfferOpen = false;
            offerClosedFrame = Time.frameCount;
        }

        static void ChooseOffer(int i)
        {
            CloseOffer();
            if (offerStart == null) return;
            offerStart(i == 0 ? LevelCatalogue.BootcampIndex() : LevelCatalogue.ContinueIndex());
        }

        // ------------------------------------------------------------------ drawing

        static void Styles()
        {
            if (white == null)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (small != null) return;
            GUIStyle b = new GUIStyle(GUI.skin.label);
            b.richText = false;
            b.normal.textColor = Color.white;
            small = new GUIStyle(b); small.fontStyle = FontStyle.Bold;
            title = new GUIStyle(b); title.fontStyle = FontStyle.Bold;
            line = new GUIStyle(b); line.wordWrap = true; line.fontStyle = FontStyle.Italic;
            item = new GUIStyle(b);
            card = new GUIStyle(b); card.alignment = TextAnchor.MiddleCenter; card.wordWrap = true;
            cardBig = new GUIStyle(b); cardBig.alignment = TextAnchor.MiddleCenter; cardBig.fontStyle = FontStyle.Bold;
        }

        void OnGUI()
        {
            if (OfferOpen) { DrawOffer(); return; }
            if (state != State.Running && state != State.Finished) return;
            GUI.depth = -6;
            if (Event.current.type != EventType.Repaint) return;
            if (player == null || ground == null || SettingsPanel.Open || Net.InMenu) return;
            Styles();
            float s = Mathf.Clamp(Screen.height / 1080f, 0.8f, 1.4f);
            Color keep = GUI.color;
            if (state == State.Finished) DrawFinish(s);
            else
            {
                Health h = player.GetComponent<Health>();
                if (h != null && h.IsDead) DrawCentre(s, "YOU'RE DOWN", "Back on your feet in a few seconds, at this station.");
                else
                {
                    DrawMarker(s);
                    DrawPanel(s);
                    if (station == 8 && ticks[2] && holdH > 0f) DrawSend(s);
                }
            }
            GUI.color = keep;
        }

        void DrawPanel(float s)
        {
            int n = ItemCount(station);
            float w = 640f * s, x = (Screen.width - w) * 0.5f, y = 14f * s;
            float pad = 18f * s;
            small.fontSize = Mathf.RoundToInt(14f * s);
            title.fontSize = Mathf.RoundToInt(26f * s);
            line.fontSize = Mathf.RoundToInt(17f * s);
            item.fontSize = Mathf.RoundToInt(17f * s);
            bool praising = Time.time < praiseUntil;
            string say = praising ? praise : "\"" + Lines[station] + "\"";
            float lh = line.CalcHeight(new GUIContent(say), w - 2f * pad);
            float rowH = 26f * s;
            float h = pad + 20f * s + 34f * s + lh + 10f * s + (praising ? 0f : n * rowH) + 30f * s + pad * 0.5f;

            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(x, y, w, h), white);
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(x, y, w, 3f * s), white);

            float cy = y + pad * 0.7f;
            GUI.color = Accent;
            string clock = station == 8 && RunClock >= 0f ? "   ·   RUN " + FormatRun(RunClock) : "";
            GUI.Label(new Rect(x + pad, cy, w - 2f * pad, 20f * s), "BOOTCAMP   ·   COUNTESS WEAR, SPRING 1944   ·   STATION " + (station + 1) + " OF " + StationTitles.Length + clock, small);
            cy += 20f * s;
            GUI.color = Color.white;
            GUI.Label(new Rect(x + pad, cy, w - 2f * pad, 34f * s), StationTitles[station].ToUpperInvariant(), title);
            cy += 34f * s;
            GUI.color = new Color(1f, 1f, 1f, 0.88f);
            GUI.Label(new Rect(x + pad, cy, w - 2f * pad, lh), say, line);
            cy += lh + 10f * s;
            if (!praising)
            {
                int open = FirstOpen();
                for (int i = 0; i < n; i++)
                {
                    bool done = ticks[i];
                    bool active = Sequential(station) ? i == open : !done;
                    float box = 14f * s;
                    Rect br = new Rect(x + pad, cy + (rowH - box) * 0.5f, box, box);
                    GUI.color = done ? new Color(0.55f, 0.85f, 0.5f) : new Color(1f, 1f, 1f, active ? 0.9f : 0.35f);
                    GUI.DrawTexture(br, white);
                    if (!done)
                    {
                        GUI.color = new Color(0f, 0f, 0f, 0.85f);
                        GUI.DrawTexture(new Rect(br.x + 2f, br.y + 2f, br.width - 4f, br.height - 4f), white);
                    }
                    GUI.color = done ? new Color(0.7f, 0.9f, 0.65f) : new Color(1f, 1f, 1f, active ? 1f : 0.45f);
                    GUI.Label(new Rect(br.xMax + 12f * s, cy, w - 2f * pad - box - 12f * s, rowH), ItemText(station, i), item);
                    cy += rowH;
                }
            }
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            GUI.Label(new Rect(x + pad, y + h - 26f * s, w - 2f * pad, 22f * s),
                station == 0 ? "F10  settings      Esc  game menu      Backspace  skip station" : "Backspace  skip station      Esc  game menu", small);
        }

        string ItemText(int s, int i)
        {
            string move = KeyLabel.Move;
            switch (s)
            {
                case 0:
                    if (i == 0) return "Look left and right   ·   mouse";
                    if (i == 1) return "Switch the view, then switch back   ·   " + KeyLabel.Of(Key.V) + ", " + KeyLabel.Of(Key.V);
                    return "Walk to the flag   ·   " + move;
                case 1:
                    if (i == 0) return "Sprint   ·   hold Shift";
                    if (i == 1) return "Jump the ditch   ·   Space";
                    if (i == 2) return "Climb the wall   ·   Space against it, or keep going";
                    return "Through the finish posts";
                case 2:
                    if (i == 0) return "Kneel   ·   " + KeyLabel.Of(Key.C);
                    if (i == 1) return "Go prone   ·   " + KeyLabel.Of(Key.Z);
                    if (i == 2) return "Crawl under the wire   ·   prone, " + move;
                    return "Kneel behind the sandbags   ·   " + KeyLabel.Of(Key.C);
                case 3:
                    if (i == 0) return "Into the water   ·   walk off the bank";
                    if (i == 1) return "Swim across   ·   " + move;
                    return "Climb out on the far bank   ·   swim into it";
                case 4:
                    if (i == 0) return "Go to the firing point";
                    if (i == 1) return "Sten from the hip, 25 m targets   ·   left mouse   (" + Mathf.Min(nearHits, 3) + " / 3 hits)";
                    if (i == 2) return "Pick up the rifle on the table   ·   " + KeyLabel.Of(Key.E);
                    if (i == 3) return "Aim at the 50 m targets   ·   hold right mouse   (" + Mathf.Min(farHits, 3) + " / 3 hits)";
                    if (i == 4) return "Reload   ·   " + KeyLabel.Of(Key.R);
                    return "Pistol, then rifle again   ·   2, then 1 (or Tab, or the wheel)";
                case 5:
                    if (i == 0) return "Into the throwing bay";
                    if (i == 1) return "Select the grenades   ·   3";
                    return "Throw one into the pit   ·   left mouse, aim high";
                case 6:
                    if (i == 0) return "Pick up the PIAT   ·   " + KeyLabel.Of(Key.E);
                    if (i == 1) return "Aim it   ·   hold right mouse";
                    return "Hit the tank   ·   left mouse, aim a little high";
                case 7:
                    if (i == 0) return "Report to the aid post";
                    if (i == 1) return "You're wounded: watch the health panel";
                    return "Put on a field dressing   ·   4, takes a few seconds";
                case 8:
                    if (i == 0) return "Over the start line   ·   the clock starts";
                    if (i == 1) return "Drop the defenders   (" + defendersDown + " / " + ground.Defenders.Count + ")";
                    if (i == 2) return "Take the bridge";
                    return "Send the signal   ·   hold " + KeyLabel.Of(Key.H);
            }
            return "";
        }

        /// <summary>Where the marker points for the current item, with its label; false for none.</summary>
        bool MarkerFor(out Vector3 local, out string label)
        {
            local = Vector3.zero;
            label = null;
            int open = FirstOpen();
            switch (station)
            {
                case 0: local = new Vector3(0f, 2.2f, 8f); label = "FALL IN"; return true;
                case 1:
                    if (!ticks[1]) { local = new Vector3(0f, 0.5f, 28.8f); label = "JUMP"; }
                    else if (!ticks[2]) { local = new Vector3(0f, 1.4f, 38.5f); label = "CLIMB"; }
                    else { local = new Vector3(0f, 1.5f, 46f); label = "FINISH"; }
                    return true;
                case 2:
                    if (!ticks[2]) { local = new Vector3(0f, 0.6f, TrainingGround.WireZ0); label = "CRAWL"; }
                    else { local = new Vector3(0f, 1.2f, TrainingGround.BagsZ); label = "KNEEL"; }
                    return true;
                case 3: local = new Vector3(0f, 0.5f, TrainingGround.PitZ1 + 1.2f); label = "FAR BANK"; return true;
                case 4:
                    if (open == 0) { local = new Vector3(7f, 1f, 94f); label = "FIRING POINT"; return true; }
                    if ((open == 1 || open == 3) && popUp != null)
                    {
                        local = ground.Local(popUp.transform.position) + new Vector3(0f, 2.2f, 0f);
                        label = open == 1 ? "25 m" : "50 m";
                        return true;
                    }
                    if (open == 1) { local = new Vector3(32f, 1.6f, 94f); label = "25 m"; return true; }
                    if (open == 2) { local = new Vector3(8.2f, 1.1f, 94f); label = "PICK UP"; return true; }
                    if (open == 3) { local = new Vector3(57f, 1.6f, 94f); label = "50 m"; return true; }
                    return false;
                case 5:
                    if (open == 0) { local = new Vector3(7f, 1f, 116f); label = "THROWING BAY"; return true; }
                    if (open == 2) { local = new Vector3(29f, 1f, 116f); label = "PIT"; return true; }
                    return false;
                case 6:
                    if (open == 0) { local = new Vector3(8.2f, 1.1f, 132f); label = "PICK UP"; return true; }
                    local = new Vector3(49f, 2f, 132f); label = "DESTROY"; return true;
                case 7:
                    if (open == 0) { local = new Vector3(-2f, 1.2f, 145f); label = "AID POST"; return true; }
                    return false;
                case 8:
                    if (open == 0) { local = new Vector3(0f, 1f, TrainingGround.RunStartZ); label = "START"; return true; }
                    if (open == 1)
                    {
                        Health best = null;
                        float bd = float.MaxValue;
                        foreach (Health h in ground.Defenders)
                        {
                            if (!TargetUp(h)) continue;
                            float d = (h.transform.position - player.transform.position).sqrMagnitude;
                            if (d < bd) { bd = d; best = h; }
                        }
                        if (best == null) return false;
                        local = ground.Local(best.transform.position) + new Vector3(0f, 2.2f, 0f);
                        label = "DEFENDER";
                        return true;
                    }
                    if (open == 2) { local = new Vector3(0f, 1.2f, 164f); label = "TAKE"; return true; }
                    return false;
            }
            return false;
        }

        void DrawMarker(float s)
        {
            Vector3 local;
            string label;
            if (Time.time < praiseUntil || !MarkerFor(out local, out label)) return;
            Camera cam = player.cam != null ? player.cam : Camera.main;
            if (cam == null) return;
            Vector3 world = ground.W(local.x, local.y, local.z);
            Vector3 sp = cam.WorldToScreenPoint(world);
            bool behind = sp.z < 0f;
            if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }
            float gx = sp.x, gy = Screen.height - sp.y;
            float m = 60f * s;
            bool off = behind || gx < m || gx > Screen.width - m || gy < m || gy > Screen.height - m;
            if (behind)
            {
                gy = Screen.height - m;
                gx = gx < Screen.width * 0.5f ? m : Screen.width - m;
            }
            gx = Mathf.Clamp(gx, m, Screen.width - m);
            gy = Mathf.Clamp(gy, m, Screen.height - m);
            float d = Vector3.Distance(player.transform.position, world);
            float size = 14f * s;
            Matrix4x4 keepM = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(gx, gy));
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(gx - size * 0.5f - 2f, gy - size * 0.5f - 2f, size + 4f, size + 4f), white);
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(gx - size * 0.5f, gy - size * 0.5f, size, size), white);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(gx - size * 0.2f, gy - size * 0.2f, size * 0.4f, size * 0.4f), white);
            GUI.matrix = keepM;
            small.fontSize = Mathf.RoundToInt(14f * s);
            string text = label + "   " + Mathf.RoundToInt(d) + " m" + (off ? "" : "");
            Vector2 ts = small.CalcSize(new GUIContent(text));
            float lx = Mathf.Clamp(gx - ts.x * 0.5f, 4f, Screen.width - ts.x - 4f);
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(lx + 1f, gy + size + 1f, ts.x, ts.y), text, small);
            GUI.color = Color.white;
            GUI.Label(new Rect(lx, gy + size, ts.x, ts.y), text, small);
        }

        void DrawSend(float s)
        {
            float w = 320f * s, h = 10f * s, x = (Screen.width - w) * 0.5f, y = Screen.height * 0.62f;
            float f = Mathf.Clamp01(holdH / SendSeconds());
            card.fontSize = Mathf.RoundToInt(17f * s);
            GUI.color = Color.white;
            GUI.Label(new Rect(x - 100f * s, y - 34f * s, w + 200f * s, 26f * s), f >= 1f ? "\"Ham and Jam\" sent" : "Sending: Ham... and... Jam...", card);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x, y, w, h), white);
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(x, y, w * f, h), white);
        }

        void DrawCentre(float s, string big, string text)
        {
            cardBig.fontSize = Mathf.RoundToInt(40f * s);
            card.fontSize = Mathf.RoundToInt(19f * s);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, Screen.height * 0.38f, Screen.width, 120f * s), white);
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, Screen.height * 0.38f + 14f * s, Screen.width, 50f * s), big, cardBig);
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(0f, Screen.height * 0.38f + 66f * s, Screen.width, 30f * s), text, card);
        }

        void DrawFinish(float s)
        {
            float y0 = Screen.height * 0.3f;
            cardBig.fontSize = Mathf.RoundToInt(46f * s);
            card.fontSize = Mathf.RoundToInt(20f * s);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(0f, y0, Screen.width, 300f * s), white);
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(0f, y0, Screen.width, 3f * s), white);
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, y0 + 24f * s, Screen.width, 60f * s), "BOOTCAMP COMPLETE", cardBig);
            GUI.color = Accent;
            GUI.Label(new Rect(0f, y0 + 88f * s, Screen.width, 30f * s), "Countess Wear   ·   " + praise, card);
            if (runLine.Length > 0) GUI.Label(new Rect(0f, y0 + 118f * s, Screen.width, 30f * s), runLine, card);
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.Label(new Rect(Screen.width * 0.2f, y0 + 154f * s, Screen.width * 0.6f, 60f * s),
                "\"Ham and Jam. That'll do, lads. Next time it's the real thing: the canal bridge at Bénouville, 6 June 1944, 00:16.\"", card);
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, y0 + 230f * s, Screen.width, 30f * s), "Enter   on to Pegasus Bridge          R   run the bridge again          Esc, then Leave the game   main menu", card);
        }

        void DrawOffer()
        {
            GUI.depth = -110;   // over the main menu
            Styles();
            float s = Mathf.Max(0.6f, Screen.height / 1080f);
            Event e = Event.current;
            Color keep = GUI.color;
            float w = 760f * s, h = 400f * s, x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;
            if (e.type == EventType.Repaint)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), white);
                GUI.color = new Color(0.06f, 0.06f, 0.07f, 0.96f);
                GUI.DrawTexture(new Rect(x, y, w, h), white);
                GUI.color = Accent;
                GUI.DrawTexture(new Rect(x, y, w, 4f * s), white);
            }
            title.fontSize = Mathf.RoundToInt(34f * s);
            line.fontSize = Mathf.RoundToInt(19f * s);
            item.fontSize = Mathf.RoundToInt(24f * s);
            small.fontSize = Mathf.RoundToInt(15f * s);
            float pad = 40f * s;
            GUI.color = Color.white;
            GUI.Label(new Rect(x + pad, y + 30f * s, w - 2f * pad, 44f * s), "BOOTCAMP FIRST?", title);
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.Label(new Rect(x + pad, y + 82f * s, w - 2f * pad, 80f * s),
                "Five minutes at Countess Wear, Exeter, where D Company rehearsed the bridges in spring 1944. Every control of the game, one station at a time.", line);
            string[] opts = { "BOOTCAMP   ·   ABOUT 5 MINUTES", "STRAIGHT TO PEGASUS BRIDGE" };
            for (int i = 0; i < 2; i++)
            {
                Rect r = new Rect(x + pad, y + 190f * s + i * 64f * s, w - 2f * pad, 52f * s);
                if (e.type == EventType.Repaint && r.Contains(e.mousePosition) && (e.mousePosition - offerMouse).sqrMagnitude > 1f) offerFocus = i;
                if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { e.Use(); ChooseOffer(i); GUI.color = keep; return; }
                bool on = offerFocus == i;
                if (e.type == EventType.Repaint)
                {
                    GUI.color = new Color(1f, 1f, 1f, on ? 0.08f : 0.03f);
                    GUI.DrawTexture(r, white);
                    if (on)
                    {
                        GUI.color = Accent;
                        GUI.DrawTexture(new Rect(r.x, r.y + 8f * s, 5f * s, r.height - 16f * s), white);
                    }
                }
                GUI.color = new Color(1f, 1f, 1f, on ? 1f : 0.55f);
                GUI.Label(new Rect(r.x + 20f * s, r.y, r.width - 20f * s, r.height), opts[i], item);
            }
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            GUI.Label(new Rect(x + pad, y + h - 46f * s, w - 2f * pad, 24f * s), "The bootcamp is always in the level select.      Enter  confirm      Esc  back", small);
            if (e.type == EventType.Repaint) offerMouse = e.mousePosition;
            GUI.color = keep;
        }

        // ------------------------------------------------------------------ sounds

        void Play(AudioClip c, float v)
        {
            if (c != null && sfx != null) sfx.PlayOneShot(c, v);
        }

        /// <summary>A sergeant-major's whistle: a shrill pea whistle with its trill.</summary>
        static AudioClip Whistle()
        {
            int rate = 44100, n = Mathf.CeilToInt(rate * 0.55f);
            float[] d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float trill = 1f + 0.035f * Mathf.Sin(2f * Mathf.PI * 28f * t);
                phase += 2f * Mathf.PI * 2750f * trill / rate;
                float env = Mathf.Min(1f, t * 80f) * Mathf.Min(1f, (0.55f - t) * 20f);
                float am = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 28f * t);
                d[i] = Mathf.Sin(phase) * env * am * 0.22f;
            }
            AudioClip c = AudioClip.Create("bootcamp whistle", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>A short bugle call for the end (G4 C5 E5 G5).</summary>
        static AudioClip Bugle()
        {
            int rate = 44100;
            float[] notes = { 392f, 523.3f, 659.3f, 784f };
            float[] lens = { 0.18f, 0.18f, 0.18f, 0.7f };
            float total = 0f;
            foreach (float l in lens) total += l;
            int n = Mathf.CeilToInt(rate * total);
            float[] d = new float[n];
            int at = 0;
            for (int k = 0; k < notes.Length; k++)
            {
                int len = Mathf.CeilToInt(rate * lens[k]);
                for (int i = 0; i < len && at + i < n; i++)
                {
                    float t = (float)i / rate;
                    float env = Mathf.Min(1f, t * 60f) * Mathf.Exp(-t * (k == notes.Length - 1 ? 2.5f : 6f));
                    float f = notes[k];
                    float v = Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * f * t) + 0.15f * Mathf.Sin(6f * Mathf.PI * f * t);
                    d[at + i] = v * env * 0.3f;
                }
                at += len;
            }
            AudioClip c = AudioClip.Create("bootcamp bugle", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>The clatter of a pop-up target's frame: a short knock of filtered noise.</summary>
        static AudioClip Clack()
        {
            int rate = 44100, n = Mathf.CeilToInt(rate * 0.12f);
            float[] d = new float[n];
            System.Random r = new System.Random(5);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float noise = (float)(r.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.25f;
                float knock = Mathf.Sin(2f * Mathf.PI * 180f * t) * Mathf.Exp(-t * 40f);
                d[i] = (lp * 0.8f * Mathf.Exp(-t * 55f) + knock * 0.6f) * 0.5f;
            }
            AudioClip c = AudioClip.Create("bootcamp target clack", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip Tone(string name, float f0, float f1, float seconds, float vol)
        {
            int rate = 44100, n = Mathf.CeilToInt(rate * seconds);
            float[] d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                phase += 2f * Mathf.PI * Mathf.Lerp(f0, f1, t) / rate;
                d[i] = Mathf.Sin(phase) * Mathf.Exp(-5f * t) * Mathf.Min(1f, i / 60f) * vol;
            }
            AudioClip c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
