using System.Collections.Generic;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Tells the local player clearly that they were hit: a hit sound, a red flash at the screen
    /// edges, a short camera flinch, a marker pointing at where the shot came from, and a heartbeat
    /// plus a pulsing red edge when health is low.
    /// Damage is detected by watching Health.Current, so it also works for co-op clients, where the
    /// host applies damage and only the new value arrives (Health.Damaged does not fire there).
    /// </summary>
    [DefaultExecutionOrder(600)]
    public sealed class HitFeedback : MonoBehaviour
    {
        [Tooltip("Health fraction below which the heartbeat and the red pulse start.")]
        public float lowHealth = 0.35f;

        [Tooltip("Seconds a direction marker stays on screen.")]
        public float markerSeconds = 1.4f;

        const float FlinchSeconds = 0.18f;

        struct Marker
        {
            public Vector3 worldDir;
            public float until;
        }

        SoldierController soldier;
        Health health;
        AudioSource voice;
        AudioSource heart;
        readonly List<Marker> markers = new List<Marker>();

        float lastHp = -1f;
        float flash;
        float flinchUntil;
        float flinchStrength;
        Vector3 lastDir;
        float lastDirTime = -10f;
        bool haveDir;

        static AudioClip hitClip;
        static AudioClip heartClip;
        static Texture2D vignette;
        static Texture2D white;

        void Awake()
        {
            soldier = GetComponent<SoldierController>();
            health = GetComponent<Health>();
        }

        void OnEnable()
        {
            if (health != null) health.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            if (health != null) health.Damaged -= OnDamaged;
            if (heart != null) heart.Stop();
        }

        void Start()
        {
            if (hitClip == null) hitClip = MakeHitClip();
            if (heartClip == null) heartClip = MakeHeartClip();
            if (vignette == null) vignette = MakeVignette(128);
            if (white == null)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }

            voice = MakeSource("Hit sound", false);
            heart = MakeSource("Heartbeat", true);
            heart.clip = heartClip;
            heart.volume = 0f;
        }

        AudioSource MakeSource(string name, bool loop)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.spatialBlend = 0f;      // in the player's head, not in the world
            s.playOnAwake = false;
            s.loop = loop;
            s.priority = 0;
            return s;
        }

        bool IsLocal()
        {
            return soldier != null && AdditionsBootstrap.LocalPlayer() == soldier;
        }

        // Host and single player: the event carries the shooter, used for the direction marker.
        void OnDamaged(Health h, DamageInfo info)
        {
            if (info.source != null)
            {
                lastDir = info.source.transform.position - transform.position;
                haveDir = true;
            }
            else if (info.direction.sqrMagnitude > 0.0001f)
            {
                lastDir = -info.direction;
                haveDir = true;
            }
            else
            {
                haveDir = false;
            }
            lastDirTime = Time.time;
        }

        void Update()
        {
            if (health == null || voice == null) return;
            bool local = IsLocal();

            float hp = health.Current;
            if (lastHp < 0f) lastHp = hp;
            float lost = lastHp - hp;
            lastHp = hp;
            if (local && lost > 0.01f) OnHit(lost);

            float max = Mathf.Max(1f, health.maxHealth);
            float frac = Mathf.Clamp01(hp / max);
            float low = Mathf.Clamp01(1f - frac / Mathf.Max(0.01f, lowHealth));
            float wantVolume = local && GameSettings.Heartbeat && !health.IsDead && frac < lowHealth ? Mathf.Lerp(0.25f, 0.9f, low) : 0f;
            heart.volume = Mathf.MoveTowards(heart.volume, wantVolume, Time.deltaTime * 1.5f);
            heart.pitch = Mathf.Lerp(1f, 1.4f, low);
            if (heart.volume > 0.001f && !heart.isPlaying) heart.Play();
            else if (heart.volume <= 0.001f && heart.isPlaying) heart.Stop();

            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 1.8f);
            for (int i = markers.Count - 1; i >= 0; i--)
                if (Time.time > markers[i].until) markers.RemoveAt(i);
        }

        void OnHit(float lost)
        {
            float max = Mathf.Max(1f, health.maxHealth);
            float k = Mathf.Clamp01(lost / max * 3f);          // a third of max health or more is a full-strength hit
            float fx = GameSettings.HitEffects;

            flash = Mathf.Clamp01(flash + (0.35f + 0.65f * k) * fx);
            flinchUntil = Time.time + FlinchSeconds;
            flinchStrength = (1.2f + 2.8f * k) * fx;

            voice.pitch = Random.Range(0.92f, 1.08f);
            voice.PlayOneShot(hitClip, 0.55f + 0.45f * k);

            if (haveDir && Time.time - lastDirTime < 0.3f)
            {
                Marker m;
                m.worldDir = lastDir;
                m.until = Time.time + markerSeconds;
                markers.Add(m);
            }
        }

        void LateUpdate()
        {
            // Flinch after PlayerView has placed the camera. SoldierController sets the camera
            // rotation again every frame, so this never adds up.
            if (!IsLocal() || soldier.cam == null || Time.time > flinchUntil || !soldier.enabled) return;
            float t = (flinchUntil - Time.time) / FlinchSeconds;
            float a = flinchStrength * t;
            soldier.cam.transform.rotation *= Quaternion.Euler(-a * 0.6f + Random.Range(-a, a) * 0.3f, Random.Range(-a, a) * 0.4f, Random.Range(-a, a) * 0.5f);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !IsLocal() || CinematicDirector.Playing) return;
            if (vignette == null || white == null) return;

            float max = Mathf.Max(1f, health.maxHealth);
            float frac = Mathf.Clamp01(health.Current / max);
            float low = health.IsDead ? 0f : Mathf.Clamp01(1f - frac / Mathf.Max(0.01f, lowHealth));
            float pulse = low * (0.55f + 0.45f * Mathf.Sin(Time.time * Mathf.Lerp(4f, 9f, low)));
            float alpha = Mathf.Clamp01(flash * 0.7f + pulse * 0.45f * GameSettings.HitEffects);

            Color old = GUI.color;
            if (alpha > 0.01f)
            {
                GUI.color = new Color(0.75f, 0.02f, 0.02f, alpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), vignette, ScaleMode.StretchToFill);
            }
            DrawMarkers();
            GUI.color = old;
        }

        void DrawMarkers()
        {
            if (markers.Count == 0 || soldier.cam == null) return;
            Vector3 fwd = soldier.cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return;

            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float radius = Screen.height * 0.17f;
            float w = Screen.height * 0.07f, h = Screen.height * 0.012f;
            Matrix4x4 keep = GUI.matrix;
            foreach (Marker m in markers)
            {
                Vector3 d = m.worldDir;
                d.y = 0f;
                if (d.sqrMagnitude < 0.0001f) continue;
                float angle = Vector3.SignedAngle(fwd, d, Vector3.up);   // 0 = in front, 90 = to the right
                float life = Mathf.Clamp01((m.until - Time.time) / markerSeconds);
                GUI.color = new Color(0.95f, 0.12f, 0.08f, 0.9f * life);
                GUI.matrix = keep;
                GUIUtility.RotateAroundPivot(angle, centre);
                GUI.DrawTexture(new Rect(centre.x - w * 0.5f, centre.y - radius - h, w, h), white);
            }
            GUI.matrix = keep;
        }

        // ---------------------------------------------------------------- generated assets

        const int Rate = 44100;

        static AudioClip MakeHitClip()
        {
            int n = (int)(Rate * 0.32f);
            float[] d = new float[n];
            System.Random rng = new System.Random(7);
            float phase = 0f, low = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(150f, 52f, Mathf.Clamp01(t / 0.12f));   // falling body thump
                phase += 2f * Mathf.PI * f / Rate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 16f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.18f;                                 // muffled impact noise
                float smack = low * Mathf.Exp(-t * 45f) * 1.6f;
                d[i] = Mathf.Clamp(thump * 0.85f + smack, -1f, 1f);
            }
            AudioClip c = AudioClip.Create("stans_player_hit", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip MakeHeartClip()
        {
            int n = Rate;   // one second, looped; pitch speeds it up at lower health
            float[] d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                d[i] = Beat(t, 0f, 1f) + Beat(t, 0.27f, 0.65f);
            }
            AudioClip c = AudioClip.Create("stans_heartbeat", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static float Beat(float t, float at, float gain)
        {
            float u = t - at;
            if (u < 0f || u > 0.18f) return 0f;
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u / 0.012f) * 0.5f) * Mathf.Exp(-u * 26f);
            return gain * env * (Mathf.Sin(2f * Mathf.PI * 48f * u) * 0.8f + Mathf.Sin(2f * Mathf.PI * 96f * u) * 0.2f);
        }

        static Texture2D MakeVignette(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half, dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, r));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
