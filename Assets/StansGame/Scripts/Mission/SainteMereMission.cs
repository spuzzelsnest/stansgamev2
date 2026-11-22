using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace StansGame
{
    /// <summary>
    /// Chapter 2: Sainte-Mère-Église, the night of 5/6 June 1944 (Mission Boston, 82nd Airborne Division).
    ///
    /// The player jumps with a stick of F Company, 2nd Battalion, 505th PIR that is dropped by mistake right over the
    /// town at about 01:50. The square is lit up by the burning Pommier house and the German garrison is out in it,
    /// guarding the civilians of the bucket chain. Get down alive, get out of the square and join Lt. Col. Krause's
    /// 3rd Battalion gathering west of the town. At about 04:00 they go in, clear the square, hoist the flag on the
    /// town hall (04:30) and hold the town against the counter-attacks of the morning.
    ///
    /// Faction.British stands for "the Allies" in the shared combat code, so the Americans use it too.
    /// Single player for now; dying sends you back to the last checkpoint (the scene reloads).
    /// </summary>
    public class SainteMereMission : MonoBehaviour
    {
        [System.Serializable]
        public class Phase
        {
            public string title;
            [TextArea] public string objective;
        }

        public static SainteMereMission Instance { get; private set; }

        [Header("The jump")]
        public SoldierController player;
        public ParachuteDrop playerChute;
        public C47Flight playerPlane;
        public C47Flight[] serial;                  // the rest of the formation flying over
        public float autoJumpAfter = 3.5f;            // the jumpmaster does not wait for ever

        [Header("On the ground")]
        public Transform squareCentre;
        public float squareRadius = 40f;
        public Transform rallyPoint;                // the orchard west of the town where 3/505 gathers
        public float rallyRadius = 14f;
        public Transform townHall;                  // the Mairie, where the flag went up
        public float townHallRadius = 7f;
        public GameObject flag;                     // the Stars and Stripes, hidden until hoisted
        public GameObject nightGarrison;            // the Germans in the square during the drop
        public GameObject townDefenders;            // who is still there at 04:00
        public GameObject krauseBattalion;          // 3/505, out of play until the player reaches the rally point
        public Transform[] krauseTargets;           // square, then the town hall
        public GameObject[] counterAttacks;         // from the Carentan road (south) and from Neuville (north)
        public float[] counterAttackDelays = { 10f, 70f };
        public float holdSeconds = 150f;

        [Header("Clock")]
        public int startHour = 1, startMinute = 48;
        public float timeScaleMinutesPerSecond = 0.5f;

        public Phase[] phases =
        {
            new Phase { title = "Mission Boston", objective = "Stand in the door. When the light turns green: SPACE to jump." },
            new Phase { title = "Coming down over the town", objective = "A/D turn, W run with the wind, S slip. Keep out of the square and away from the fire!" },
            new Phase { title = "Out of the square", objective = "Get clear of the square and make for the orchard west of the town, where 3rd Battalion is gathering." },
            new Phase { title = "Into Sainte-Mère-Église", objective = "Go in with Lt. Col. Krause's 3rd Battalion. Clear the Germans out of the church square." },
            new Phase { title = "The first town in France", objective = "Hoist the Stars and Stripes on the town hall." },
            new Phase { title = "Hold the town", objective = "Counter-attack! Hold the square against the Germans coming up the Carentan road and down from Neuville." },
            new Phase { title = "Liberated", objective = "06:00. Krause reports: Sainte-Mère-Église is under his control." },
        };

        public int CurrentPhase { get; private set; }
        public float Minutes { get; set; }
        public string ClockText { get { int total = startHour * 60 + startMinute + (int)Minutes; return $"{(total / 60) % 24:00}:{total % 60:00}"; } }

        // checkpoint: survives the scene reload after the player is killed
        static int checkpoint;
        static Vector3 checkpointPos;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCheckpoint() { checkpoint = 0; checkpointPos = Vector3.zero; }

        string message; float messageUntil;
        float phaseStart, fade, deadSince = -1f;
        bool waitingInDoor; float doorSince;
        int wavesLaunched;
        readonly List<Health> defenders = new List<Health>();
        readonly Dictionary<Collider, bool> colliderState = new Dictionary<Collider, bool>();

        void Awake()
        {
            Instance = this;
            MissionText.Show = Say;
            if (Net.Active)
            {
                // co-op: every player is a network soldier (NetPlayer) and the stick in the door is the players;
                // the single-player soldier and the AI stick-mates stay out
                checkpoint = 0;
                if (player) Destroy(player.gameObject);
                player = null; playerChute = null;
                if (playerPlane)
                {
                    foreach (var man in playerPlane.stick) if (man) Destroy(man.gameObject);
                    playerPlane.stick.Clear(); playerPlane.playerSlot = 0;
                }
            }
        }

        void Start()
        {
            // the planes are over the Cotentin and the town is already awake (the fire, the first sticks of the 506th)
            SoldierAI.GliderLanded();
            SetGroupLive(krauseBattalion, false);
            SetGroupLive(townDefenders, false);
            if (counterAttacks != null) foreach (var g in counterAttacks) SetGroupLive(g, false);
            if (flag) flag.SetActive(false);

            if (checkpoint >= 2) { ResumeAtCheckpoint(); return; }

            Minutes = 0f;
            if (player && player.gameObject.activeInHierarchy && !Net.Active) player.SetControlActive(false);   // normally already waiting in the aircraft (inactive)
            if (playerPlane)
            {
                playerPlane.PlayerAtDoor = () => { if (!waitingInDoor) { waitingInDoor = true; doorSince = Time.time; Say("GREEN LIGHT! GO! GO! GO!   (SPACE)", 4f); } };
                playerPlane.Launch(4f);
            }
            if (serial != null) for (int i = 0; i < serial.Length; i++) if (serial[i]) serial[i].Launch(Random.Range(0f, 14f));
            if (playerChute && !Net.Active) playerChute.Landed += _ => OnPlayerLanded();
            StartCoroutine(Intro());
        }

        IEnumerator Intro()
        {
            Say("5 June 1944, 23:00. In Sainte-Mère-Église a house on the square is on fire.", 5f);
            yield return new WaitForSeconds(5.5f);
            Say("The church bell rings the alarm. Townspeople pass buckets under German guard.", 5f);
            yield return new WaitForSeconds(5.5f);
            Say("6 June, 01:48. F Company, 2nd Battalion, 505th Parachute Infantry. Red light. Stand up, hook up!", 6f);
        }

        void ResumeAtCheckpoint()
        {
            CurrentPhase = checkpoint; phaseStart = Time.time;
            if (playerPlane) playerPlane.gameObject.SetActive(false);
            if (playerChute) playerChute.enabled = false;
            if (player) { player.gameObject.SetActive(true); player.SetControlActive(true, checkpointPos + Vector3.up * 0.2f); }
            Minutes = checkpoint >= 3 ? 130f : 15f;
            SetGroupLive(nightGarrison, checkpoint < 3);
            if (checkpoint >= 3) { SetGroupLive(krauseBattalion, true); OrderKrause(checkpoint >= 4 ? 1 : 0); }
            if (checkpoint == 3) { SetGroupLive(townDefenders, true); CollectDefenders(); }
            if (checkpoint >= 4 && flag && checkpoint >= 5) flag.SetActive(true);
            Say("Back in the fight.", 3f);
        }

        bool menuOpen;

        void OnDestroy() { SoldierAI.AtEase = false; }   // not into the next level

        void Update()
        {
            SoldierAI.AtEase = CurrentPhase >= phases.Length - 1;   // liberated: the paratroopers stand down
            var keys = UnityEngine.InputSystem.Keyboard.current;
            if (!Net.Active && keys != null && keys.escapeKey.wasPressedThisFrame)
            {
                menuOpen = !menuOpen;
                Cursor.lockState = menuOpen ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = menuOpen;
            }
            if (Time.time > messageUntil) message = null;
            Minutes += Time.deltaTime * timeScaleMinutesPerSecond;
            fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime * 0.6f);

            if (Net.Active) { CoopUpdate(); return; }

            if (PlayerDead())
            {
                if (deadSince < 0f) deadSince = Time.time;
                if (Time.time - deadSince > 6f)
                {
                    if (checkpoint < 2) { checkpoint = 0; }
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().name : gameObject.scene.name);
                }
                return;
            }

            switch (CurrentPhase)
            {
                case 0:
                    InTheDoor();
                    break;
                case 2:
                    if (rallyPoint && Near(rallyPoint.position, rallyRadius))
                    {
                        Checkpoint(3, rallyPoint.position);
                        StartCoroutine(TwoHoursLater());
                    }
                    break;
                case 3:
                    defenders.RemoveAll(h => !h || h.IsDead);
                    if (defenders.Count <= 1 && Time.time - phaseStart > 5f)
                    {
                        Advance("The square is ours. Get the flag up on the town hall!");
                        OrderKrause(1);
                    }
                    break;
                case 4:
                    if (townHall && Near(townHall.position, townHallRadius))
                    {
                        if (flag) flag.SetActive(true);
                        Minutes = Mathf.Max(Minutes, 162f);   // 04:30
                        Checkpoint(5, townHall.position + townHall.forward * 4f);
                        Advance("04:30. The Stars and Stripes flies over Sainte-Mère-Église.");
                    }
                    break;
                case 5:
                {
                    float t = Time.time - phaseStart;
                    while (counterAttacks != null && wavesLaunched < counterAttacks.Length && wavesLaunched < counterAttackDelays.Length && t >= counterAttackDelays[wavesLaunched])
                    {
                        SetGroupLive(counterAttacks[wavesLaunched], true);
                        Say(wavesLaunched == 0 ? "Germans on the Carentan road, coming from the south!" : "More of them coming down the N13 from Neuville!", 4f);
                        wavesLaunched++;
                    }
                    if (t >= holdSeconds && AttackersLeft() <= 2)
                    {
                        Minutes = Mathf.Max(Minutes, 252f);   // 06:00
                        Advance("Mission complete. Sainte-Mère-Église is held.");
                    }
                    break;
                }
            }
        }

        void InTheDoor()
        {
            if (!waitingInDoor) return;
            var kb = Keyboard.current;
            if ((kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) || Time.time - doorSince > autoJumpAfter)
            {
                waitingInDoor = false;
                if (Time.time - doorSince > autoJumpAfter) Say("The jumpmaster shoves you out of the door!", 3f);
                LocalAboard = false;
                playerPlane.OutOfTheDoor(playerPlane.playerSlot);
                Advance(null);
            }
        }

        // ------------------------------------------------------------------ co-op (multiplayer)
        // Each player jumps from his own copy of the C-47 (cosmetic, same timing), steers his own canopy and lands
        // where he lands. From the rally on the mission is shared: the host runs it (SharedPhase, SharedWaves) and
        // every machine applies the same steps to its world (ApplyShared). A player reaching the orchard or the town
        // hall moves everyone on; the dead come back near the action (NetSession respawns them via SpawnPoint).

        public int SharedPhase { get; private set; } = 2;
        public int SharedWaves { get; private set; }
        public bool LocalAboard { get; private set; }
        public static System.Action<int> ReachedOnHost;   // set by SmeMissionNet on clients: tell the host
        int appliedPhase = 2, appliedWaves;
        float sharedSince;

        /// <summary>The local player's network soldier has spawned: into the aircraft if it is still on its run in, else on the ground.</summary>
        public void SetLocalPlayer(SoldierController sc)
        {
            player = sc; playerChute = sc ? sc.GetComponent<ParachuteDrop>() : null;
            if (!sc) return;
            deadSince = -1f;
            bool board = CurrentPhase == 0 && playerChute && playerPlane && !playerPlane.GreenLight && playerPlane.Progress < 0.3f;
            if (board)
            {
                playerPlane.stick.Clear(); playerPlane.stick.Add(playerChute); playerPlane.playerSlot = 0;
                sc.SetControlActive(false);
                foreach (var r in sc.GetComponentsInChildren<Renderer>()) r.enabled = false;
                var cc = sc.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
                LocalAboard = true;
                playerChute.Landed += _ => OnCoopLanded();
            }
            else
            {
                if (playerChute) playerChute.enabled = false;
                LocalAboard = false;
                sc.SetControlActive(true, sc.transform.position);
                CurrentPhase = Mathf.Max(2, SharedPhase); phaseStart = Time.time;
                Say(SharedPhase >= 3 ? "You're with 3rd Battalion. Back in the fight." : "You're down. Make for the orchard west of the town.", 4f);
            }
        }

        /// <summary>Where the host puts a (re)spawning player.</summary>
        public Vector3 SpawnPoint(ulong clientId)
        {
            Vector3 b = SharedPhase >= 5 && townHall ? townHall.position + townHall.forward * 4f
                      : rallyPoint ? rallyPoint.position : Vector3.zero;
            b += new Vector3((clientId % 4) * 1.6f - 2.4f, 0f, (clientId / 4 % 4) * 1.6f);
            if (Physics.Raycast(b + Vector3.up * 60f, Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore)) b.y = hit.point.y;
            return b + Vector3.up * 0.3f;
        }

        void OnCoopLanded()
        {
            var p = player ? player.transform.position : Vector3.zero;
            bool inSquare = squareCentre && Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(squareCentre.position.x, 0f, squareCentre.position.z)) < squareRadius;
            CurrentPhase = Mathf.Max(2, SharedPhase); phaseStart = Time.time;
            Say(inSquare ? "You're down in the square! Get out of the light!" : "You're down. Find cover and get your bearings.", 5f);
        }

        void CoopUpdate()
        {
            if (PlayerDead()) { if (deadSince < 0f) deadSince = Time.time; }
            else deadSince = -1f;
            if (CurrentPhase == 0) InTheDoor();

            if (Net.IsServer) ServerLogic();
            ApplyShared(SharedPhase, SharedWaves);

            // a player reaching the orchard or the town hall moves the whole mission on
            if (CurrentPhase >= 2 && !PlayerDead())
            {
                if (SharedPhase == 2 && rallyPoint && Near(rallyPoint.position, rallyRadius)) Reached(0);
                if (SharedPhase == 4 && townHall && Near(townHall.position, townHallRadius)) Reached(1);
            }
        }

        void Reached(int what)
        {
            if (Net.IsServer) ServerReached(what);
            else ReachedOnHost?.Invoke(what);
        }

        /// <summary>Host: a player got to the orchard (0) or the town hall (1).</summary>
        public void ServerReached(int what)
        {
            if (what == 0 && SharedPhase == 2) SetShared(3);
            if (what == 1 && SharedPhase == 4) SetShared(5);
        }

        void SetShared(int p) { SharedPhase = p; sharedSince = Time.time; }

        void ServerLogic()
        {
            float t = Time.time - sharedSince;
            if (SharedPhase == 3)
            {
                defenders.RemoveAll(h => !h || h.IsDead);
                if (appliedPhase >= 3 && defenders.Count <= 1 && t > 8f) SetShared(4);
            }
            else if (SharedPhase == 5)
            {
                while (counterAttacks != null && SharedWaves < counterAttacks.Length && SharedWaves < counterAttackDelays.Length && t >= counterAttackDelays[SharedWaves])
                    SharedWaves++;
                if (t >= holdSeconds && SharedWaves >= (counterAttacks?.Length ?? 0) && AttackersLeftCoop() <= 2) SetShared(6);
            }
        }

        int AttackersLeftCoop()
        {
            int n = 0;
            for (int w = 0; counterAttacks != null && w < SharedWaves && w < counterAttacks.Length; w++)
                if (counterAttacks[w]) foreach (var h in counterAttacks[w].GetComponentsInChildren<Health>()) if (!h.IsDead) n++;
            return n;
        }

        /// <summary>Every machine: bring the world up to the host's mission step (called with the synced values).</summary>
        public void ApplyShared(int phase, int waves)
        {
            if (!Net.IsServer) { SharedPhase = phase; SharedWaves = waves; }
            while (appliedPhase < phase) { appliedPhase++; ApplyStep(appliedPhase); }
            while (counterAttacks != null && appliedWaves < waves && appliedWaves < counterAttacks.Length)
            {
                SetGroupLive(counterAttacks[appliedWaves], true);
                Say(appliedWaves == 0 ? "Germans on the Carentan road, coming from the south!" : "More of them coming down the N13 from Neuville!", 4f);
                appliedWaves++;
            }
        }

        void ApplyStep(int p)
        {
            switch (p)
            {
                case 3:
                    fade = 1.2f;
                    Say("Two hours later. 3rd Battalion, 505th: about a hundred and eighty men, Lt. Col. Edward Krause.", 6f);
                    Minutes = Mathf.Max(Minutes, 132f);
                    SetGroupLive(nightGarrison, false);
                    SetGroupLive(townDefenders, true);
                    CollectDefenders();
                    SetGroupLive(krauseBattalion, true);
                    OrderKrause(0);
                    // everyone on the ground goes in with Krause
                    if (player && CurrentPhase >= 2 && rallyPoint && !PlayerDead() && !Near(rallyPoint.position, 45f))
                        player.SetControlActive(true, SpawnPoint(Net.LocalClientId));
                    break;
                case 4:
                    Say("The square is ours. Get the flag up on the town hall!", 5f);
                    OrderKrause(1);
                    break;
                case 5:
                    if (flag) flag.SetActive(true);
                    Minutes = Mathf.Max(Minutes, 162f);
                    Say("04:30. The Stars and Stripes flies over Sainte-Mère-Église.", 5f);
                    break;
                case 6:
                    Minutes = Mathf.Max(Minutes, 252f);
                    Say("Mission complete. Sainte-Mère-Église is held.", 6f);
                    MissionProgress.Complete(gameObject.scene.name, MissionProgress.Elapsed);
                    break;
            }
            if (CurrentPhase >= 2) { CurrentPhase = p; phaseStart = Time.time; }
        }

        void OnPlayerLanded()
        {
            var p = player ? player.transform.position : Vector3.zero;
            bool inSquare = squareCentre && Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(squareCentre.position.x, 0f, squareCentre.position.z)) < squareRadius;
            Checkpoint(2, p);
            Advance(inSquare ? "You're down in the square! Get out of the light!" : "You're down. Find cover and get your bearings.");
            // the stick survivors who landed close by stay with the player
            if (playerPlane)
                foreach (var man in playerPlane.stick)
                {
                    if (!man || man == playerChute) continue;
                    var ai = man.GetComponent<SoldierAI>();
                    if (ai && Vector3.Distance(man.transform.position, p) < 60f) ai.mode = SoldierAI.Mode.FollowPlayer;
                }
        }

        IEnumerator TwoHoursLater()
        {
            fade = 1.2f;
            Say("Two hours later. 3rd Battalion, 505th: about a hundred and eighty men, Lt. Col. Edward Krause.", 6f);
            yield return new WaitForSeconds(0.6f);
            Minutes = Mathf.Max(Minutes, 132f);   // 04:00
            SetGroupLive(nightGarrison, false);   // most of the garrison had pulled out of the town by now
            SetGroupLive(townDefenders, true);
            CollectDefenders();
            SetGroupLive(krauseBattalion, true);
            OrderKrause(0);
            Advance(null);
        }

        void CollectDefenders()
        {
            defenders.Clear();
            if (townDefenders) foreach (var h in townDefenders.GetComponentsInChildren<Health>(true)) if (!h.IsDead) defenders.Add(h);
        }

        void OrderKrause(int target)
        {
            if (!krauseBattalion || krauseTargets == null || krauseTargets.Length == 0) return;
            var t = krauseTargets[Mathf.Clamp(target, 0, krauseTargets.Length - 1)];
            foreach (var ai in krauseBattalion.GetComponentsInChildren<SoldierAI>(true))
            {
                ai.mode = SoldierAI.Mode.Advance; ai.advanceTarget = t; ai.startAlert = true;
            }
        }

        int AttackersLeft()
        {
            int n = 0;
            for (int w = 0; counterAttacks != null && w < wavesLaunched; w++)
                if (counterAttacks[w]) foreach (var h in counterAttacks[w].GetComponentsInChildren<Health>()) if (!h.IsDead) n++;
            return n + (wavesLaunched < (counterAttacks?.Length ?? 0) ? 99 : 0);
        }

        void Checkpoint(int phase, Vector3 pos) { checkpoint = Mathf.Max(checkpoint, phase); checkpointPos = pos; }

        void Advance(string msg)
        {
            if (CurrentPhase + 1 == phases.Length - 1)   // the last phase: the mission is won
                MissionProgress.Complete(gameObject.scene.name, MissionProgress.Elapsed);
            CurrentPhase = Mathf.Min(CurrentPhase + 1, phases.Length - 1);
            phaseStart = Time.time;
            if (!string.IsNullOrEmpty(msg)) Say(msg, 5f);
        }

        public void Say(string msg, float seconds) { message = msg; messageUntil = Time.time + seconds; }

        bool PlayerDead()
        {
            if (!player) return false;
            var h = player.GetComponent<Health>();
            return h && h.IsDead;
        }

        bool Near(Vector3 pos, float r)
        {
            if (!player || !player.enabled) return false;
            Vector3 d = player.transform.position - pos; d.y = 0f;
            return d.magnitude < r;
        }

        /// <summary>Takes a group out of play (hidden, no collisions, not a target, AI off) or puts it back.</summary>
        void SetGroupLive(GameObject group, bool live)
        {
            if (!group) return;
            foreach (var r in group.GetComponentsInChildren<Renderer>(true)) r.enabled = live;
            foreach (var c in group.GetComponentsInChildren<Collider>(true))
            {
                if (!live) { if (!colliderState.ContainsKey(c)) colliderState[c] = c.enabled; c.enabled = false; }
                else if (colliderState.TryGetValue(c, out bool was)) c.enabled = was;
            }
            foreach (var cc in group.GetComponentsInChildren<CharacterController>(true)) cc.enabled = live;
            foreach (var h in group.GetComponentsInChildren<Health>(true)) h.enabled = live;
            foreach (var ai in group.GetComponentsInChildren<SoldierAI>(true)) ai.enabled = live && Net.Simulates;
        }

        // ------------------------------------------------------------------ camera in the door

        void LateUpdate()
        {
            if (CurrentPhase != 0 || !playerPlane || !playerPlane.Flying) return;
            var cam = player && player.cam ? player.cam : Camera.main;
            if (!cam) return;
            Transform d = playerPlane.door ? playerPlane.door : playerPlane.transform;
            // standing in the open cargo door, looking out to port and down at the burning town
            Vector3 eye = d.position + playerPlane.transform.right * 0.6f + Vector3.up * 0.4f;
            Vector3 look = eye - playerPlane.transform.right * 10f - playerPlane.transform.forward * 4f - Vector3.up * 7f;
            float shake = waitingInDoor ? 0.04f : 0.02f;
            cam.transform.SetPositionAndRotation(eye + Random.insideUnitSphere * shake, Quaternion.LookRotation(look - eye));
        }

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            var phase = phases[Mathf.Clamp(CurrentPhase, 0, phases.Length - 1)];
            MissionHud.TopLine(ClockText, phase.title, phase.objective);
            MissionHud.Message(message);
            DrawMarker();
            if (player && player.enabled && player.gameObject.activeInHierarchy && CurrentPhase >= 2) MissionHud.Player(player);
            else if (PlayerDead()) MissionHud.Dead(Net.Active ? "You're hit. Back in the fight in a few seconds..." : "You're hit. Back to the last checkpoint...");
            menuOpen = MissionHud.Menu(menuOpen);
            MissionHud.Fade(fade);
        }

        void DrawMarker()
        {
            Transform target = CurrentPhase == 2 ? rallyPoint : CurrentPhase == 3 ? squareCentre : CurrentPhase == 4 ? townHall : null;
            var cam = Camera.main;
            if (!target || !cam || !player || !player.enabled) return;
            Vector3 sp = cam.WorldToScreenPoint(target.position + Vector3.up * 2f);
            if (sp.z < 0f) { sp.x = Screen.width - sp.x; sp.y = 0f; }
            sp.x = Mathf.Clamp(sp.x, 30f, Screen.width - 30f);
            float y = Mathf.Clamp(Screen.height - sp.y, 80f, Screen.height - 90f);
            int dist = Mathf.RoundToInt(Vector3.Distance(player.transform.position, target.position));
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, richText = true };
            GUI.Label(new Rect(sp.x - 60, y - 12, 120, 24), $"<b>◆</b>  {dist} m", st);
        }
    }
}
