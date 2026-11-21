using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StansGame
{
    /// <summary>
    /// "The Longest Day" treatment: black-and-white film look, letterbox, time stamps and captions that fade in and out,
    /// told from the historical accounts (Howard's D Company, Wallwork's landing, Brotheridge, "Ham and Jam",
    /// the Gondrées, Thornton's PIAT, Lovat's commandos and Piper Bill Millin).
    /// Scenes are triggered by the mission state, so in multiplayer every player sees them on their own screen.
    /// Space or Enter skips a scene. F toggles the black-and-white film look during play.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class CinematicDirector : MonoBehaviour
    {
        public static bool Playing { get; private set; }
        /// <summary>True while the opening title cards are up: the gliders wait so the landing happens on camera.</summary>
        public static bool HoldGliders { get; private set; }
        public static CinematicDirector Instance { get; private set; }

        public MissionDirector mission;
        public Volume filmLook;                    // global volume: desaturate, grain, vignette
        public Transform bridgeCentre, cafe, junction, benouvilleRoad;
        public Transform lovatStart, lovatEnd;     // commandos march in along the Bénouville road and over the bridge
        [Tooltip("Lovat's commandos: the British officer model (it wears a beret, not a helmet); its beret is dyed commando green (Resources/Textures/British_Commando_BaseColor).")]
        public GameObject commandoPrefab;
        [Header("Intro: the tow over the coast")]
        [Tooltip("A Halifax towing Horsa 91, high above the map (switched off): shown as the glider casts off.")]
        public GameObject towGroup;
        public Transform towHalifax, towHorsa, towHitch, bridlePort, bridleStarboard;
        public LineRenderer towRope, towBridle;
        [Tooltip("Keep the film look on during play as well (toggle with F).")]
        public bool filmLookInPlay = false;

        Camera cam;
        string caption, stamp, prevCaption, prevStamp;
        float captionStart, captionEnd = -1f, prevGone, letterbox, fade;        // fade: 0 = picture, 1 = black
        bool skip, introDone, hamDone, tanksDone, thorntonDone, finaleDone;
        readonly List<GameObject> props = new List<GameObject>();

        void Awake() => Instance = this;

        void Start()
        {
            cam = Camera.main;
            if (cam)
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data) data.renderPostProcessing = true;
            }
            if (filmLook) filmLook.weight = filmLookInPlay ? 1f : 0f;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (Playing && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) skip = true;
                if (!Playing && kb.fKey.wasPressedThisFrame) filmLookInPlay = !filmLookInPlay;
            }
            if (filmLook)
                filmLook.weight = Mathf.MoveTowards(filmLook.weight, Playing || filmLookInPlay ? 1f : 0f, Time.unscaledDeltaTime * 1.5f);
            letterbox = Mathf.MoveTowards(letterbox, Playing ? 1f : 0f, Time.unscaledDeltaTime * 2f);
            StepScene();

            if (Net.InMenu || !mission || Playing) return;

            // scene triggers, from the (host-synced) mission state
            if (!introDone)
            {
                introDone = true;
                if ((mission.LandedMask & 1) == 0) { Begin("Intro", Intro()); return; }   // late joiners skip the intro
                hamDone = mission.CurrentPhase >= 2; tanksDone = mission.TanksLive;
                thorntonDone = mission.panzer && mission.panzer.IsDead; finaleDone = mission.CurrentPhase >= mission.phases.Length - 1;
            }
            if (!hamDone && mission.CurrentPhase >= 3) { hamDone = true; Begin("HamAndJam", HamAndJam()); return; }
            if (!tanksDone && mission.TanksLive) { tanksDone = true; Begin("TanksComing", TanksComing()); return; }
            if (!thorntonDone && mission.panzer && mission.panzer.IsDead) { thorntonDone = true; Begin("Thornton", Thornton()); return; }
            if (!finaleDone && mission.CurrentPhase >= mission.phases.Length - 1) { finaleDone = true; Begin("Finale", Finale()); }
        }

        // ------------------------------------------------------------------ plumbing

        AudioSource score;

        void PlayScore(AudioClip clip, float volume, bool loop)
        {
            if (!clip) return;
            if (!score) { score = gameObject.AddComponent<AudioSource>(); score.spatialBlend = 0f; score.playOnAwake = false; score.ignoreListenerPause = true; }
            score.clip = clip; score.volume = volume; score.loop = loop; score.Play();
        }

        IEnumerator FadeScore(float seconds)
        {
            if (!score || !score.isPlaying) yield break;
            float v0 = score.volume;
            for (float t = 0f; t < seconds && score; t += Time.unscaledDeltaTime) { score.volume = v0 * (1f - t / seconds); yield return null; }
            if (score) score.Stop();
        }

        // A scene is stepped by hand from Update rather than run as a coroutine: a coroutine dies silently if its
        // object is switched off or an exception is thrown, which used to leave the letterbox up and the player
        // frozen. Here an error, a skip or a scene that runs too long always ends the scene cleanly.
        readonly Stack<IEnumerator> sceneStack = new Stack<IEnumerator>();
        string sceneName;
        float sceneStarted;
        SoldierController scenePlayer;
        bool sceneHadControl;
        const float MaxSceneSeconds = 150f;

        void Begin(string name, IEnumerator scene)
        {
            Playing = true; skip = false;
            sceneName = name; sceneStarted = Time.unscaledTime;
            sceneStack.Clear(); sceneStack.Push(scene);
            scenePlayer = mission.player;
            sceneHadControl = scenePlayer && scenePlayer.enabled;
            if (sceneHadControl) scenePlayer.SetControlActive(false);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            Debug.Log("Stan's Game: film scene '" + name + "' started");
        }

        void StepScene()
        {
            if (!Playing || sceneStack.Count == 0) return;
            if (skip || Time.unscaledTime - sceneStarted > MaxSceneSeconds) { EndScene(skip ? "skipped" : "timed out"); return; }
            try
            {
                while (sceneStack.Count > 0)
                {
                    var top = sceneStack.Peek();
                    if (!top.MoveNext()) { sceneStack.Pop(); continue; }       // finished: carry on with the caller
                    if (top.Current is IEnumerator nested) { sceneStack.Push(nested); continue; }
                    return;                                                       // yield return null: next frame
                }
                EndScene("finished");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EndScene("error");
            }
        }

        void EndScene(string why)
        {
            if (!Playing) return;
            sceneStack.Clear();
            ClearCaption();
            foreach (var p in props) if (p) Destroy(p);
            props.Clear();
            EndTow();
            Time.timeScale = 1f;
            fade = 0f;
            if (score && score.isPlaying && isActiveAndEnabled) StartCoroutine(FadeScore(2f)); else if (score) score.Stop();
            Playing = false;
            HoldGliders = false;
            skip = false;
            if (sceneHadControl && scenePlayer) scenePlayer.SetControlActive(true);
            Debug.Log("Stan's Game: film scene '" + sceneName + "' " + why);
        }

        void OnDisable() { EndScene("interrupted"); }

        const float CaptionFade = 0.7f;

        /// <summary>Shows a caption: the old one fades out while the new one fades in (no typewriter).
        /// With a duration, the caption also fades out by itself at the end.</summary>
        void Say(string time, string text, float duration = -1f)
        {
            ClearCaption();
            stamp = time; caption = text; captionStart = Time.unscaledTime;
            captionEnd = duration > 0f ? captionStart + duration : -1f;
        }

        void ClearCaption()
        {
            if (!string.IsNullOrEmpty(caption) && CaptionAlpha() > 0.01f) { prevCaption = caption; prevStamp = stamp; prevGone = Time.unscaledTime + CaptionFade * CaptionAlpha(); }
            caption = stamp = null; captionEnd = -1f;
        }

        float CaptionAlpha()
        {
            float now = Time.unscaledTime;
            float a = Mathf.Clamp01((now - captionStart - 0.25f) / CaptionFade);   // a short pause, then fade in
            if (captionEnd > 0f) a = Mathf.Min(a, Mathf.Clamp01((captionEnd - now) / CaptionFade));
            return Mathf.SmoothStep(0f, 1f, a);
        }

        /// <summary>Hold a camera move for a number of seconds (or until skipped).</summary>
        IEnumerator Shot(float seconds, System.Func<float, (Vector3 pos, Vector3 look)> path)
        {
            float t = 0f;
            while (t < seconds && !skip)
            {
                t += Time.unscaledDeltaTime;
                if (cam)
                {
                    var (pos, look) = path(Mathf.Clamp01(t / seconds));
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                    cam.fieldOfView = 50f;
                }
                yield return null;
            }
        }

        IEnumerator Black(float seconds, string time, string text)
        {
            fade = 1f; Say(time, text, seconds);
            float t = 0f;
            while (t < seconds && !skip) { t += Time.unscaledDeltaTime; yield return null; }
        }

        IEnumerator FadeIn(float seconds)
        {
            while (fade > 0f && !skip) { fade -= Time.unscaledDeltaTime / seconds; yield return null; }
            fade = 0f;
        }

        static Vector3 Ground(Vector3 p, float up)
        {
            var t = Terrain.activeTerrain;
            float y = t ? t.SampleHeight(p) + t.transform.position.y : 0f;
            return new Vector3(p.x, Mathf.Max(y, -1.5f) + up, p.z);
        }

        GliderApproach Glider(int i) => mission.gliders != null && i < mission.gliders.Length ? mission.gliders[i] : null;

        // ------------------------------------------------------------------ the scenes

        /// <summary>The history film's flight: the stream over the Channel and Horsa 91 casting off, with its own captions.</summary>
        public void PlayHistoryFlyby(string t1, string c1, string t2, string c2, string t3, string c3)
        {
            if (!towGroup || Playing) return;
            Begin("history flyby", HistoryFlyby(t1, c1, t2, c2, t3, c3));
        }

        IEnumerator HistoryFlyby(string t1, string c1, string t2, string c2, string t3, string c3)
        {
            PlayScore(Sfx.HalifaxDrone, 0.35f, true);   // quieter: the narrator speaks over it
            yield return Flyby(7f, t1, c1, 0);
            yield return Flyby(7f, t2, c2, 1);
            yield return TowAndCastOff(t3, c3);
        }

        IEnumerator Intro()
        {
            HoldGliders = true;
            PlayScore(Sfx.HalifaxDrone, 0.7f, true);   // the Halifax tugs, heard from inside the glider
            const string t1 = "5 JUNE 1944 — 22:56", c1 = "RAF Tarrant Rushton, Dorset. Six Halifax bombers take off, each towing a Horsa glider. Aboard: D Company, " +
                "2nd Oxfordshire & Buckinghamshire Light Infantry, with Royal Engineers — 181 men under Major John Howard.";
            const string t2 = "OPERATION DEADSTICK", c2 = "Their task: seize the bridges over the Caen Canal and the River Orne intact, before the Germans can blow them, " +
                "and hold them until relieved. Hours later the seaborne invasion will land a few miles to the north.";
            const string t3 = "6 JUNE 1944 — 00:07", c3 = "The gliders cast off over the coast at 6,000 feet. The pilots fly by stopwatch, compass and moonlight.";
            if (towGroup)
            {
                // the stream over the Channel under the opening captions, then Horsa 91 casting off
                yield return Flyby(6f, t1, c1, 0);
                yield return Flyby(6f, t2, c2, 1);
                yield return TowAndCastOff(t3, c3);   // sets the gliders going and fades the engines at the release
            }
            else
            {
                yield return Black(5f, t1, c1); yield return Black(5f, t2, c2); yield return Black(4f, t3, c3);
                fade = 0f; StartCoroutine(FadeScore(4f));
            }
            fade = 0f;
            HoldGliders = false;
            var g = Glider(0);
            // 1: wide shot from the canal bank: the sentry on the bridge hears nothing but the wind
            Say("00:15", "Bénouville. The sentries of the 716th Infantry Division on the bridge hear nothing but the wind.");
            yield return Shot(6f, k => (Ground(new Vector3(-30f, 0f, -45f), 1.6f) + Vector3.up * k, (bridgeCentre ? bridgeCentre.position : Vector3.zero) + Vector3.up * (6f + 20f * k)));
            // 2: beside the glider in its silent dive
            Say("00:15", "Glider No. 91 — Staff Sergeant Jim Wallwork and Staff Sergeant John Ainsworth at the controls. " +
                "Lieutenant Den Brotheridge's platoon and Major Howard sit behind them, arms linked, singing to keep calm.");
            yield return Shot(7f, k =>
            {
                if (!g) return (Vector3.up * 50f, Vector3.zero);
                Quaternion flat = Quaternion.Euler(0f, g.transform.eulerAngles.y, 0f);
                Vector3 p = g.transform.position + flat * new Vector3(Mathf.Lerp(14f, 6f, k), 2f, Mathf.Lerp(-6f, 10f, k));
                return (p, g.transform.position);
            });
            // 3: chase until it lands
            Say("00:16", "Arrester parachute out. They come in at about 90 miles an hour across the field...");
            float wait = 0f;
            while (g && (mission.LandedMask & 1) == 0 && !skip && wait < 40f)
            {
                wait += Time.unscaledDeltaTime;
                Quaternion flat = Quaternion.Euler(0f, g.transform.eulerAngles.y, 0f);
                Vector3 eye = g.transform.position + flat * new Vector3(2.5f, 5f, -26f);
                Vector3 look = g.transform.position + flat * Vector3.forward * 40f;
                if (cam) cam.transform.SetPositionAndRotation(Vector3.Lerp(cam.transform.position, eye, Time.unscaledDeltaTime * 4f),
                    Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(look - eye), Time.unscaledDeltaTime * 4f));
                yield return null;
            }
            // 4: the landing
            Say("00:16", "The Horsa smashes into the wire within yards of the bridge. Air Chief Marshal Leigh-Mallory would later praise it " +
                "as one of the finest feats of flying of the war. Both pilots are thrown out through the cockpit, stunned.");
            Vector3 gp = g ? g.transform.position : Vector3.zero;
            Quaternion gr = g ? Quaternion.Euler(0f, g.transform.eulerAngles.y, 0f) : Quaternion.identity;
            Vector3 eye1 = Ground(gp + gr * new Vector3(-13f, 0f, 20f), 3.2f);
            yield return Shot(5f, k => (eye1 + Vector3.up * k * 0.5f, gp + Vector3.up * 1.8f));
            Say("00:16", "No orders are needed. D Company pours out and runs for the bridge. Go!");
            Vector3 eye2 = Ground(gp + gr * new Vector3(9f, 0f, 14f), 2.6f);
            yield return Shot(2.5f, k => (eye2, gp + gr * Vector3.forward * 30f + Vector3.up * 2f));
        }

        // ------------------------------------------------------------------ the tow and the cast-off

        bool towFogSaved; float towFog;

        /// <summary>One Halifax and its Horsa on the rope. Pair 0 is the one built into the level (Horsa 91); the
        /// other five are copies made for the flyby.</summary>
        class TowPair
        {
            public Transform tug, horsa, hitch, port, stbd;
            public LineRenderer rope, bridle;
            public Vector3 offset;   // in the formation's frame, from pair 0
            public float phase;
        }
        readonly List<TowPair> pairs = new List<TowPair>();
        GameObject sea;
        Vector3 tugRel;   // where the Halifax flies relative to its glider (as built: ahead and a little below)
        const float TowSpeed = 60f;   // ~135 mph on tow

        void TowLook()
        {
            if (!towFogSaved) { towFogSaved = true; towFog = RenderSettings.fogDensity; }
            RenderSettings.fogDensity = Mathf.Min(towFog, 0.0006f);   // the low mist over the fields is far below: clear air up here
        }

        /// <summary>The six combinations in a loose stream (they took off a minute apart), a moonlit Channel below.</summary>
        void SetUpFormation()
        {
            if (pairs.Count > 0 || !towGroup) return;
            towGroup.SetActive(true);
            tugRel = towHalifax.localPosition - towHorsa.localPosition;
            pairs.Add(new TowPair { tug = towHalifax, horsa = towHorsa, hitch = towHitch, port = bridlePort, stbd = bridleStarboard, rope = towRope, bridle = towBridle });
            var offsets = new[] { new Vector3(-75f, 10f, -115f), new Vector3(80f, -7f, -105f), new Vector3(-145f, 16f, -225f),
                                  new Vector3(155f, 4f, -215f), new Vector3(10f, 22f, -330f) };
            for (int i = 0; i < offsets.Length; i++)
            {
                var tug = Instantiate(towHalifax.gameObject, towGroup.transform).transform;
                var horsa = Instantiate(towHorsa.gameObject, towGroup.transform).transform;
                var rope = Instantiate(towRope.gameObject, towGroup.transform).GetComponent<LineRenderer>();
                var bridle = Instantiate(towBridle.gameObject, towGroup.transform).GetComponent<LineRenderer>();
                pairs.Add(new TowPair { tug = tug, horsa = horsa, rope = rope, bridle = bridle, offset = offsets[i], phase = i * 1.7f + 0.6f,
                    hitch = tug.Find(towHitch.name), port = horsa.Find(bridlePort.name), stbd = horsa.Find(bridleStarboard.name) });
            }
            // the Channel: a big dark, glossy sheet north of the map, the moon glinting on it
            sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sea.name = "The Channel (film)";
            Destroy(sea.GetComponent<Collider>());
            sea.transform.SetParent(towGroup.transform, true);
            sea.transform.SetPositionAndRotation(new Vector3(0f, -2f, 12000f), Quaternion.identity);
            sea.transform.localScale = new Vector3(3000f, 1f, 2000f);   // 30 x 20 km
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh)
            {
                var m = new Material(sh);
                m.SetColor("_BaseColor", new Color(0.02f, 0.03f, 0.045f)); m.SetFloat("_Smoothness", 0.93f); m.SetFloat("_Metallic", 0f);
                sea.GetComponent<Renderer>().sharedMaterial = m;
            }
            sea.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Puts pair i on tow at the formation's point 'lead' (where pair 0's glider is), gently rising and falling.</summary>
        void FlyPair(TowPair p, Vector3 lead, Quaternion heading, float t)
        {
            float bob = Mathf.Sin(t * 1.3f + p.phase) * 0.6f + Mathf.Sin(t * 0.47f + p.phase * 0.5f) * 0.9f;
            Vector3 g = lead + heading * p.offset + Vector3.up * bob;
            p.horsa.SetPositionAndRotation(g, heading * Quaternion.Euler(1.5f + Mathf.Sin(t * 0.9f + p.phase) * 0.6f, 0f, Mathf.Sin(t * 0.7f + p.phase) * 2f));
            p.tug.SetPositionAndRotation(g + heading * tugRel - Vector3.up * bob * 0.4f, heading);
            if (p.hitch && p.port && p.stbd)
            {
                Vector3 join = Join(p);
                Rope(p.rope, p.hitch.position, join, 3.5f);
                p.bridle.SetPosition(0, p.port.position); p.bridle.SetPosition(1, join); p.bridle.SetPosition(2, p.stbd.position);
            }
        }

        static Vector3 Join(TowPair p)
        {
            Vector3 mid = (p.port.position + p.stbd.position) * 0.5f;
            return mid + p.horsa.forward * 13f - p.horsa.up * 0.6f;
        }

        static void Rope(LineRenderer lr, Vector3 a, Vector3 b, float sag)
        {
            if (!lr) return;
            int n = lr.positionCount;
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1);
                lr.SetPosition(i, Vector3.Lerp(a, b, k) + Vector3.down * sag * 4f * k * (1f - k));
            }
        }

        /// <summary>
        /// Over the Channel: the six Halifax-Horsa combinations in their stream, the moon on the sea. Two shots under
        /// the opening captions (instead of a black screen): alongside the stream as it overtakes the camera, then from
        /// below as it passes overhead.
        /// </summary>
        IEnumerator Flyby(float seconds, string time, string text, int shot)
        {
            SetUpFormation(); TowLook();
            Say(time, text, seconds);
            Quaternion heading = towGroup.transform.rotation;
            Vector3 fwd = heading * Vector3.forward, right = heading * Vector3.right;
            // moonlight on the undersides: we look up at them against the clouds
            var fill = towGroup.GetComponentInChildren<Light>(true);
            if (fill) { fill.transform.rotation = Quaternion.LookRotation(heading * new Vector3(0.4f, 0.75f, 0.3f)); fill.intensity = 1.4f; }
            // well out over the sea, north of the map
            Vector3 lead0 = new Vector3(towGroup.transform.position.x, towGroup.transform.position.y - 60f, 9000f) - fwd * TowSpeed * (shot == 0 ? 0f : seconds);
            float t = 0f;
            while (t < seconds && !skip)
            {
                float dt = Time.unscaledDeltaTime; t += dt;
                fade = shot == 0 ? Mathf.Max(0f, 1f - t / 1.5f) : 0f;
                float clock = t + (shot == 0 ? 0f : seconds);
                Vector3 lead = lead0 + fwd * TowSpeed * clock;
                foreach (var p in pairs) FlyPair(p, lead, heading, clock);
                Vector3 centre = lead + heading * new Vector3(5f, 8f, -150f);
                if (cam)
                {
                    Vector3 eye, look;
                    if (shot == 0)
                    {   // alongside, a little above: the stream slowly overtakes the camera, the sea below
                        eye = lead0 + fwd * TowSpeed * (seconds * 0.1f + clock * 0.8f) + right * 300f - Vector3.up * 70f + fwd * -60f;
                        look = centre + Vector3.up * 10f;
                    }
                    else
                    {   // from below and ahead: the combinations come over the camera
                        eye = lead0 + fwd * TowSpeed * (seconds * 1.45f) + right * 50f - Vector3.up * 90f;
                        look = Vector3.Lerp(lead + fwd * 40f, centre, 0.35f);
                    }
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
                    cam.fieldOfView = shot == 0 ? 40f : 58f;
                }
                yield return null;
            }
        }

        /// <summary>
        /// Over the French coast at 6,000 feet: Halifax and Horsa 91 on the tow rope (the glider in the high-tow position,
        /// on a Y-bridle from both wing roots, as in the photographs), seen from below and behind. Wallwork pulls the
        /// release; a moment later the Halifax drops its end of the rope as well, and the rope and bridle tumble away
        /// into the dark. The Halifax climbs and banks away to port for its diversion raid on Caen, its engines
        /// fading; the glider noses down on its own. The camera pulls back to keep both in the picture.
        /// </summary>
        IEnumerator TowAndCastOff(string time, string text)
        {
            SetUpFormation(); TowLook();
            Transform root = towGroup.transform;
            Vector3 start = root.position;
            Quaternion heading = root.rotation;
            Vector3 fwd = heading * Vector3.forward, up = Vector3.up;
            var p0 = pairs[0];
            // the other five were a minute apart on the way (kilometres back): only Horsa 91 and its tug in this shot
            for (int i = 1; i < pairs.Count; i++) foreach (var tr in new Component[] { pairs[i].tug, pairs[i].horsa, pairs[i].rope, pairs[i].bridle }) if (tr) tr.gameObject.SetActive(false);
            Vector3 tugOffset = tugRel;
            const float releaseAt = 6f, tugShot = 3f, tugDrop = 4.2f, total = 13.5f;
            var fill = towGroup.GetComponentInChildren<Light>(true);
            if (fill) { fill.transform.rotation = Quaternion.LookRotation(heading * new Vector3(0.35f, 0.5f, 1f)); fill.intensity = 1.5f; }

            Vector3 gliderPos = start, tugPos = start + heading * tugOffset;
            Vector3 endA = Vector3.zero, velA = Vector3.zero, endB = Vector3.zero, velB = Vector3.zero;   // A: the tug's end, B: the bridle end
            float gliderSpeed = TowSpeed, sink = 0f, tugYaw = 0f, tugClimb = 0f, gliderPitch = 0f, ropeLength = 60f;
            bool released = false, dropped = false;
            bool haveEye = false, eyeOnTug = false;
            float t = 0f;
            Say(time, text, releaseAt - 0.3f);
            while (t < total && !skip)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                fade = 0f;
                float bob = Mathf.Sin(t * 1.3f) * 0.6f + Mathf.Sin(t * 0.47f) * 0.9f;

                if (!released && t >= releaseAt)
                {
                    released = true;
                    endB = Join(p0); velB = fwd * TowSpeed; endA = towHitch.position; velA = fwd * TowSpeed;
                    ropeLength = Vector3.Distance(endA, endB);
                    Say("00:07", "Cast off. Wallwork pulls the release — then the Halifax lets go of the rope too, and it falls away into the dark. " +
                        "The tugs fly on to bomb Caen as a diversion. Six gliders turn for Normandy in silence.");
                    StartCoroutine(FadeScore(6f));
                }

                // the tug: on course; after the release it climbs and banks away to port
                if (released) { tugYaw = Mathf.MoveTowards(tugYaw, -32f, 6f * dt); tugClimb = Mathf.MoveTowards(tugClimb, 4f, 2f * dt); }
                Quaternion tugRot = heading * Quaternion.Euler(-tugClimb * 1.5f, tugYaw, -Mathf.Clamp(tugYaw * 0.9f, -28f, 28f));
                tugPos = released ? tugPos + tugRot * Vector3.forward * TowSpeed * dt + up * tugClimb * dt
                                  : start + fwd * TowSpeed * t + heading * tugOffset + up * bob * 0.6f;
                towHalifax.SetPositionAndRotation(tugPos, tugRot);

                // the glider: riding the tow, then on its own - slower, nose down, sinking
                if (released)
                {
                    gliderSpeed = Mathf.MoveTowards(gliderSpeed, 44f, 3f * dt);
                    sink = Mathf.MoveTowards(sink, 7f, 1.6f * dt);
                    gliderPitch = Mathf.MoveTowards(gliderPitch, 7f, 2.5f * dt);
                    gliderPos += fwd * gliderSpeed * dt - up * sink * dt;
                }
                else gliderPos = start + fwd * TowSpeed * t + up * bob;
                towHorsa.SetPositionAndRotation(gliderPos, heading * Quaternion.Euler(released ? gliderPitch : 1.5f + Mathf.Sin(t * 0.9f) * 0.6f, 0f, Mathf.Sin(t * 0.7f) * 2f));


                // the rope: on tow it sags between the tug's tail and the bridle; once the glider lets go the free end
                // drops back behind the tug, and once the tug lets go too the whole rope falls, end over end
                if (!released) { Rope(towRope, towHitch.position, Join(p0), 3.5f); }
                else
                {
                    if (!dropped && t >= releaseAt + tugDrop) { dropped = true; velA = (towHitch.position - endA) / Mathf.Max(dt, 0.001f); }
                    if (!dropped) endA = towHitch.position;
                    else { velA += (Vector3.down * 9.81f - velA * 0.35f) * dt; endA += velA * dt; }
                    velB += (Vector3.down * 9.81f - velB * 0.6f) * dt;
                    endB += velB * dt;
                    // keep the two ends a rope's length apart at most (it's a rope, not a spring)
                    Vector3 d = endB - endA; float len = d.magnitude;
                    if (len > ropeLength)
                    {
                        Vector3 fix = d / len * (len - ropeLength);
                        if (dropped) { endA += fix * 0.5f; endB -= fix * 0.5f; } else endB -= fix;
                        Vector3 n = d / len;
                        float rel = Vector3.Dot(velB - velA, n);
                        if (rel > 0f) { if (dropped) { velA += n * rel * 0.5f; velB -= n * rel * 0.5f; } else velB -= n * rel; }
                    }
                    Rope(towRope, endA, endB, Mathf.Clamp((ropeLength - len) * 0.4f + 2f, 2f, 18f));
                }
                if (!released) { towBridle.SetPosition(0, bridlePort.position); towBridle.SetPosition(1, Join(p0)); towBridle.SetPosition(2, bridleStarboard.position); }
                else
                {
                    Vector3 back = (endB - endA).normalized, side = Vector3.Cross(Vector3.up, back).normalized;
                    towBridle.SetPosition(0, endB + back * 11f + side * 1.2f + Vector3.down * 1.5f);
                    towBridle.SetPosition(1, endB);
                    towBridle.SetPosition(2, endB + back * 11f - side * 1.2f + Vector3.down * 1.5f);
                }

                // the camera: on tow (and for a few seconds after the release) below and behind the glider, looking
                // up past it to the tug; then a cut to just off the Halifax's starboard wing as it banks away to port
                // and lets go of its end of the rope, the glider far below and behind
                if (cam)
                {
                    bool onTug = released && t >= releaseAt + tugShot;
                    Vector3 eye, look;
                    if (!onTug)
                    {
                        eye = released ? gliderPos + heading * new Vector3(-20f, -8f, -46f)
                                       : start + fwd * TowSpeed * t + heading * new Vector3(-20f, -8f, -46f) + up * bob * 0.4f;
                        look = Vector3.Lerp(gliderPos + up * 2.5f, tugPos, released ? 0.4f : 0.3f);
                    }
                    else
                    {
                        float k = (t - releaseAt - tugShot) / (total - releaseAt - tugShot);
                        Quaternion level = Quaternion.Euler(0f, (heading * Quaternion.Euler(0f, tugYaw, 0f)).eulerAngles.y, 0f);   // the turn, without the bank
                        eye = tugPos + level * new Vector3(Mathf.Lerp(34f, 30f, k), Mathf.Lerp(-5f, -9f, k), Mathf.Lerp(-16f, -34f, k));
                        look = tugPos + level * new Vector3(-4f, -3f, Mathf.Lerp(10f, -6f, k));
                    }
                    Quaternion want = Quaternion.LookRotation(look - eye, up);
                    bool cut = !haveEye || (onTug && !eyeOnTug);
                    eyeOnTug = onTug; haveEye = true;
                    cam.transform.SetPositionAndRotation(eye, cut ? want : Quaternion.Slerp(cam.transform.rotation, want, dt * 5f));
                    cam.fieldOfView = onTug ? 48f : 50f;
                }
                yield return null;
            }
            if (!released) StartCoroutine(FadeScore(3f));
            EndTow();
        }

        void EndTow()
        {
            for (int i = 1; i < pairs.Count; i++)
            {
                var p = pairs[i];
                if (p.tug) Destroy(p.tug.gameObject); if (p.horsa) Destroy(p.horsa.gameObject);
                if (p.rope) Destroy(p.rope.gameObject); if (p.bridle) Destroy(p.bridle.gameObject);
            }
            pairs.Clear();
            if (sea) Destroy(sea);
            if (towGroup && towGroup.activeSelf) towGroup.SetActive(false);
            if (towFogSaved) { RenderSettings.fogDensity = towFog; towFogSaved = false; }
        }

        IEnumerator HamAndJam()
        {
            Vector3 b = bridgeCentre ? bridgeCentre.position : Vector3.zero;
            Say("00:21", "The bridge is taken in minutes. Lieutenant Den Brotheridge, first across at the head of his platoon, " +
                "is shot in the neck — among the first Allied soldiers killed by enemy fire on D-Day.");
            yield return Shot(7f, k => (b + new Vector3(Mathf.Lerp(-34f, -26f, k), 4f, Mathf.Lerp(-18f, -12f, k)), b + Vector3.up * 2f));
            Say("00:26", "The success signal goes out on the wireless, again and again — Lance-Corporal Ted Tappenden keeps at it for " +
                "a long time with no answer, until he is shouting \"Ham and bloody Jam!\" Both bridges captured intact.");
            yield return Shot(6f, k => (b + new Vector3(Mathf.Lerp(18f, 26f, k), 2.2f, -9f), b + new Vector3(-10f, 3f, 0f)));
            // the sappers go over the bridge for the demolition charges (MissionDirector.DeployDefence sends them)
            Say("00:30", "Captain Jock Neilson's sappers go over the bridge looking for the demolition charges. The chambers are empty: " +
                "the explosives are found stored in a hut nearby. The bridge will not be blown.");
            yield return Shot(6.5f, k => (b + new Vector3(Mathf.Lerp(-21f, -15f, k), Mathf.Lerp(1.4f, 1.9f, k), Mathf.Lerp(-6.5f, -5f, k)), b + new Vector3(Mathf.Lerp(-4f, 4f, k), 0.6f, 0.5f)));
            // and the rest take up positions on both banks for the counter-attack that must come
            Say("00:32", "Howard knows the Germans will be back. His platoons dig in on both banks of the canal: Brens covering the towpaths " +
                "and the road from Bénouville, riflemen in the ditches and behind the walls.");
            yield return Shot(7f, k => (b + new Vector3(Mathf.Lerp(-8f, 6f, k), Mathf.Lerp(16f, 19f, k), Mathf.Lerp(-34f, -30f, k)), b + new Vector3(Mathf.Lerp(-10f, 4f, k), 0f, 4f)));
            if (cafe)
            {
                Say("00:30", "In the café at the west end, Georges and Thérèse Gondrée hide in the cellar with their daughters. " +
                    "Their house is said to be the first in France liberated on D-Day.");
                Vector3 c = cafe.position;
                yield return Shot(6f, k => (Ground(c + new Vector3(Mathf.Lerp(16f, 12f, k), 0f, 20f), 2f), c + Vector3.up * 4f));
            }
        }

        IEnumerator TanksComing()
        {
            Vector3 j = junction ? junction.position : new Vector3(-66f, 0f, 0f);
            Vector3 road = benouvilleRoad ? benouvilleRoad.position : new Vector3(-180f, 0f, -40f);
            Say("01:30", "Engines in the dark at the Bénouville crossroads. The first vehicles of the 21st Panzer Division " +
                "feel their way down the road toward the bridge, without infantry.");
            yield return Shot(6f, k => (Ground(Vector3.Lerp(j, road, 0.25f) + new Vector3(0f, 0f, 8f), 1.4f), Ground(road, 1.5f)));
            Say("01:55", "Sergeant \"Wagger\" Thornton crawls forward to the T-junction with D Company's only working PIAT. " +
                "Its range is short and it is not reliable. He will have one shot, at about thirty yards.");
            yield return Shot(6f, k => (Ground(j + new Vector3(4f, 0f, -6f), Mathf.Lerp(0.8f, 1.6f, k)), Ground(road, 2f)));
            Say("01:58", "Your turn: the PIAT is weapon 3. Wait for the lead vehicle to come close — and don't miss.");
            yield return Shot(3.5f, k => (Ground(j + new Vector3(4f, 0f, -6f), 1.6f), Ground(road, 2f)));
        }

        IEnumerator Thornton()
        {
            Vector3 p = mission.panzer ? mission.panzer.transform.position : Vector3.zero;
            if (!Net.Active) Time.timeScale = 0.35f;
            Say("02:00", "The PIAT bomb strikes home. The vehicle's ammunition starts to explode — a firework display that goes on " +
                "for an hour. The Germans conclude the bridge is held by anti-tank guns, and pull back.");
            yield return Shot(6f, k => (Ground(p + new Vector3(Mathf.Lerp(-16f, -11f, k), 0f, Mathf.Lerp(12f, 7f, k)), 2.5f), p + Vector3.up));
            Time.timeScale = 1f;
            Say("02:00", "What it was is still argued about: Thornton remembered a Mark IV; later research points to one of " +
                "21st Panzer's converted French Hotchkiss self-propelled guns.");
            yield return Shot(6f, k => (Ground(p + new Vector3(-30f, 0f, 20f), 6f + 2f * k), p + Vector3.up));
        }

        IEnumerator Finale()
        {
            // Lord Lovat's commandos and Piper Bill Millin (a visual column, the same on every screen), marching at a
            // steady pace up the road from Bénouville and over the bridge, two files, piper in front
            Vector3 a = lovatStart ? lovatStart.position : new Vector3(-160f, 0f, -30f);
            Vector3 b = lovatEnd ? lovatEnd.position : new Vector3(60f, 0f, 0f);
            Vector3 dir = (b - a); dir.y = 0f; dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 b0 = bridgeCentre ? bridgeCentre.position : Vector3.zero;
            const float pace = 1.6f, spacing = 2.4f, dur = 26f;
            // start the piper where he reaches the middle of the bridge as the scene ends
            float along0 = Vector3.Dot(b0 - a, dir) - pace * dur * 0.85f;
            var column = SpawnColumn(a + dir * along0, b, 18);
            PlayScore(Sfx.Bagpipes, 0.8f, true);
            Say("13:00", "From Sword Beach, Lord Lovat's 1st Special Service Brigade fights its way inland to the bridges — " +
                "led by Piper Bill Millin playing the bagpipes, in defiance of the War Office.");
            float t = 0f;
            while (t < dur && !skip)
            {
                t += Time.unscaledDeltaTime;
                for (int i = 0; i < column.Count; i++)
                {
                    var s = column[i]; if (!s) continue;
                    // the piper alone in front, then pairs; a little unevenness in the step
                    int rank = i == 0 ? 0 : (i + 1) / 2 + 1;
                    float off = i == 0 ? 0f : (i % 2 == 0 ? 1f : -1f) * 0.9f;
                    float along = along0 + pace * t - rank * spacing + Mathf.Sin(i * 1.7f) * 0.25f;
                    Vector3 p = a + dir * along + side * off;
                    p = Ground(p, 0f);
                    if (p.x > -24f && p.x < 24f) p.y = b0.y + 0.15f;   // on the deck
                    s.SetPositionAndRotation(p, Quaternion.LookRotation(dir));
                    var anim = s.GetComponentInChildren<Animator>();
                    if (anim) Locomotion.Set(anim, Vector3.forward * pace, 0.05f, true);
                }
                Vector3 lead = column.Count > 0 && column[0] ? column[0].position : b0;
                if (cam)
                {
                    Vector3 eye = b0 + new Vector3(10f, 3.5f, -16f);
                    cam.transform.SetPositionAndRotation(eye, Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(lead + Vector3.up - eye), Time.unscaledDeltaTime * 3f));
                }
                if (t > 12f && t - Time.unscaledDeltaTime <= 12f)
                    Say("13:00", "Lovat is said to have apologised to Howard for being a couple of minutes late. The pipes play the commandos across the bridge, under fire.");
                yield return null;
            }
            yield return Black(6f, "6 JUNE 1944",
                "The bridges held. Later in 1944 the Bénouville bridge was renamed Pegasus Bridge, after the badge of the " +
                "British airborne forces. The Café Gondrée still serves visitors at the west end today.");
            yield return Black(4f, "STAN'S GAME", "Made by Joan and Stan.  —  Press Space to fight on.");
            yield return FadeIn(1.5f);
        }

        /// <summary>
        /// The commandos: copies of the British soldier models (only their looks and their hands on their rifles -
        /// no AI, no health, nothing on the network), the piper in front without a rifle. Falls back to plain figures
        /// if the models are missing.
        /// </summary>
        List<Transform> SpawnColumn(Vector3 from, Vector3 to, int count)
        {
            var list = new List<Transform>();
            var md = mission ? mission : MissionDirector.Instance;
            var prefabs = new List<GameObject>();
            if (commandoPrefab) prefabs.Add(commandoPrefab);
            else if (md) foreach (var pf in new[] { md.britishRiflePrefab, md.britishSoldierPrefab, md.britishRiflePrefab, md.britishBrenPrefab }) if (pf) prefabs.Add(pf);
            // the green beret of the commandos (the officer model's maroon beret, re-dyed)
            var greenBeret = commandoPrefab ? Resources.Load<Texture2D>("Textures/British_Commando_BaseColor") : null;
            Material commandoMat = null;
            var tartan = new Material(Shader.Find("Universal Render Pipeline/Lit")); tartan.SetColor("_BaseColor", new Color(0.32f, 0.12f, 0.10f));
            var khaki = new Material(Shader.Find("Universal Render Pipeline/Lit")); khaki.SetColor("_BaseColor", new Color(0.36f, 0.33f, 0.24f));
            for (int i = 0; i < count; i++)
            {
                Transform root;
                if (prefabs.Count > 0)
                {
                    root = Figure(prefabs[i % prefabs.Count], i == 0);
                    if (greenBeret)
                        foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            if (!commandoMat) { commandoMat = new Material(r.sharedMaterial); commandoMat.SetTexture("_BaseMap", greenBeret); commandoMat.mainTexture = greenBeret; }
                            var ms = r.sharedMaterials; for (int k = 0; k < ms.Length; k++) ms[k] = commandoMat; r.sharedMaterials = ms;
                        }
                }
                else
                {
                    root = new GameObject("Commando").transform;
                    var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); body.transform.SetParent(root, false);
                    body.transform.localPosition = new Vector3(0f, 0.9f, 0f); body.transform.localScale = new Vector3(0.5f, 0.9f, 0.4f);
                    body.GetComponent<Renderer>().sharedMaterial = khaki; Destroy(body.GetComponent<Collider>());
                }
                root.name = i == 0 ? "Piper Bill Millin" : "Commando";
                if (i == 0)
                {
                    // the pipes under his left arm
                    var bag = GameObject.CreatePrimitive(PrimitiveType.Sphere); bag.transform.SetParent(root, false);
                    bag.transform.localPosition = new Vector3(-0.22f, 1.25f, 0.12f); bag.transform.localScale = new Vector3(0.22f, 0.26f, 0.3f);
                    bag.GetComponent<Renderer>().sharedMaterial = tartan; Destroy(bag.GetComponent<Collider>());
                    for (int d = 0; d < 3; d++)
                    {
                        var drone = GameObject.CreatePrimitive(PrimitiveType.Cylinder); drone.transform.SetParent(root, false);
                        drone.transform.localPosition = new Vector3(-0.2f + d * 0.06f, 1.55f, 0.05f - d * 0.03f);
                        drone.transform.localRotation = Quaternion.Euler(-25f, 0f, 30f - d * 12f);
                        drone.transform.localScale = new Vector3(0.025f, 0.28f - d * 0.05f, 0.025f);
                        drone.GetComponent<Renderer>().sharedMaterial = khaki; Destroy(drone.GetComponent<Collider>());
                    }
                }
                root.position = from;
                list.Add(root); props.Add(root.gameObject);
            }
            return list;
        }

        /// <summary>A soldier prefab copied for looks only: animation, hands on the rifle; everything else stripped.</summary>
        static Transform Figure(GameObject prefab, bool unarmed)
        {
            var park = new GameObject("park"); park.SetActive(false);
            var go = Instantiate(prefab, park.transform);
            // keep what draws and animates; take out the rest (network parts last - they depend on each other)
            var all = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = all.Length - 1; i >= 0; i--)   // last added first: they lean on the earlier ones
            {
                var mb = all[i];
                if (mb is WeaponHolder || mb is HandIK || mb is Weapon) continue;
                if (mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("Unity.Netcode")) continue;
                DestroyImmediate(mb);
            }
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("Unity.Netcode") && !(mb.GetType().Name == "NetworkObject")) DestroyImmediate(mb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (mb.GetType().Name == "NetworkObject") DestroyImmediate(mb);
            foreach (var c in go.GetComponentsInChildren<CharacterController>(true)) DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) r.isKinematic = true;
            foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            if (unarmed) foreach (var w in go.GetComponentsInChildren<WeaponHolder>(true)) { foreach (Transform c in w.transform) Destroy(c.gameObject); }
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            go.transform.SetParent(null, false);
            Destroy(park);
            return go.transform;
        }

        // ------------------------------------------------------------------ drawing

        public static void DrawCaption(string time, string text, float alpha, float bar, GUIStyle style)
        {
            if (alpha <= 0f) return;
            var ink = new Color(0.92f, 0.9f, 0.82f, alpha);
            var ts = new GUIStyle(style); ts.normal.textColor = ink;
            if (!string.IsNullOrEmpty(time))
                GUI.Label(new Rect(Screen.width * 0.06f, bar + Screen.height * 0.03f, Screen.width * 0.6f, 40), $"<b>{time}</b>", ts);
            var cs = new GUIStyle(ts) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Mathf.RoundToInt(Screen.height * 0.022f) };
            float w = Screen.width * 0.72f;
            GUI.Label(new Rect((Screen.width - w) / 2f, Screen.height - bar + bar * 0.12f, w, bar * 0.9f), text, cs);
        }

        void OnGUI()
        {
            if (letterbox <= 0.001f && fade <= 0f) return;
            GUI.depth = -10;
            var black = Texture2D.whiteTexture;
            var old = GUI.color;
            if (fade > 0f) { GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(fade)); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), black); }
            float bar = Screen.height * 0.14f * letterbox;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, bar), black);
            GUI.DrawTexture(new Rect(0, Screen.height - bar, Screen.width, bar), black);
            GUI.color = old;
            if (!Playing) return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.026f), richText = true };
            if (!string.IsNullOrEmpty(prevCaption))
            {
                float pa = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((prevGone - Time.unscaledTime) / CaptionFade));
                if (pa <= 0f) prevCaption = prevStamp = null;
                else DrawCaption(prevStamp, prevCaption, pa, bar, style);
            }
            if (!string.IsNullOrEmpty(caption)) DrawCaption(stamp, caption, CaptionAlpha(), bar, style);
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.LowerRight };
            hint.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
            GUI.Label(new Rect(0, Screen.height - 22, Screen.width - 12, 20), "Space: skip", hint);
        }
    }
}
