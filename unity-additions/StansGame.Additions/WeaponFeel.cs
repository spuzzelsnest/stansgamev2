using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame.Additions
{
    /// <summary>
    /// Weapon feel for the local player:
    /// - recoil: every shot kicks the view and the weapon (pitch, a little yaw and roll, a push back
    ///   into the shoulder) and both recover quickly; each weapon type kicks differently. The game's own
    ///   permanent aim climb per shot stays as it is; this is the visible kick on top of it.
    /// - sway: the weapon lags behind fast mouse movement and breathes slightly when still.
    /// - bob: the weapon and view move with the footsteps, more when running, little when aiming.
    /// - hit markers: a tick sound on every hit and a cross on screen, red and larger on a kill. The
    ///   game's crosshair draws its own marker at the hip, so the cross here is drawn only while that
    ///   crosshair is hidden (aiming down the sights). The sound plays always.
    /// - bullet cracks: an enemy round passing close by cracks past the player's head, shakes the view
    ///   a little and darkens the screen edges for a moment (suppression).
    /// Runs after PlayerView (which places the camera and lines up the sights) and before HitFeedback.
    /// While aiming down the sights the kick turns the view and the weapon together around the eye,
    /// so the sight picture stays lined up while it climbs and settles.
    /// </summary>
    [DefaultExecutionOrder(550)]
    public sealed class WeaponFeel : MonoBehaviour
    {
        struct Profile
        {
            public float pitch, yaw, roll, back, flip, recover;
            public Profile(float pitch, float yaw, float roll, float back, float flip, float recover)
            {
                this.pitch = pitch; this.yaw = yaw; this.roll = roll; this.back = back; this.flip = flip; this.recover = recover;
            }
        }

        // Degrees of view kick, degrees of yaw/roll spread, metres pushed back, extra weapon flip (degrees),
        // recovery speed (1/s). Bolt rifle and pistol kick hard and single; SMG and Bren are many small kicks.
        static Profile For(HoldStyle h)
        {
            switch (h)
            {
                case HoldStyle.Rifle:    return new Profile(2.6f, 0.55f, 1.4f, 0.055f, 4.5f, 9f);
                case HoldStyle.SMG:      return new Profile(0.75f, 0.45f, 0.6f, 0.022f, 1.6f, 14f);
                case HoldStyle.Heavy:    return new Profile(1.05f, 0.5f, 0.8f, 0.035f, 2.0f, 12f);
                case HoldStyle.Pistol:   return new Profile(2.1f, 0.5f, 1.2f, 0.03f, 7.0f, 10f);
                case HoldStyle.Launcher: return new Profile(4.5f, 0.6f, 1.5f, 0.11f, 5.0f, 5f);
            }
            return new Profile(1f, 0.4f, 0.6f, 0.03f, 2f, 12f);
        }

        SoldierController soldier;
        Health health;
        CharacterController cc;
        AudioSource ui;
        AudioSource crackSource;

        Weapon lastWeapon;
        int lastAmmo = -1;

        // Recoil: the target jumps on a shot and decays; the current value follows the target fast.
        Vector3 kickTarget, kick;       // x pitch up, y yaw, z roll (degrees)
        float backTarget, back;         // metres
        float flipTarget, flip;         // degrees, weapon only
        float recover = 12f;

        Vector2 sway, swayVel;          // degrees, weapon only
        float bobPhase, bobAmount;
        float suppression, shakeUntil;

        float hitUntil, killUntil;
        static Texture2D white, vignette;
        static AudioClip hitClip, killClip;
        static AudioClip[] crackClips;

        /// <summary>Test counters: shots felt, hit markers shown, kill markers shown, cracks heard.</summary>
        public static int Shots, Hits, Kills, Cracks;

        /// <summary>The view kick being applied this frame, in degrees (for tests).</summary>
        public Vector3 CurrentKick { get { return kick; } }

        /// <summary>Suppression from near misses, 0 to 1 (for tests).</summary>
        public float Suppression { get { return suppression; } }

        void Awake()
        {
            soldier = GetComponent<SoldierController>();
            health = GetComponent<Health>();
            cc = GetComponent<CharacterController>();
        }

        void OnEnable()
        {
            Weapon.HitConfirmed += OnHitConfirmed;
            Weapon.ShotPassed += OnShotPassed;
        }

        void OnDisable()
        {
            Weapon.HitConfirmed -= OnHitConfirmed;
            Weapon.ShotPassed -= OnShotPassed;
        }

        void Start()
        {
            if (white == null)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (vignette == null) vignette = MakeVignette(128);
            if (hitClip == null) hitClip = MakeTick(false);
            if (killClip == null) killClip = MakeTick(true);
            if (crackClips == null)
            {
                crackClips = new AudioClip[3];
                for (int i = 0; i < crackClips.Length; i++) crackClips[i] = MakeCrack(11 + i * 7);
            }

            GameObject a = new GameObject("Hit marker sound");
            a.transform.SetParent(transform, false);
            ui = a.AddComponent<AudioSource>();
            ui.spatialBlend = 0f;
            ui.playOnAwake = false;
            ui.priority = 0;

            GameObject b = new GameObject("Bullet crack");
            crackSource = b.AddComponent<AudioSource>();
            crackSource.spatialBlend = 1f;          // heard from the side the round passed on
            crackSource.minDistance = 1.5f;
            crackSource.maxDistance = 25f;
            crackSource.rolloffMode = AudioRolloffMode.Linear;
            crackSource.dopplerLevel = 0f;
            crackSource.playOnAwake = false;
            crackSource.priority = 0;
            DontDestroyOnLoad(b);
        }

        void OnDestroy()
        {
            if (crackSource != null) Destroy(crackSource.gameObject);
        }

        bool IsLocal()
        {
            return soldier != null && AdditionsBootstrap.LocalPlayer() == soldier;
        }

        bool Active()
        {
            return IsLocal() && soldier.enabled && soldier.cam != null && !CinematicDirector.Playing && (health == null || !health.IsDead);
        }

        // ---------------------------------------------------------------- events

        void OnHitConfirmed(GameObject shooter, Health victim, bool killed)
        {
            if (shooter != gameObject || !IsLocal()) return;
            if (victim != null && health != null && victim.faction == health.faction) return;   // no reward for hitting your own
            if (killed)
            {
                killUntil = Time.time + 0.5f;
                Kills++;
                if (ui != null && killClip != null) ui.PlayOneShot(killClip, 0.85f);
            }
            else
            {
                Hits++;
                if (ui != null && hitClip != null) ui.PlayOneShot(hitClip, 0.7f);
            }
            hitUntil = Time.time + 0.22f;
        }

        void OnShotPassed(Vector3 from, Vector3 to, GameObject shooter)
        {
            if (shooter == null || shooter == gameObject || !Active()) return;
            Health sh = shooter.GetComponentInParent<Health>();
            if (sh != null && health != null && sh.faction == health.faction) return;   // only enemy fire cracks

            Vector3 ear = soldier.cam.transform.position;
            Vector3 seg = to - from;
            float len = seg.magnitude;
            if (len < 0.5f) return;
            Vector3 dir = seg / len;
            float along = Mathf.Clamp(Vector3.Dot(ear - from, dir), 0f, len);
            if (along < 2f) return;                                  // the shooter's own muzzle blast is enough there
            Vector3 closest = from + dir * along;
            float dist = Vector3.Distance(closest, ear);
            const float Radius = 4f;
            if (dist > Radius) return;
            if (along >= len - 0.05f && dist > 1f) return;           // stopped by something before it got here

            float k = 1f - dist / Radius;
            Cracks++;
            if (crackSource != null && crackClips != null)
            {
                crackSource.transform.position = closest + (closest - ear).normalized * 0.4f;
                crackSource.pitch = Random.Range(0.92f, 1.1f);
                crackSource.PlayOneShot(crackClips[Random.Range(0, crackClips.Length)], Mathf.Lerp(0.35f, 1f, k));
            }
            float fx = GameSettings.HitEffects;
            suppression = Mathf.Clamp01(suppression + (0.18f + 0.3f * k) * fx);
            shakeUntil = Time.time + 0.12f;
        }

        // ---------------------------------------------------------------- per frame

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            suppression = Mathf.MoveTowards(suppression, 0f, dt * 0.55f);
            if (!Active())
            {
                kickTarget = kick = Vector3.zero;
                backTarget = back = flipTarget = flip = 0f;
                sway = swayVel = Vector2.zero;
                lastWeapon = null;
                lastAmmo = -1;
                return;
            }

            Camera cam = soldier.cam;
            Weapon w = soldier.CurrentWeapon;
            bool aiming = soldier.IsAiming;
            bool fp = GameSettings.FirstPerson;
            float motion = GameSettings.WeaponMotion;

            // A shot was fired this frame if the loaded rounds went down on the same weapon.
            if (w != null && w == lastWeapon && lastAmmo >= 0 && w.Ammo < lastAmmo && !w.IsReloading)
            {
                int n = Mathf.Min(lastAmmo - w.Ammo, 3);
                for (int i = 0; i < n; i++) { Kick(w, aiming); MuzzleSmoke.Shot(w); }
            }
            lastWeapon = w;
            lastAmmo = w != null ? w.Ammo : -1;
            MuzzleSmoke.Tick(w);

            float fall = 1f - Mathf.Exp(-recover * dt);
            float rise = 1f - Mathf.Exp(-45f * dt);
            kickTarget = Vector3.Lerp(kickTarget, Vector3.zero, fall);
            backTarget = Mathf.Lerp(backTarget, 0f, fall * 1.3f);
            flipTarget = Mathf.Lerp(flipTarget, 0f, fall * 1.2f);
            kick = Vector3.Lerp(kick, kickTarget, rise);
            back = Mathf.Lerp(back, backTarget, rise);
            flip = Mathf.Lerp(flip, flipTarget, rise);

            // Sway: the weapon trails the mouse a little, springs back, and breathes when still.
            Vector2 md = Mouse.current != null && !SettingsPanel.Open ? Mouse.current.delta.ReadValue() : Vector2.zero;
            float swayScale = (aiming ? 0.18f : 1f) * motion;
            Vector2 swayGoal = Vector2.ClampMagnitude(new Vector2(-md.x, -md.y) * 0.035f, 3f) * swayScale;
            sway = Vector2.SmoothDamp(sway, swayGoal, ref swayVel, 0.09f, Mathf.Infinity, dt);

            // Bob: one cycle per two steps, scaled by ground speed.
            Vector3 v = cc != null ? cc.velocity : Vector3.zero;
            v.y = 0f;
            float speed = cc != null && cc.isGrounded ? v.magnitude : 0f;
            bobAmount = Mathf.MoveTowards(bobAmount, Mathf.Clamp01(speed / 5.5f), dt * 3f);
            bobPhase += dt * Mathf.Clamp(speed, 0f, 8f) * 2.4f;
            if (bobPhase > Mathf.PI * 200f) bobPhase -= Mathf.PI * 200f;
            float bobScale = bobAmount * motion * (aiming ? 0.22f : 1f);
            float breath = (aiming ? 0.25f : 1f) * motion;

            float shake = Time.time < shakeUntil ? suppression * 0.35f * GameSettings.HitEffects : 0f;

            // View: kick around the eye, step bob, near-miss shake.
            Transform ct = cam.transform;
            Vector3 eye = ct.position;
            Quaternion before = ct.rotation;
            float viewKick = (aiming ? 1f : 0.55f) * (fp ? 1f : 0.6f) * Mathf.Lerp(0.5f, 1f, motion);
            Quaternion viewLocal = Quaternion.Euler(-kick.x * viewKick + Random.Range(-shake, shake), kick.y * viewKick + Random.Range(-shake, shake), kick.z * viewKick * 0.6f);
            Quaternion bobRoll = Quaternion.identity;
            if (fp)
            {
                bobRoll = Quaternion.Euler(0f, 0f, Mathf.Sin(bobPhase) * 0.35f * bobScale);
                ct.position = eye + ct.up * (Mathf.Sin(bobPhase * 2f) * 0.012f * bobScale);
            }
            ct.rotation = before * viewLocal * bobRoll;

            if (w == null || !w.gameObject.activeInHierarchy) return;
            // Move the WeaponHolder, not the weapon: the holder is placed fresh every frame
            // (WeaponHolder.LateUpdate, SetPositionAndRotation), the weapon under it never is, so
            // offsets on the weapon itself would add up frame after frame.
            WeaponHolder holder = w.GetComponentInParent<WeaponHolder>();
            if (holder == null || holder.transform == transform || ct.IsChildOf(holder.transform)) return;
            Transform wt = holder.transform;

            // The weapon turns with the view kick around the eye, so the sights stay on it while aiming.
            Quaternion turn = ct.rotation * Quaternion.Inverse(before);
            wt.position = eye + turn * (wt.position - eye);
            wt.rotation = turn * wt.rotation;

            if (!fp)
            {
                // Over the shoulder: just the push back and a little muzzle flip on the model.
                wt.position -= ct.forward * back * 0.6f;
                wt.rotation = Quaternion.AngleAxis(-flip * 0.5f, ct.right) * wt.rotation;
                WeaponPose.SyncHands(holder);
                return;
            }

            // First person: push into the shoulder, muzzle flip, sway lag, step bob and breathing.
            float pushBack = aiming ? Mathf.Min(back * 0.35f, 0.015f) : back;
            float flipNow = flip * (aiming ? 0.18f : 1f);
            float t = Time.time;
            Vector3 offset = -ct.forward * pushBack
                + ct.right * (Mathf.Sin(bobPhase) * 0.011f * bobScale + sway.x * 0.004f)
                + ct.up * (Mathf.Sin(bobPhase * 2f) * 0.007f * bobScale - Mathf.Abs(Mathf.Sin(bobPhase)) * 0.004f * bobScale + sway.y * 0.004f
                           + Mathf.Sin(t * 1.3f) * 0.0012f * breath);
            Vector3 pivot = wt.position;
            wt.position = pivot + offset;
            Quaternion lag = Quaternion.AngleAxis(-sway.x, ct.up) * Quaternion.AngleAxis(sway.y + -flipNow + Mathf.Sin(t * 1.1f) * 0.12f * breath, ct.right)
                * Quaternion.AngleAxis(-sway.x * 0.6f + Mathf.Sin(bobPhase) * 1.2f * bobScale, ct.forward);
            wt.rotation = lag * wt.rotation;
            WeaponPose.SyncHands(holder);   // the hands follow the kick and sway
        }

        void Kick(Weapon w, bool aiming)
        {
            Profile p = For(w.hold);
            float motion = Mathf.Lerp(0.5f, 1f, GameSettings.WeaponMotion);
            float steady = aiming ? 0.75f : 1f;
            if (soldier.IsProne) steady *= 0.55f;
            else if (soldier.IsCrouching) steady *= 0.8f;
            float s = steady * motion;
            kickTarget += new Vector3(p.pitch * Random.Range(0.85f, 1.1f), Random.Range(-p.yaw, p.yaw), Random.Range(-p.roll, p.roll)) * s;
            kickTarget.x = Mathf.Min(kickTarget.x, p.pitch * 2.6f * s);
            kickTarget.y = Mathf.Clamp(kickTarget.y, -p.yaw * 2.5f, p.yaw * 2.5f);
            kickTarget.z = Mathf.Clamp(kickTarget.z, -p.roll * 2f, p.roll * 2f);
            backTarget = Mathf.Min(backTarget + p.back * s, p.back * 1.8f);
            flipTarget = Mathf.Min(flipTarget + p.flip * s, p.flip * 2f);
            recover = p.recover;
            Shots++;
        }

        // ---------------------------------------------------------------- drawing

        void OnGUI()
        {
            GUI.depth = 2;   // under the HUD panels and text, over the world
            if (Event.current.type != EventType.Repaint || !Active() || white == null) return;
            Color keep = GUI.color;

            if (suppression > 0.01f && vignette != null)
            {
                GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(suppression * 0.55f));
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), vignette, ScaleMode.StretchToFill);
            }

            Crosshair ch = FindCrosshair();
            bool gameDrawsMarker = ch != null && ch.enabled;
            float now = Time.time;
            if (!gameDrawsMarker && (now < hitUntil || now < killUntil))
            {
                bool kill = now < killUntil;
                float life = kill ? (killUntil - now) / 0.5f : (hitUntil - now) / 0.22f;
                float s = Screen.height / 1080f;
                float gap = (kill ? 9f : 7f) * s, len = (kill ? 13f : 9f) * s, thick = Mathf.Max(2f, (kill ? 3f : 2.2f) * s);
                Color c = kill ? new Color(0.95f, 0.15f, 0.1f, Mathf.Clamp01(life * 1.4f)) : new Color(1f, 1f, 1f, Mathf.Clamp01(life * 1.6f));
                DrawCross(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), gap, len, thick, c);
            }
            GUI.color = keep;
        }

        Crosshair crosshair;
        Crosshair FindCrosshair()
        {
            if (crosshair == null) crosshair = FindAnyObjectByType<Crosshair>(FindObjectsInactive.Include);
            return crosshair;
        }

        static void DrawCross(Vector2 c, float gap, float len, float thick, Color colour)
        {
            Matrix4x4 keep = GUI.matrix;
            for (int i = 0; i < 4; i++)
            {
                GUI.matrix = keep;
                GUIUtility.RotateAroundPivot(45f + 90f * i, c);
                GUI.color = new Color(0f, 0f, 0f, colour.a * 0.55f);
                GUI.DrawTexture(new Rect(c.x + gap - 1f, c.y - thick * 0.5f - 1f, len + 2f, thick + 2f), white);
                GUI.color = colour;
                GUI.DrawTexture(new Rect(c.x + gap, c.y - thick * 0.5f, len, thick), white);
            }
            GUI.matrix = keep;
        }

        // ---------------------------------------------------------------- generated assets

        const int Rate = 44100;

        /// <summary>Hit: a short bright tick. Kill: the tick plus a lower knock just after it.</summary>
        static AudioClip MakeTick(bool kill)
        {
            int n = (int)(Rate * (kill ? 0.16f : 0.06f));
            float[] d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float tick = (Mathf.Sin(2f * Mathf.PI * 2900f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 4300f * t) * 0.25f) * Mathf.Exp(-t * 95f);
                float s = tick;
                if (kill)
                {
                    float u = t - 0.045f;
                    if (u > 0f) s += Mathf.Sin(2f * Mathf.PI * 820f * u) * Mathf.Exp(-u * 38f) * 0.75f + Mathf.Sin(2f * Mathf.PI * 1640f * u) * Mathf.Exp(-u * 60f) * 0.2f;
                }
                d[i] = Mathf.Clamp(s, -1f, 1f) * 0.8f;
            }
            AudioClip c = AudioClip.Create(kill ? "stans_kill_marker" : "stans_hit_marker", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>
        /// A supersonic round passing close: a sharp N-shaped pressure snap, then a short hiss as it
        /// tears past, and a faint slap back from the surroundings.
        /// </summary>
        static AudioClip MakeCrack(int seed)
        {
            System.Random rng = new System.Random(seed);
            int n = (int)(Rate * 0.32f);
            float[] d = new float[n];
            float hp = 0f, prev = 0f, lp = 0f;
            float nWave = 0.0009f + (float)rng.NextDouble() * 0.0004f;   // length of the snap
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                if (t < nWave) s += 1f - 2f * (t / nWave);                 // N-wave: up, ramp down, back up
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                hp = 0.82f * (hp + noise - prev);                          // high-passed hiss
                prev = noise;
                s += hp * 0.55f * Mathf.Exp(-t * 55f) * Mathf.Clamp01(t / 0.0015f);
                float u = t - 0.07f;                                       // slap back
                if (u > 0f)
                {
                    lp += (noise - lp) * 0.25f;
                    s += lp * 0.35f * Mathf.Exp(-u * 22f);
                }
                d[i] = Mathf.Clamp(s * 0.9f, -1f, 1f);
            }
            AudioClip c = AudioClip.Create("stans_bullet_crack_" + seed, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static Texture2D MakeVignette(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half, dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.3f, r))));
                }
            tex.Apply();
            return tex;
        }
    }
}
