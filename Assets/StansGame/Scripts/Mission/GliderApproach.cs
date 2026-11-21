using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Flies a Horsa from release point down a glide path and crash-lands it at the landing
    /// marker: steep descent, flare, then a long skid with dust, ending nosed into the wire.
    /// Raises Landed so the MissionDirector can spawn the section that was aboard.
    /// </summary>
    public class GliderApproach : MonoBehaviour
    {
        public Transform landingPoint;          // where the glider comes to rest
        public float approachDistance = 900f;   // start this far back along the landing heading
        public float approachHeight = 160f;
        public float glideSpeed = 40f;          // ~145 km/h; real Horsa landed near 90-100 mph
        public float skidLength = 55f;
        public float delay = 0f;
        public ParticleSystem skidDust;
        public AudioSource windAudio, crashAudio;
        public bool autoStart = true;

        public enum Damage { WheelsTorn, NoseStoved, BrokenBack }
        [Header("How it ended up (June 1944 photographs)")]
        [Tooltip("Degrees the glider swings round at the end of the skid (No. 91 slewed round as it hit the wire).")]
        public float slew = 0f;
        public Damage damage = Damage.WheelsTorn;

        [Header("The wreck as it lay (set by the level builder from the photos)")]
        [Tooltip("How far it sinks onto its belly once the wheels are gone, and how it ends up pitched and rolled (+roll = port side down).")]
        public float settleDrop = 0.62f, settlePitch = 1.5f, settleRoll = 1.5f;
        [Tooltip("The tail section at its hinge under the break (Euler degrees): zero = not broken.")]
        public Vector3 tailBreak;
        [Tooltip("Nose stove in (squashed and bent down), or just the cockpit glazing gone leaving the bare frame.")]
        public bool noseCrushed, noseFrameBare;
        [Tooltip("Degrees the outer wing panels are bent down at the wing joint (torn at the joint).")]
        public float portWingFold, starboardWingFold;

        public event System.Action<GliderApproach> Landed;
        public bool HasLanded { get; private set; }

        Vector3 start, touchdown, rest;
        Quaternion heading;
        float t, total;
        bool running;

        void Start()
        {
            if (!landingPoint) { enabled = false; return; }
            if (!windAudio) windAudio = MakeSource(Sfx.Wind, true, 0.8f);
            if (!crashAudio) crashAudio = MakeSource(Sfx.Synthesised("crash"), false, 1f);
            heading = Quaternion.Euler(0f, landingPoint.eulerAngles.y, 0f);
            rest = landingPoint.position;
            touchdown = rest - heading * Vector3.forward * skidLength;
            start = touchdown - heading * Vector3.forward * approachDistance + Vector3.up * approachHeight;
            total = Vector3.Distance(start, touchdown) / glideSpeed;
            transform.SetPositionAndRotation(start, heading);
        }

        float waited;

        AudioSource MakeSource(AudioClip clip, bool loop, float volume)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip; src.loop = loop; src.volume = volume; src.playOnAwake = false;
            src.spatialBlend = 1f; src.minDistance = 15f; src.maxDistance = 1500f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.dopplerLevel = 0.3f;
            return src;
        }

        bool wrecked;

        /// <summary>
        /// How it lay afterwards: the undercarriage torn off (the Horsas came in fast and hard on the rough field)
        /// and down on its belly; No. 91 with its nose stove in against the wire; No. 92 with its back broken.
        /// Runs on the host at the end of the skid and on clients when they learn the glider has landed.
        /// </summary>
        public void Wreck(bool settle = true)
        {
            if (wrecked) return;
            wrecked = true;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.Contains("wheel") || n.Contains("leg") || n.Contains("radius rod")) t.gameObject.SetActive(false);
            }
            if (settle)
            {
                // down on its belly (on a client the network transform brings the settled position over)
                transform.position += Vector3.down * settleDrop;
                transform.rotation *= Quaternion.Euler(settlePitch, 0f, settleRoll);
            }
            var tail = transform.Find("Tail section");
            if (tail && tailBreak != Vector3.zero) tail.localRotation = Quaternion.Euler(tailBreak);
            else if (tail && damage == Damage.BrokenBack) tail.localRotation = Quaternion.Euler(-7.5f, 11f, 4f);   // snapped and sagging to one side
            var nose = transform.Find("Nose section");
            if (nose)
            {
                if (noseCrushed) { nose.localScale = new Vector3(1.06f, 0.8f, 0.45f); nose.localRotation = Quaternion.Euler(12f, 5f, -7f); }
                var glazing = nose.Find("Nose glazing"); var frame = nose.Find("Nose frame");
                if (frame) frame.gameObject.SetActive(noseCrushed || noseFrameBare);
                if (glazing && (noseFrameBare || noseCrushed)) glazing.gameObject.SetActive(false);
            }
            if (noseCrushed || noseFrameBare || damage == Damage.NoseStoved)
            {
                var pitot = transform.Find("Pitot tube"); if (pitot) pitot.gameObject.SetActive(false);
            }
            Fold("Wing outer port", portWingFold, 1f);
            Fold("Wing outer starboard", starboardWingFold, -1f);
        }

        [ContextMenu("Show the wreck (editor preview)")]
        void PreviewWreck() { wrecked = false; Wreck(true); }

        void Fold(string part, float degrees, float sign)
        {
            var w = transform.Find(part);
            if (!w || degrees == 0f) return;
            w.localRotation = Quaternion.Euler(0f, 0f, sign * degrees);   // about the wing joint, outer end down
            var splinters = w.Find("Splinters"); if (splinters) splinters.gameObject.SetActive(true);
        }

        public void Begin() { if (running || HasLanded) return; running = true; t = 0f; if (windAudio) windAudio.Play(); }

        void Update()
        {
            // start after the delay, but only once the world is running (not in the multiplayer menu,
            // and only on the host: clients see the glider through its network transform)
            if (!running && !HasLanded && autoStart && Net.Simulates && !CinematicDirector.HoldGliders)
            {
                waited += Time.deltaTime;
                if (waited >= delay) Begin();
            }
            if (!Net.Simulates && !wrecked && MissionDirector.Instance && MissionDirector.Instance.gliders != null)
            {
                int i = System.Array.IndexOf(MissionDirector.Instance.gliders, this);
                if (i >= 0 && (MissionDirector.Instance.LandedMask & (1 << i)) != 0)
                {
                    Wreck(false);
                }
            }
            if (!running) return;
            t += Time.deltaTime;
            if (t < total)
            {
                float k = t / total;
                // steep dive then flare in the last 15%
                float flare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, k));
                Vector3 p = Vector3.Lerp(start, touchdown, k);
                p.y = Mathf.Lerp(start.y, touchdown.y, Mathf.Pow(k, 0.8f + flare * 0.6f));
                float pitch = Mathf.Lerp(12f, -4f, flare);
                float roll = Mathf.Sin(t * 0.9f) * 4f * (1f - k);
                transform.SetPositionAndRotation(p, heading * Quaternion.Euler(pitch, 0f, roll));
            }
            else
            {
                float s = t - total;
                float skidTime = skidLength / (glideSpeed * 0.5f);
                float k = Mathf.Clamp01(s / skidTime);
                float eased = 1f - (1f - k) * (1f - k);
                Vector3 p = Vector3.Lerp(touchdown, rest, eased);
                float bounce = Mathf.Abs(Mathf.Sin(k * 18f)) * 0.25f * (1f - k);
                p.y += bounce;
                // the last part of the skid can swing it round (wire, a ditch, a wing digging in)
                float swing = slew * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, k));
                transform.SetPositionAndRotation(p, heading * Quaternion.Euler(-2f + bounce * 4f, swing + Mathf.Sin(k * 6f) * 6f * (1f - k), 0f));
                if (skidDust && !skidDust.isPlaying) { skidDust.Play(); if (crashAudio) crashAudio.Play(); }
                if (k >= 1f)
                {
                    running = false; HasLanded = true; SoldierAI.GliderLanded();
                    transform.rotation = heading * Quaternion.Euler(0f, slew, 0f);
                    Wreck();
                    if (skidDust) skidDust.Stop();
                    if (windAudio) windAudio.Stop();
                    Landed?.Invoke(this);
                }
            }
        }
    }
}
