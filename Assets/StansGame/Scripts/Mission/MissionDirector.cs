using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Runs the night of 5/6 June 1944 at the Bénouville bridge:
    ///   00:16 Glider 1 lands, 00:17 Glider 2, 00:18 Glider 3.
    ///   Objective: clear the east-bank pillbox and trenches, then take the bridge.
    ///   Once the west bank is taken the Orne platoons report "Jam"; the player sends "Ham and Jam" on the
    ///   wireless (hold H) until it is acknowledged. Then hold: German probing attacks in three waves (Bénouville,
    ///   Le Port, the east-bank road), 7th Para Bn arriving, ~01:30 armour down the Bénouville road (PIAT),
    ///   snipers until Lord Lovat's commandos and the pipes arrive at 13:00.
    /// Game time runs faster than real time (timeScaleMinutesPerSecond).
    ///
    /// Multiplayer: the host runs the mission; the shared state (phase, clock, landed gliders,
    /// which groups are in play, messages) is mirrored to clients by MissionNet. Every machine
    /// handles its own local player (glider ride camera, HUD).
    /// </summary>
    public class MissionDirector : MonoBehaviour
    {
        [System.Serializable]
        public class Phase
        {
            public string title;
            [TextArea] public string objective;
        }

        public static MissionDirector Instance { get; private set; }

        [Tooltip("The local player. Single player: set by the level builder. Multiplayer: set when this machine's soldier spawns.")]
        public SoldierController player;
        public GliderApproach[] gliders;          // 0 = the players' glider
        public Transform[] gliderSectionSpawns;   // where each section deploys
        public GameObject britishSoldierPrefab;
        public GameObject britishBrenPrefab;      // every third man in a section carries the Bren
        public GameObject britishRiflePrefab;     // Lee-Enfield riflemen
        public int sectionSize = 6;
        public Transform bridgeObjective;          // trigger zone centre at the west end
        public float bridgeRadius = 12f;
        public List<Health> eastBankDefenders = new List<Health>();
        public GameObject tankAttackGroup;         // out of play until phase 3
        public Health panzer;
        public GameObject paraReinforcements;      // 7th Para Bn, out of play until the hold timer
        public float holdSeconds = 180f;           // (old single timer, unused)

        [Header("Holding the bridge")]
        [Tooltip("German probing attacks, out of play until their time: Bénouville road, Le Port (north, west bank), east-bank road.")]
        public GameObject[] attackWaves;
        [Tooltip("Seconds after the signal is acknowledged when each wave comes in.")]
        public float[] waveDelays = { 20f, 80f, 145f };
        public float parasDelay = 110f;            // 7th Para's first companies reach the bridge
        public float tanksDelay = 205f;            // armour from the Bénouville crossroads
        public GameObject snipers;                 // château / village snipers after the armour is beaten off
        public float holdOnSeconds = 120f;         // then the commandos arrive
        public int signalsNeeded = 3;              // "Ham and Jam" sent again and again before anyone answers

        [Header("Clock")]
        public int startHour = 0, startMinute = 16;
        public float timeScaleMinutesPerSecond = 0.5f;

        public Phase[] phases =
        {
            new Phase { title = "Coup de main", objective = "Get out of the glider. Knock out the pillbox and clear the trenches on the east bank." },
            new Phase { title = "Over the bridge", objective = "Cross the bridge and take the west bank by the café." },
            new Phase { title = "Ham and Jam", objective = "Send the success signal: hold H to transmit \"Ham and Jam\" on the wireless. Keep sending until someone answers." },
            new Phase { title = "Hold the bridge", objective = "Germans probing from Bénouville, Le Port and along the canal. Hold the bridge until 7th Parachute Battalion arrives." },
            new Phase { title = "Armour on the road!", objective = "German armour coming from Bénouville. Stop the lead vehicle with the PIAT (3)." },
            new Phase { title = "Hold until relieved", objective = "Snipers in the village and the château grounds. Hold on until the commandos arrive from Sword Beach." },
            new Phase { title = "Bridge held", objective = "The bridges are held. 6 June 1944." },
        };

        // ---- shared mission state (written by the host, mirrored to clients) ----
        public int CurrentPhase { get; set; }
        public float Minutes { get; set; }
        public int LandedMask { get; set; }        // bit i = glider i has landed
        public bool TanksLive { get; set; }
        public bool ParasLive { get; set; }
        public int WavesMask { get; set; }         // bit i = attack wave i in play
        public bool SnipersLive { get; set; }
        public int SignalsSent { get; set; }
        float phaseStart;
        public string Message { get; private set; }
        public int MessageSerial { get; private set; }
        public float MessageSeconds { get; private set; }

        public string ClockText { get { int total = startHour * 60 + startMinute + (int)Minutes; return $"{(total / 60) % 24:00}:{total % 60:00}"; } }
        float messageUntil;
        bool appliedTanks = true, appliedParas = true, appliedSnipers = true;
        int appliedWaves = -1;
        bool started, riding;

        void Awake() => Instance = this;

        void Start()
        {
            for (int i = 0; i < gliders.Length; i++)
            {
                int idx = i;
                if (gliders[i]) gliders[i].Landed += g => OnGliderLanded(idx);
            }
            ApplyGroups();
            if (player && !Net.Active && !Net.InMenu) SetLocalPlayer(player);   // single player
        }

        // ------------------------------------------------------------------ local player

        /// <summary>Called for this machine's own soldier (single player: at start; multiplayer: when it spawns).</summary>
        public void SetLocalPlayer(SoldierController p)
        {
            player = p;
            if (!p) return;
            if ((LandedMask & 1) == 0) StartCoroutine(RidePlayerGlider());
        }

        IEnumerator RidePlayerGlider()
        {
            if (riding) yield break;
            riding = true;
            player.SetControlActive(false);
            var cam = player.cam ? player.cam : Camera.main;
            var g = gliders.Length > 0 ? gliders[0] : null;
            while (g && (LandedMask & 1) == 0)
            {
                if (cam && !CinematicDirector.Playing)
                {
                    // chase camera behind and above the Horsa, looking past it at the bridge
                    Quaternion flat = Quaternion.Euler(0f, g.transform.eulerAngles.y, 0f);
                    Vector3 eye = g.transform.position + flat * new Vector3(2.5f, 6f, -30f);
                    Vector3 look = g.transform.position + flat * Vector3.forward * 40f;
                    cam.transform.SetPositionAndRotation(Vector3.Lerp(cam.transform.position, eye, Time.deltaTime * 4f),
                        Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(look - eye), Time.deltaTime * 4f));
                }
                yield return null;
            }
            while (CinematicDirector.Playing) yield return null;   // let the landing scene finish
            riding = false;
            if (!player) yield break;
            var spawn = (gliderSectionSpawns != null && gliderSectionSpawns.Length > 0 && gliderSectionSpawns[0]) ? gliderSectionSpawns[0] : (g ? g.transform : player.transform);
            // each player climbs out at a slightly different spot
            int slot = (int)(Net.LocalClientId % 6);
            Vector3 exit = spawn.position + spawn.right * (slot - 2.5f) * 1.6f + spawn.forward * 1.5f;
            player.SetControlActive(true, exit);
        }

        // ------------------------------------------------------------------ host side

        void OnGliderLanded(int i)
        {
            if (!Net.Simulates) return;
            LandedMask |= 1 << i;
            var spawn = (gliderSectionSpawns != null && i < gliderSectionSpawns.Length) ? gliderSectionSpawns[i] : gliders[i].transform;
            Say(i == 0 ? "Glider one down. 00:16. Go, go, go!" : $"Glider {i + 1} is down.", i == 0 ? 4f : 3f);

            if (britishSoldierPrefab)
            {
                int players = CountPlayers();
                int n = i == 0 ? Mathf.Max(1, sectionSize - players) : sectionSize;
                for (int k = 0; k < n; k++)
                {
                    Vector3 p = spawn.position + spawn.right * (k - n / 2f) * 1.4f - spawn.forward * 2f;
                    var pick = k % 3 == 2 && britishBrenPrefab ? britishBrenPrefab : k % 3 == 1 && britishRiflePrefab ? britishRiflePrefab : britishSoldierPrefab;
                    var s = Instantiate(pick, p, spawn.rotation);
                    var ai = s.GetComponent<SoldierAI>();
                    if (ai) { ai.mode = SoldierAI.Mode.Advance; ai.advanceTarget = bridgeObjective; ai.startAlert = true; }
                    NetHooks.Spawned?.Invoke(s);
                }
            }
        }

        static int CountPlayers()
        {
            int n = 0;
            foreach (var sc in FindObjectsByType<SoldierController>()) if (sc) n++;
            return Mathf.Max(1, n);
        }

        void Update()
        {
            if (Time.time > messageUntil) Message = null;
            ApplyGroups();
            if (!Net.Simulates) return;   // clients: MissionNet copies the host's state in

            if (!started) { started = true; Say("5 June 1944, 22:56. Six Horsa gliders leave RAF Tarrant Rushton...", 6f); }
            Minutes += Time.deltaTime * timeScaleMinutesPerSecond;

            switch (CurrentPhase)
            {
                case 0:
                    eastBankDefenders.RemoveAll(h => h == null || h.IsDead);
                    if ((LandedMask & 1) != 0 && eastBankDefenders.Count == 0) Advance("East bank clear. Over the bridge!");
                    break;
                case 1:
                    if (bridgeObjective && AnyPlayerWithin(bridgeObjective.position, bridgeRadius))
                    {
                        Advance("West bank taken. The canal bridge is ours: \"Ham\".");
                        StartCoroutine(JamReport());
                    }
                    break;
                case 2:
                    if (SignalsSent >= signalsNeeded)
                    {
                        Advance("\"Ham and Bloody Jam!\" ... at last: \"Ham and Jam received. Well done. Out.\"  Now hold the bridge.");
                        DeployDefence();
                    }
                    break;
                case 3:
                {
                    float t = Time.time - phaseStart;
                    for (int w = 0; attackWaves != null && w < attackWaves.Length && w < waveDelays.Length; w++)
                        if (t >= waveDelays[w] && (WavesMask & (1 << w)) == 0)
                        {
                            WavesMask |= 1 << w;
                            Say(w == 0 ? "Movement on the Bénouville road! Here they come." : w == 1 ? "Germans from Le Port, along the west bank!" : "More of them on the east-bank road, from the south!", 4f);
                        }
                    if (!ParasLive && t >= parasDelay) { ParasLive = true; Say("7th Para Battalion coming in from the drop zone!", 4f); }
                    if (t >= tanksDelay)
                    {
                        Minutes = Mathf.Max(Minutes, 90 - (startHour * 60 + startMinute));   // ~01:30
                        Advance("Engine noise from the Bénouville junction... tanks!");
                        TanksLive = true;
                    }
                    break;
                }
                case 4:
                    if (panzer && panzer.IsDead)
                    {
                        Advance("The lead vehicle is burning. The rest are pulling back. Watch for snipers.");
                        SnipersLive = true;
                    }
                    break;
                case 5:
                    if (Time.time - phaseStart >= holdOnSeconds)
                    {
                        Minutes = Mathf.Max(Minutes, 13 * 60 - (startHour * 60 + startMinute));   // 13:00
                        Advance("Bagpipes! The commandos are here.");
                    }
                    break;
            }
        }

        static bool AnyPlayerWithin(Vector3 pos, float radius)
        {
            foreach (var sc in FindObjectsByType<SoldierController>())
            {
                var h = sc.GetComponent<Health>();
                if (h && h.IsDead) continue;
                if (Vector3.Distance(sc.transform.position, pos) < radius) return true;
            }
            return false;
        }

        IEnumerator JamReport()
        {
            yield return new WaitForSeconds(6f);
            // "Jam" is the Orne bridge: wait for Fox and Sweeney to have it (if the level has the Orne element), at most 2.5 minutes
            if (FindAnyObjectByType<OrneAssault>())
            {
                float t0 = Time.time;
                while (!OrneAssault.Taken && Time.time - t0 < 150f) yield return null;
            }
            Say("Wireless, from the Orne bridge: \"Jam\" — taken intact. Send \"Ham and Jam\": hold H.", 7f);
        }

        // ------------------------------------------------------------------ after "Ham and Jam": ready for the counter-attack

        /// <summary>
        /// Host: Howard's men get ready for the counter-attack that must come. Two or three sappers (Captain Jock
        /// Neilson's Royal Engineers) go over the bridge looking for the demolition charges; the rest split up and take
        /// positions on both banks of the canal - more of them on the west bank, facing the road from Bénouville and
        /// the T-junction where the attacks will come from, and Brens and riflemen along both towpaths, watching up and
        /// down the canal and out to the east. The Orne platoons stay at their bridge.
        /// </summary>
        void DeployDefence()
        {
            Vector3 b = Vector3.zero;
            var men = new List<SoldierAI>();
            foreach (var ai in FindObjectsByType<SoldierAI>())
            {
                var h = ai.GetComponent<Health>();
                if (!h || h.IsDead || h.faction != Faction.British) continue;
                Vector3 d = ai.transform.position - b; d.y = 0f;
                if (d.magnitude > 160f) continue;   // the Orne bridge is theirs to hold
                men.Add(ai);
            }
            if (men.Count == 0) return;
            men.Sort((p, q) => (p.transform.position - b).sqrMagnitude.CompareTo((q.transform.position - b).sqrMagnitude));

            // the search: one man along each side of the deck, a third underneath the west end where the charges would go
            var searches = new List<Vector3[]>
            {
                new[] { new Vector3(-14f, 0f, 2.1f), new Vector3(-7f, 0f, 2.1f), new Vector3(0f, 0f, 2.1f), new Vector3(7f, 0f, 2.1f), new Vector3(14f, 0f, 2.1f) },
                new[] { new Vector3(14f, 0f, -2.1f), new Vector3(7f, 0f, -2.1f), new Vector3(0f, 0f, -2.1f), new Vector3(-7f, 0f, -2.1f), new Vector3(-14f, 0f, -2.1f) },
                new[] { new Vector3(-17f, 0f, 3.5f), new Vector3(-17f, 0f, -3.5f), new Vector3(-12f, 0f, 0f) },
            };
            int sappers = men.Count >= 9 ? 3 : men.Count >= 4 ? 2 : 1;
            var used = new List<Vector3>();
            int idx = 0;
            for (; idx < sappers && idx < men.Count; idx++)
            {
                var pts = searches[idx];
                for (int i = 0; i < pts.Length; i++) pts[i] = OnGround(pts[i]);
                // afterwards: by the east end of the bridge, behind the railing, watching the road from Ranville
                Vector3 after = PostSpot(new Vector3(17f + idx * 1.5f, 0f, idx % 2 == 0 ? 3.2f : -3.2f), used);
                men[idx].SearchThenDefend(pts, 2.6f, after, Vector3.right);
            }

            // the banks: (x side, z along the canal, which way to watch); the west bank first and heaviest
            var posts = new List<(float side, float z, Vector3 watch, float inland)>
            {
                (-1f,  9f, Vector3.forward, 2.5f), (-1f, -9f, Vector3.back, 2.5f),           // west towpath, up and down the canal
                (-1f,  4f, Vector3.left, 20f),     (-1f, -5f, Vector3.left, 20f),            // facing the Bénouville road
                (+1f,  9f, Vector3.forward, 2.5f), (+1f, -9f, Vector3.back, 2.5f),           // east towpath
                (-1f, 20f, Vector3.forward, 3f),   (-1f, -20f, Vector3.back, 3f),
                (-1f, 12f, Vector3.left, 26f),     (-1f, -13f, Vector3.left, 26f),           // the T-junction side, wider
                (+1f,  4f, Vector3.right, 14f),    (+1f, -5f, Vector3.right, 14f),           // the road to Le Port and Ranville
                (+1f, 20f, Vector3.forward, 3f),   (+1f, -20f, Vector3.back, 3f),
            };
            int k = 0;
            for (; idx < men.Count; idx++, k++)
            {
                var (side, z, watch, inland) = posts[k % posts.Count];
                float x = side * (Water.CanalShore(z, side) + inland + (k / posts.Count) * 3f);
                Vector3 spot = PostSpot(new Vector3(x, 0f, z + (k / posts.Count) * 2.5f * Mathf.Sign(z)), used);
                men[idx].Defend(spot, watch);
            }
            Say("Howard: \"Sappers, check the bridge for charges. Everyone else, dig in on both banks — they'll be back.\"", 6f);
        }

        /// <summary>The ground (or the bridge deck) under a point, not a roof.</summary>
        static Vector3 OnGround(Vector3 p)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 6f, p.z), Vector3.down, 14f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider is CharacterController || h.collider.attachedRigidbody && !h.collider.attachedRigidbody.isKinematic) continue;
                if (h.point.y > 2.5f || h.normal.y < 0.6f) continue;   // roofs, walls
                if (h.point.y > best) best = h.point.y;
            }
            return new Vector3(p.x, float.IsNegativeInfinity(best) ? 0f : best, p.z);
        }

        /// <summary>A post near the wanted point: on firm ground, out of the water, not on top of another man's.</summary>
        static Vector3 PostSpot(Vector3 want, List<Vector3> used)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = want + (i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(i * 2.4f), 0f, Mathf.Sin(i * 2.4f)) * (1.2f + i * 0.5f));
                p = OnGround(p);
                if (Water.SurfaceAt(p + Vector3.up * 0.05f, out _) || p.y < -2.5f) continue;
                bool clash = false;
                foreach (var u in used) if ((u - p).sqrMagnitude < 2.2f * 2.2f) { clash = true; break; }
                if (clash) continue;
                if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;   // inside a wall or a glider
                used.Add(p);
                return p;
            }
            var w = OnGround(want); used.Add(w); return w;
        }

        /// <summary>Testing (Stan's Game > Debug > Skip Ahead): move the mission on to its next step.</summary>
        public void DebugSkip()
        {
            if (!Net.Simulates) return;
            switch (CurrentPhase)
            {
                case 0: foreach (var h in eastBankDefenders) if (h && !h.IsDead) h.TakeDamage(new DamageInfo { amount = 9999f, point = h.transform.position }); break;
                case 1: Advance("(skipped) West bank taken: \"Ham\"."); StartCoroutine(JamReport()); break;
                case 2: SignalsSent = signalsNeeded; break;
                case 3: phaseStart -= 60f; break;                                   // a minute further into the hold
                case 4: if (panzer && !panzer.IsDead) panzer.TakeDamage(new DamageInfo { amount = 99999f, point = panzer.transform.position }); break;
                case 5: phaseStart -= holdOnSeconds; break;
            }
        }

        /// <summary>Host: one "Ham and Jam" transmission went out (from the player's wireless).</summary>
        public void ReportSignal()
        {
            if (!Net.Simulates || CurrentPhase != 2) return;
            SignalsSent++;
            if (SignalsSent < signalsNeeded)
                Say(SignalsSent == 1 ? "Only static. No answer. Send it again." : "Still nothing... keep sending.", 3.5f);
        }

        IEnumerator HoldTimer()
        {
            yield return new WaitForSeconds(holdSeconds * 0.5f);
            ParasLive = true;
            Say("7th Para Battalion coming in from the drop zone!", 4f);
            yield return new WaitForSeconds(holdSeconds * 0.5f);
            Minutes = Mathf.Max(Minutes, 2 * 60 - (startHour * 60 + startMinute)); // jump the clock to ~02:00
            Advance("Engine noise from the Bénouville junction... tanks!");
            TanksLive = true;
        }

        void Advance(string msg)
        {
            if (CurrentPhase + 1 == phases.Length - 1 && Net.Simulates)   // the last phase: the mission is won
                MissionProgress.Complete(gameObject.scene.name, MissionProgress.Elapsed);
            CurrentPhase = Mathf.Min(CurrentPhase + 1, phases.Length - 1);
            phaseStart = Time.time;
            Say(msg, 5f);
        }

        /// <summary>Host: show a message to everyone.</summary>
        public void Say(string msg, float seconds)
        {
            ShowLocal(msg, seconds);
            MessageSerial++;
        }

        /// <summary>Show a message on this machine only (clients get the host's messages through this).</summary>
        public void ShowLocal(string msg, float seconds)
        {
            Message = msg; MessageSeconds = seconds; messageUntil = Time.time + seconds;
        }

        // ------------------------------------------------------------------ groups that join later

        void ApplyGroups()
        {
            if (appliedTanks != TanksLive) { appliedTanks = TanksLive; SetGroupLive(tankAttackGroup, TanksLive); }
            if (appliedParas != ParasLive) { appliedParas = ParasLive; SetGroupLive(paraReinforcements, ParasLive); }
            if (appliedSnipers != SnipersLive) { appliedSnipers = SnipersLive; SetGroupLive(snipers, SnipersLive); }
            if (appliedWaves != WavesMask && attackWaves != null)
            {
                for (int w = 0; w < attackWaves.Length; w++)
                {
                    bool live = (WavesMask & (1 << w)) != 0;
                    if (appliedWaves < 0 || ((appliedWaves >> w) & 1) != (live ? 1 : 0)) SetGroupLive(attackWaves[w], live);
                }
                appliedWaves = WavesMask;
            }
        }

        readonly Dictionary<Collider, bool> colliderState = new Dictionary<Collider, bool>();

        /// <summary>
        /// Takes a group out of play (hidden, no collisions, not a target, AI off) or puts it back. The objects stay
        /// active so their network identities keep working.
        /// </summary>
        void SetGroupLive(GameObject group, bool live)
        {
            if (!group) return;
            foreach (var r in group.GetComponentsInChildren<Renderer>(true)) r.enabled = live;
            foreach (var l in group.GetComponentsInChildren<Light>(true)) if (!live) l.enabled = false;
            foreach (var c in group.GetComponentsInChildren<Collider>(true))
            {
                if (!live) { if (!colliderState.ContainsKey(c)) colliderState[c] = c.enabled; c.enabled = false; }
                else if (colliderState.TryGetValue(c, out bool was)) c.enabled = was;
            }
            foreach (var rb in group.GetComponentsInChildren<Rigidbody>(true))
                if (rb.GetComponent<TankController>()) rb.isKinematic = !live || !Net.Simulates;
            foreach (var h in group.GetComponentsInChildren<Health>(true)) h.enabled = live;   // not a target while out of play
            bool sim = live && Net.Simulates;   // clients never run AI or vehicle physics
            foreach (var ai in group.GetComponentsInChildren<SoldierAI>(true)) ai.enabled = sim;
            foreach (var ai in group.GetComponentsInChildren<TankAI>(true)) ai.enabled = sim;
            foreach (var tc in group.GetComponentsInChildren<TankController>(true)) tc.enabled = sim;
        }

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            if (Net.InMenu || CinematicDirector.Playing) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true };
            var phase = phases[Mathf.Clamp(CurrentPhase, 0, phases.Length - 1)];
            GUI.Label(new Rect(20, 16, 900, 30), $"<b>{ClockText}</b>  6 June 1944   —   {phase.title}", style);
            style.fontSize = 16;
            GUI.Label(new Rect(20, 44, 900, 26), phase.objective, style);
            if (!string.IsNullOrEmpty(Message))
            {
                var big = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, richText = true };
                GUI.Label(new Rect(0, Screen.height * 0.22f, Screen.width, 40), $"<b>{Message}</b>", big);
            }
            if (player && player.enabled)
            {
                var w = player.CurrentWeapon;
                var hs = player.GetComponent<Health>();
                var kit = player.GetComponent<Loadout>();
                string held = w ? $"{w.displayName}   {w.Ammo} / {w.SpareRounds}{(w.IsReloading ? "  reloading..." : "")}"
                            : kit && kit.grenades.Count > 0 ? $"{kit.grenades[0].displayName}   × {kit.grenades.Count}" : "";
                GUI.Label(new Rect(20, Screen.height - 10 - Hud.HealthHeight - 30, 600, 26), held, style);
                string pose = player.IsProne ? "prone" : player.IsCrouching ? "kneeling" : "standing";
                if (hs) Hud.HealthBar(hs, 20, Screen.height - 10, pose);
                // "Press E to ..." is drawn by VehicleEntry itself
            }
            else if (player)
            {
                var hs = player.GetComponent<Health>();
                if (hs && hs.IsDead)
                {
                    var big = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter, richText = true };
                    GUI.Label(new Rect(0, Screen.height * 0.5f, Screen.width, 40), "<b>You're hit. Back in the fight in a moment...</b>", big);
                }
            }
        }
    }
}
