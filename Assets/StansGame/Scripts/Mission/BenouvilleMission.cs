using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace StansGame
{
    /// <summary>
    /// Château de Bénouville, 6 June 1944 (single player). A Company, 7th Parachute Battalion, Major Nigel Taylor.
    /// History (sources in the project doc claude/chateau-de-benouville.md): 7th Para dropped on DZ N about 00:50, badly
    /// scattered; by 01:30 about half had assembled and the CO crossed the canal bridge about 01:40. A Company took the
    /// south and west of Bénouville and fought there, isolated, for about 17 hours: the aid post was overrun, Taylor was
    /// wounded and went on from a stretcher, a Mk IV tank and a self-propelled gun were knocked out with Gammon bombs.
    /// Lt Hoeller of Panzergrenadier-Regiment 192 went up on the château roof to look at the bridge until Wally Parr put
    /// shells from the captured gun through the top floor and Howard stopped him: there were women in labour inside.
    /// Lt MacDonald's platoon of C Company got through about 19:00; the 2nd Royal Warwicks relieved the battalion in the evening.
    /// Phases: 0 over the bridge, 1 the south of the village, 2 the château grounds, 3 (film) Hoeller and Parr,
    /// 4 hold, 5 armour, 6 surrounded, 7 relieved. The opening (the drop and the march) is a film only.
    /// </summary>
    public class BenouvilleMission : MonoBehaviour
    {
        [System.Serializable] public class Phase { public string title; [TextArea] public string objective; }

        public static BenouvilleMission Instance { get; private set; }

        public SoldierController player;
        public Volume filmLook;                 // the black-and-white "Longest Day" look of Pegasus Bridge's films
        public GameObject section;
        public Transform vion;
        public Transform bridge, southEdge, chateau, parkCentre, taylorHq, aidPost, parrGun, reliefTarget;
        public GameObject villageGermans, parkGermans, roofParty, aCompanyDugIn, macdonald, warwicks, armour;
        public SoldierAI taylor;
        public GameObject[] counterAttacks;
        public float[] holdDelays = { 5f, 55f, 105f, 150f };   // waves 1-4 during "hold", seconds into the phase
        public float[] eveningDelays = { 5f, 45f };            // waves 5-6 during "surrounded"

        [Header("Clock")]
        public int startHour = 1, startMinute = 40;
        public float timeScaleMinutesPerSecond = 0.5f;

        public Phase[] phases =
        {
            new Phase { title = "Over the bridge", objective = "Cross Pegasus Bridge past Howard's men and go south down the Caen road to the end of the village." },
            new Phase { title = "The south of the village", objective = "Clear the Germans out of the houses at the south end of Bénouville." },
            new Phase { title = "The château grounds", objective = "Clear the park of the château. It flies the Red Cross: mothers and babies inside - don't fire at the house." },
            new Phase { title = "The château roof", objective = "" },
            new Phase { title = "Hold Bénouville", objective = "Counter-attacks from the west and south-west. Hold the south of the village with A Company." },
            new Phase { title = "Armour on the Caen road!", objective = "A Panzer IV and a self-propelled gun. Get close and stop them with Gammon bombs." },
            new Phase { title = "Surrounded", objective = "No wireless, no runners getting through. Hold on until the relief comes up from the bridge." },
            new Phase { title = "Relieved", objective = "The 2nd Royal Warwicks take over Bénouville. A Company held for 17 hours." },
        };

        public int CurrentPhase { get; private set; }
        public float Minutes { get; set; }
        public string ClockText { get { int total = startHour * 60 + startMinute + (int)Minutes; return $"{(total / 60) % 24:00}:{total % 60:00}"; } }

        static int checkpoint;
        static Vector3 checkpointPos;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCheckpoint() { checkpoint = 0; checkpointPos = Vector3.zero; }

        string message; float messageUntil, messageStart, letterbox;
        float phaseStart, fade, deadSince = -1f;
        int wavesLaunched;
        bool inFilm, skipFilm, menuOpen, macdonaldIn, warwicksIn;
        readonly Dictionary<Collider, bool> colliderState = new Dictionary<Collider, bool>();

        void Awake()
        {
            Instance = this;
            MissionText.Show = Say;
            if (Net.Active)
            {
                // co-op: every player is a network paratrooper (NetPlayer); the single-player soldier stays out, and so do checkpoints
                if (player) Destroy(player.gameObject);
                player = null;
                checkpoint = 0;
            }
        }

        // ------------------------------------------------------------------ co-op
        // The host runs the mission; the clients follow its phase, waves, reinforcements, clock and messages
        // (BenouvilleMissionNet) and play the same films. The dead come back near the fight (NetSession.SpawnPlayer).

        public int WavesLaunched => wavesLaunched;
        public bool MacdonaldIn => macdonaldIn;
        public bool WarwicksIn => warwicksIn;
        public int MessageSerial { get; private set; }
        public string LastMessage => message;
        public float LastMessageSeconds { get; private set; }
        bool hoellerShown, reliefShown, armourShown, synced;

        /// <summary>The local player's network soldier has spawned.</summary>
        public void SetLocalPlayer(SoldierController sc)
        {
            player = sc; deadSince = -1f;
            if (!sc) return;
            if (inFilm) sc.SetControlActive(false);
            else sc.SetControlActive(true, sc.transform.position);
        }

        /// <summary>Where the host puts a (re)spawning player: by the bridge at first, then where the fight has got to.</summary>
        public Vector3 SpawnPoint(ulong clientId)
        {
            Vector3 b = checkpointPos != Vector3.zero ? checkpointPos : new Vector3(18f, 0f, -3f);
            b += new Vector3((clientId % 4) * 1.6f - 2.4f, 0f, (clientId / 4 % 4) * 1.6f);
            return Ground(b) + Vector3.up * 0.3f;
        }

        /// <summary>Client: bring this world to where the host's mission is.</summary>
        public void ApplyShared(int phase, int waves, bool mac, bool war)
        {
            if (Net.IsServer) return;
            if (!synced)
            {
                // joined late: set the world up as it stands, without the films already gone by
                synced = true;
                if (phase >= 1) SetGroupLive(villageGermans, false);
                if (phase >= 4) { hoellerShown = true; SetGroupLive(parkGermans, false); SetGroupLive(aCompanyDugIn, true); }
                if (phase >= 6) armourShown = true;
            }
            if (phase >= 3 && !hoellerShown && phase < 7) { hoellerShown = true; StartCoroutine(HoellerFilm()); }
            if (phase >= 5 && !armourShown) { armourShown = true; SetGroupLive(armour, true); }
            while (wavesLaunched < waves && counterAttacks != null && wavesLaunched < counterAttacks.Length) LaunchWave();
            if (mac && !macdonaldIn) { macdonaldIn = true; SetGroupLive(macdonald, true); }
            if (war && !warwicksIn) { warwicksIn = true; SetGroupLive(warwicks, true); }
            if (phase >= 7 && !reliefShown) { reliefShown = true; StartCoroutine(ReliefFilm()); }
            if (!inFilm) CurrentPhase = phase;
            if (phase >= 1 && checkpointPos == Vector3.zero) checkpointPos = southEdge ? southEdge.position : Vector3.zero;
        }

        /// <summary>Client: a message the host's mission showed.</summary>
        public void ShowShared(string msg, float seconds) { if (!string.IsNullOrEmpty(msg)) Say(msg, seconds); }

        static System.Collections.Generic.IEnumerable<SoldierController> Players()
        {
            foreach (var sc in FindObjectsByType<SoldierController>(FindObjectsSortMode.None))
            {
                var h = sc.GetComponent<Health>();
                if (h && h.IsDead) continue;
                yield return sc;
            }
        }

        void Start()
        {
            SoldierAI.GliderLanded();
            SoldierAI.RaiseAlarm();   // the whole district has been awake since the gliders came in
            SetGroupLive(roofParty, false);
            SetGroupLive(aCompanyDugIn, false);
            SetGroupLive(macdonald, false);
            SetGroupLive(warwicks, false);
            SetGroupLive(armour, false);
            if (counterAttacks != null) foreach (var g in counterAttacks) SetGroupLive(g, false);
            if (checkpoint > 0) { Resume(); return; }
            StartCoroutine(OpeningFilm());
        }

        void Resume()
        {
            CurrentPhase = checkpoint; phaseStart = Time.time;
            Minutes = checkpoint >= 6 ? 1040f : checkpoint >= 5 ? 640f : checkpoint >= 4 ? 450f : checkpoint >= 2 ? 50f : 30f;
            if (checkpoint >= 1) SetGroupLive(villageGermans, false);
            if (checkpoint >= 3) SetGroupLive(parkGermans, false);
            if (checkpoint >= 4) { SetGroupLive(aCompanyDugIn, true); DugIn(); }
            if (checkpoint >= 5) { SetGroupLive(armour, true); wavesLaunched = 4; }
            if (checkpoint >= 6) { SetGroupLive(armour, false); wavesLaunched = 4; }
            MoveSection(checkpointPos);
            if (player) player.SetControlActive(true, checkpointPos + Vector3.up * 0.3f);
            Say("Back in the fight.", 3f);
        }

        void Update()
        {
            var kb = Keyboard.current;
            fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime * 0.6f);
            letterbox = Mathf.MoveTowards(letterbox, inFilm ? 1f : 0f, Time.deltaTime * 2f);
            if (filmLook) filmLook.weight = Mathf.MoveTowards(filmLook.weight, inFilm ? 1f : 0f, Time.deltaTime * 1.5f);
            if (inFilm)
            {
                if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) skipFilm = true;
                return;
            }
            if (!Net.Active && kb != null && kb.escapeKey.wasPressedThisFrame)   // in co-op Esc is the session menu
            {
                menuOpen = !menuOpen;
                Cursor.lockState = menuOpen ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = menuOpen;
            }
            if (Time.time > messageUntil) message = null;
            Minutes += Time.deltaTime * timeScaleMinutesPerSecond;

            if (PlayerDead() && !Net.Active)   // in co-op NetSession brings him back; the host's mission runs on
            {
                if (deadSince < 0f) deadSince = Time.time;
                if (Time.time - deadSince > 6f) SceneManager.LoadScene(gameObject.scene.name);
                return;
            }
            if (Net.Active && !Net.IsServer) return;   // a client follows the host (BenouvilleMissionNet)
            float t = Time.time - phaseStart;
            switch (CurrentPhase)
            {
                case 0:
                    if (Near(southEdge.position, 30f))
                    {
                        Minutes = Mathf.Max(Minutes, 30f);   // 02:10
                        Next("02:10. The south end of Bénouville. Germans in the houses - clear them out!", southEdge.position);
                    }
                    break;
                case 1:
                    if (Alive(villageGermans) <= 1 && t > 5f)
                    {
                        Next("The village is clear to the south. Madame Vion's château flies the Red Cross - the park is full of Germans.", southEdge.position);
                    }
                    break;
                case 2:
                    if (Alive(parkGermans) <= 1 && t > 5f) { hoellerShown = true; StartCoroutine(HoellerFilm()); }
                    break;
                case 4:
                    while (wavesLaunched < 4 && wavesLaunched < holdDelays.Length && t >= holdDelays[wavesLaunched]) LaunchWave();
                    if (wavesLaunched == 1 && t > 20f && t < 21f) Say("Major Taylor is hit in the leg. He goes on running the company from a stretcher.", 5f);
                    if (wavesLaunched == 3 && t > 110f && t < 111f) Say("The Regimental Aid Post has been overrun. The padre, Captain Parry, is killed.", 5f);
                    if (wavesLaunched >= 4 && t > holdDelays[3] + 20f && AttackersLeft() <= 3)
                    {
                        Minutes = Mathf.Max(Minutes, 640f);   // about 12:20
                        SetGroupLive(armour, true);
                        Next("Engines on the Caen road! A Panzer IV and a self-propelled gun coming up from the south.", taylorHq.position);
                    }
                    break;
                case 5:
                    if (Alive(armour) == 0)
                    {
                        Minutes = Mathf.Max(Minutes, 1040f);   // 19:00
                        fade = 1.2f;
                        Next("Both knocked out with Gammon bombs. 13:30: the commandos pass through to the bridges. 19:00: still surrounded.", taylorHq.position);
                    }
                    break;
                case 6:
                    while (wavesLaunched < counterAttacks.Length && wavesLaunched - 4 < eveningDelays.Length && t >= eveningDelays[wavesLaunched - 4]) LaunchWave();
                    if (!macdonaldIn && t > 25f)
                    {
                        macdonaldIn = true; SetGroupLive(macdonald, true); Order(macdonald, reliefTarget);
                        Say("Lt MacDonald's platoon of C Company has fought its way through to you - seventeen men.", 5f);
                    }
                    if (!warwicksIn && wavesLaunched >= counterAttacks.Length && t > 90f && AttackersLeft() <= 3)
                    {
                        warwicksIn = true; Minutes = Mathf.Max(Minutes, 1175f);   // 21:15
                        SetGroupLive(warwicks, true); Order(warwicks, reliefTarget);
                        Say("21:15. The 2nd Royal Warwicks are coming down the road from the bridge!", 5f);
                    }
                    if (warwicksIn && t > 120f && warwicks && Nearest(warwicks, reliefTarget.position) < 40f) { reliefShown = true; StartCoroutine(ReliefFilm()); }
                    break;
            }
        }

        void Next(string msg, Vector3 at)
        {
            CurrentPhase = Mathf.Min(CurrentPhase + 1, phases.Length - 1);
            phaseStart = Time.time;
            checkpoint = Mathf.Max(checkpoint, CurrentPhase); checkpointPos = at;
            if (!string.IsNullOrEmpty(msg)) Say(msg, 6f);
        }

        void LaunchWave()
        {
            var g = counterAttacks[wavesLaunched];
            SetGroupLive(g, true);
            string from = g.name.Contains("(") ? g.name.Split('(')[1].TrimEnd(')') : "west";
            Say($"Counter-attack! Germans coming in - {from}.", 4f);
            wavesLaunched++;
        }

        // ------------------------------------------------------------------ films

        IEnumerator OpeningFilm()
        {
            inFilm = true; skipFilm = false;
            if (player) player.SetControlActive(false);
            var cam = Camera.main;
            var shots = new (Vector3 from, Vector3 to, Vector3 look, float secs, string text)[]
            {
                (new Vector3(900f, 160f, -400f), new Vector3(700f, 140f, -250f), new Vector3(300f, 0f, 0f), 6f,
                 "6 June 1944, 00:50. 7th Parachute Battalion jumps over DZ N, east of the Orne. The drop is badly scattered."),
                (new Vector3(520f, 40f, 120f), new Vector3(380f, 30f, 60f), new Vector3(200f, 0f, 0f), 6f,
                 "01:30. Half the battalion has found the rendezvous: no mortars, no machine guns, no wireless sets."),
                (new Vector3(160f, 18f, 30f), new Vector3(60f, 12f, 18f), new Vector3(0f, 2f, 0f), 6f,
                 "Lt-Col Pine-Coffin leads them at the double over the Orne bridge and on to the canal bridge."),
                (new Vector3(-60f, 14f, -40f), new Vector3(-120f, 22f, -160f), new Vector3(-170f, 8f, -800f), 6f,
                 "01:40. Over the canal past Major Howard's men. A Company, Major Nigel Taylor: the south of Bénouville."),
            };
            fade = 1f;
            foreach (var s in shots)
            {
                if (skipFilm) break;
                Say(s.text, s.secs);
                for (float t = 0f; t < s.secs && !skipFilm; t += Time.deltaTime)
                {
                    if (cam) { cam.transform.position = Vector3.Lerp(s.from, s.to, Mathf.SmoothStep(0f, 1f, t / s.secs)); cam.transform.LookAt(s.look); }
                    yield return null;
                }
            }
            EndFilm();
            Say("Cross the bridge and go south down the Caen road.", 5f);
        }

        IEnumerator HoellerFilm()
        {
            CurrentPhase = 3; phaseStart = Time.time;
            inFilm = true; skipFilm = false;
            if (player) player.SetControlActive(false);
            Minutes = Mathf.Max(Minutes, 420f);   // the morning
            fade = 1.2f;
            SetGroupLive(roofParty, true);
            foreach (var ai in roofParty.GetComponentsInChildren<SoldierAI>(true)) ai.enabled = false;   // they only watch
            var cam = Camera.main;
            Vector3 roof = roofParty.transform.childCount > 0 ? roofParty.transform.GetChild(0).position : chateau.position + Vector3.up * 20f;
            Vector3 gun = parrGun ? parrGun.position : new Vector3(25f, 1f, 10f);
            Vector3 v = vion ? vion.position : chateau.position;
            Vector3 vOut = vion ? vion.forward : Vector3.back;
            var shots = new (Vector3 from, Vector3 to, Vector3 look, float secs, string text, bool fire)[]
            {
                (v + vOut * 7f + Vector3.up * 2.2f + Vector3.Cross(Vector3.up, vOut) * 3f, v + vOut * 5f + Vector3.up * 1.8f, v + Vector3.up * 1.5f, 7f,
                 "Madame Léa Vion runs the château as a maternity home - and a Resistance group. Gondrée's reports on the bridge went to England through her.", false),
                (roof + new Vector3(-6f, 3f, -8f), roof + new Vector3(-3f, 2f, -5f), Vector3.up * 2f, 6f,
                 "Morning. Lt Hans Hoeller, Panzergrenadier-Regiment 192, cannot get his guns through Bénouville. He goes up on the château roof.", false),
                (roof + new Vector3(2f, 1.8f, 1f), roof + new Vector3(2f, 1.8f, 3f), Vector3.zero, 5f,
                 "Madame Vion could not keep them out. From the roof Hoeller looks straight down the canal at the bridge.", false),
                (gun + new Vector3(-4f, 2.2f, -3f), gun + new Vector3(-3f, 2f, -2f), roof, 6f,
                 "At the bridge Wally Parr is on the captured German anti-tank gun. He has seen a glint - a sniper, he is sure.", true),
                (roof + new Vector3(-40f, 10f, 60f), roof + new Vector3(-30f, 8f, 45f), roof, 6f,
                 "Shells through the top floor of the château. Hoeller and his men get off the roof - fast.", false),
                (gun + new Vector3(-5f, 2f, 2f), gun + new Vector3(-4f, 2f, 3f), gun, 5f,
                 "Major Howard stops him: \"There are women in labour in there!\"", false),
            };
            int shot = 0;
            foreach (var s in shots)
            {
                if (skipFilm) break;
                Say(s.text, s.secs);
                if (shot == 4) StartCoroutine(Hits(roof));
                for (float t = 0f; t < s.secs && !skipFilm; t += Time.deltaTime)
                {
                    if (cam)
                    {
                        Vector3 look = s.look == Vector3.zero ? new Vector3(0f, 2f, 0f) : s.look == Vector3.up * 2f ? roof + Vector3.up * 1.5f : s.look;
                        cam.transform.position = Vector3.Lerp(s.from, s.to, Mathf.SmoothStep(0f, 1f, t / s.secs));
                        cam.transform.LookAt(look);
                    }
                    if (s.fire && t > 3f && t - Time.deltaTime <= 3f) { Sfx.Shot("PaK", gun); Flash(gun + Vector3.up * 1.2f, 0.15f); }
                    yield return null;
                }
                shot++;
            }
            SetGroupLive(roofParty, false);
            if (roofParty) roofParty.SetActive(false);
            Minutes = Mathf.Max(Minutes, 450f);   // ~09:10
            SetGroupLive(aCompanyDugIn, true); DugIn();
            EndFilm();
            fade = 1.2f;
            CurrentPhase = 3;
            Next("A Company is dug in along the south of the village. The Germans will come back - from the west.", taylorHq.position);
        }

        IEnumerator ReliefFilm()
        {
            CurrentPhase = 7; phaseStart = Time.time;
            inFilm = true; skipFilm = false;
            if (player) player.SetControlActive(false);
            var cam = Camera.main;
            Vector3 r = reliefTarget.position;
            var shots = new (Vector3 from, Vector3 to, Vector3 look, float secs, string text)[]
            {
                (r + new Vector3(12f, 6f, 40f), r + new Vector3(8f, 4f, 20f), r + Vector3.up, 7f,
                 "21:15. The 2nd Battalion, Royal Warwickshire Regiment, come down from the bridge and take over Bénouville."),
                (chateau.position + new Vector3(-60f, 25f, 70f), chateau.position + new Vector3(-40f, 18f, 45f), chateau.position + Vector3.up * 10f, 7f,
                 "A Company had held the south of the village for about seventeen hours, cut off, against eight attacks."),
                (r + new Vector3(-20f, 30f, -30f), r + new Vector3(-30f, 45f, -50f), r, 8f,
                 "After midnight 7th Parachute Battalion marched back over the bridge. 3 officers and 16 men had been killed, 4 officers and 38 men wounded."),
            };
            fade = 1f;
            foreach (var s in shots)
            {
                if (skipFilm) break;
                Say(s.text, s.secs);
                for (float t = 0f; t < s.secs && !skipFilm; t += Time.deltaTime)
                {
                    if (cam) { cam.transform.position = Vector3.Lerp(s.from, s.to, Mathf.SmoothStep(0f, 1f, t / s.secs)); cam.transform.LookAt(s.look); }
                    yield return null;
                }
            }
            checkpoint = 0;
            MissionProgress.Complete(gameObject.scene.name, MissionProgress.Elapsed);
            EndFilm();
            Say("Mission complete. Bénouville held until relieved.", 8f);
        }

        IEnumerator Hits(Vector3 roof)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = roof + new Vector3(Random.Range(-6f, 6f), -2.5f, Random.Range(-3f, 3f));
                Sfx.Explosion(p, false); Flash(p, 0.25f);
                yield return new WaitForSeconds(1.4f);
            }
        }

        static void Flash(Vector3 p, float secs)
        {
            var l = new GameObject("Flash").AddComponent<Light>();
            l.transform.position = p; l.type = LightType.Point; l.range = 25f; l.intensity = 8f; l.color = new Color(1f, 0.7f, 0.4f);
            Destroy(l.gameObject, secs);
        }

        void EndFilm()
        {
            inFilm = false;
            var at = CurrentPhase == 0 && bridge ? new Vector3(18f, 0.3f, -3f) : checkpointPos != Vector3.zero ? checkpointPos : player ? player.transform.position : Vector3.zero;
            if (player) { player.SetControlActive(true, Ground(at) + Vector3.up * 0.3f); if (player.cam == null) player.cam = Camera.main; }
            MoveSection(at);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        void MoveSection(Vector3 at)
        {
            if (!section || at == Vector3.zero) return;
            int i = 0;
            foreach (var ai in section.GetComponentsInChildren<SoldierAI>())
            {
                var p = Ground(at + new Vector3((i % 3) * 2f - 2f, 0f, -2f - (i / 3) * 2f));
                var cc = ai.GetComponent<CharacterController>();
                if (cc) cc.enabled = false; ai.transform.position = p + Vector3.up * 0.2f; if (cc) cc.enabled = true;
                i++;
            }
        }

        static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, 200f, p.z), Vector3.down, out var hit, 400f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            return p;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Editor testing (Stan's Game > Debug > Skip Ahead): finish the current phase.</summary>
        public void DebugSkip()
        {
            if (inFilm) { skipFilm = true; return; }
            void Kill(GameObject g) { if (g) foreach (var h in g.GetComponentsInChildren<Health>()) if (!h.IsDead) h.TakeDamage(new DamageInfo { amount = 99999f, point = h.transform.position, direction = Vector3.down }); }
            switch (CurrentPhase)
            {
                case 0: if (player) player.SetControlActive(true, Ground(southEdge.position) + Vector3.up * 0.3f); MoveSection(southEdge.position); break;
                case 1: Kill(villageGermans); break;
                case 2: Kill(parkGermans); break;
                case 4: while (wavesLaunched < 4) LaunchWave(); for (int w = 0; w < 4; w++) Kill(counterAttacks[w]); phaseStart = Time.time - holdDelays[3] - 30f; break;
                case 5: Kill(armour); break;
                case 6: while (wavesLaunched < counterAttacks.Length) LaunchWave(); foreach (var g in counterAttacks) Kill(g); phaseStart = Time.time - 130f;
                        if (warwicksIn && player) player.SetControlActive(true, Ground(reliefTarget.position) + Vector3.up * 0.3f); break;
            }
        }

        void DugIn()
        {
            if (!aCompanyDugIn) return;
            foreach (var ai in aCompanyDugIn.GetComponentsInChildren<SoldierAI>(true))
            {
                if (ai == taylor) continue;
                Vector3 watch = ai.transform.forward;
                ai.Defend(ai.transform.position, ai.transform.position + watch * 30f, 3f);
            }
        }

        void Order(GameObject g, Transform target)
        {
            if (!g || !target) return;
            foreach (var ai in g.GetComponentsInChildren<SoldierAI>(true)) { ai.mode = SoldierAI.Mode.Advance; ai.advanceTarget = target; ai.startAlert = true; }
        }

        int Alive(GameObject g)
        {
            if (!g) return 0;
            int n = 0; foreach (var h in g.GetComponentsInChildren<Health>(true)) if (h && !h.IsDead) n++;
            return n;
        }

        int AttackersLeft()
        {
            int n = 0;
            for (int w = 0; counterAttacks != null && w < wavesLaunched && w < counterAttacks.Length; w++) n += Alive(counterAttacks[w]);
            return n;
        }

        float Nearest(GameObject g, Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var h in g.GetComponentsInChildren<Health>()) if (!h.IsDead) best = Mathf.Min(best, Vector3.Distance(h.transform.position, p));
            return best;
        }

        public void Say(string msg, float seconds) { message = msg; messageUntil = Time.time + seconds; messageStart = Time.time; MessageSerial++; LastMessageSeconds = seconds; }

        bool PlayerDead() { var h = player ? player.GetComponent<Health>() : null; return h && h.IsDead; }

        bool Near(Vector3 pos, float r)
        {
            if (Net.Active)
            {
                // co-op: any of the players
                foreach (var sc in Players()) { Vector3 e = sc.transform.position - pos; e.y = 0f; if (e.magnitude < r) return true; }
                return false;
            }
            if (!player || !player.enabled) return false;
            Vector3 d = player.transform.position - pos; d.y = 0f;
            return d.magnitude < r;
        }

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
            foreach (var ai in group.GetComponentsInChildren<SoldierAI>(true)) ai.enabled = live;
            foreach (var tk in group.GetComponentsInChildren<TankAI>(true)) tk.enabled = live;
            foreach (var tc in group.GetComponentsInChildren<TankController>(true)) tc.enabled = live;
            foreach (var tc in group.GetComponentsInChildren<TankController>(true)) { var rb = tc.GetComponent<Rigidbody>(); if (rb) rb.isKinematic = !live; }
        }

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            if (inFilm || letterbox > 0.001f)
            {
                float alpha = inFilm && Time.time < messageUntil ? Mathf.Clamp01((Time.time - messageStart) / 0.6f) * Mathf.Clamp01((messageUntil - Time.time) / 0.6f) : 0f;
                MissionHud.Film(letterbox, ClockText + "  ·  6 June 1944", inFilm ? message : null, alpha);
                if (inFilm) { MissionHud.Fade(fade); return; }
            }
            var phase = phases[Mathf.Clamp(CurrentPhase, 0, phases.Length - 1)];
            MissionHud.TopLine(ClockText, phase.title, phase.objective);
            MissionHud.Message(message);
            DrawMarker();
            if (player && player.enabled) MissionHud.Player(player);
            else if (PlayerDead()) MissionHud.Dead("You're hit. Back to the last checkpoint...");
            menuOpen = MissionHud.Menu(menuOpen);
            MissionHud.Fade(fade);
        }

        void DrawMarker()
        {
            Transform target = CurrentPhase == 0 ? southEdge : CurrentPhase == 2 ? parkCentre : CurrentPhase == 5 ? taylorHq : null;
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
