using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>
    /// Arcade-but-weighty tracked vehicle (Panzer IV, Churchill). W/S throttle, A/D pivot steer,
    /// mouse traverses the turret and elevates the gun, left mouse fires the main gun,
    /// E gets out. Needs a Rigidbody with mass ~20000 and a box/convex collider on the hull.
    /// Driven by TankAI it rolls along the ground instead of being pushed by physics: it gathers speed slowly and
    /// brakes, sits on the ground under all four corners (pitching and rolling with the road), the nose lifts as it
    /// pulls away and dips when it stops, and the hull shivers with the engine. The engine and the tracks are heard
    /// louder and higher the faster it goes; after each shot the loader slams the next round into the breech.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TankController : MonoBehaviour
    {
        [Header("Drive")]
        public float maxForwardSpeed = 11f;   // m/s (~40 km/h road speed for a Pz IV)
        public float maxReverseSpeed = 3.5f;
        public float acceleration = 3f;
        public float turnRate = 32f;          // deg/s at standstill
        public Transform[] groundProbes;      // a few points under the tracks; optional

        [Header("Turret")]
        public Transform turret;              // yaws
        public Transform gun;                 // pitches
        public Transform muzzle;
        public float traverseSpeed = 18f;     // deg/s (hydraulic traverse was slow)
        [Tooltip("How far the gun turns either side of straight ahead (degrees); 0 = a full turret. A self-propelled gun in a fixed casemate has only a few degrees, so the whole vehicle has to turn.")]
        public float maxTraverse = 0f;
        public float elevateSpeed = 8f;
        public float minElevation = -8f, maxElevation = 20f;

        [Header("Main gun")]
        public Shell shellPrefab;
        public float shellSpeed = 740f;
        public float reloadTime = 6f;
        [Tooltip("Automatic weapons (the 2 cm Flakvierling, the M45 quad .50): fires while the button is held, reloadTime being the time between rounds.")]
        public bool automatic;
        [Tooltip("Rounds before a magazine or belt change (0 = no magazine, one round per reload).")]
        public int magazineSize;
        public float magazineReload = 6f;
        [Tooltip("The barrels fired in turn (quad mounts); empty = the muzzle.")]
        public Transform[] muzzles;
        [Tooltip("Kick on the hull per round (N s); below 0 = from the shell speed, as for a tank gun.")]
        public float recoil = -1f;
        [Tooltip("Muzzle flashes, hidden until a round goes: one per barrel (as muzzles), or one at the muzzle.")]
        public GameObject[] flashes;
        public AudioSource gunAudio;
        public AudioClip fireClip;
        [Tooltip("Resources/Audio clip names for this vehicle (empty: the default sounds).")]
        public string fireSound = "", engineSound = "tank_engine", trackSound = "tank_tracks", reloadSound = "tank_reload";
        public ParticleSystem muzzleBlast;

        [Header("Camera")]
        public Vector3 cameraOffset = new Vector3(0f, 4.2f, -9f);
        public float mouseSensitivity = 0.1f;

        [Header("AI")]
        public bool aiControlled;
        [HideInInspector] public float aiThrottle, aiSteer;
        [Tooltip("AI driving: how fast it gathers speed and how hard it brakes (m/s per second).")]
        public float aiAcceleration = 0.9f, aiBraking = 2.2f;
        /// <summary>Forward speed in m/s (AI driving).</summary>
        public float Speed => aiSpeed;
        /// <summary>Can the gun be laid on this point without turning the vehicle?</summary>
        public bool CanTraverseTo(Vector3 worldPoint)
        {
            if (maxTraverse <= 0f) return true;
            Vector3 to = worldPoint - transform.position;
            return Mathf.Abs(Vector3.SignedAngle(transform.forward, new Vector3(to.x, 0f, to.z), Vector3.up)) < maxTraverse - 2f;
        }
        /// <summary>Is the gun laid on its target (it has caught up with the aim)?</summary>
        public bool GunLaid(Vector3 worldPoint, float tolerance)
        {
            if (!muzzle) return false;
            return Vector3.Angle(muzzle.forward, worldPoint - muzzle.position) < tolerance;
        }
        public bool Loaded => Time.time >= nextShot;

        public bool HasDriver => driver != null || aiControlled;
        public void AIAimAt(Vector3 worldPoint)
        {
            Vector3 to = worldPoint - (turret ? turret.position : transform.position);
            aimYaw = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z)).eulerAngles.y;
            float flat = new Vector2(to.x, to.z).magnitude;
            aimPitch = Mathf.Clamp(-Mathf.Atan2(to.y, flat) * Mathf.Rad2Deg, -maxElevation, -minElevation);
        }
        public bool AIFire() { if (Time.time < nextShot) return false; FireMainGun(); return true; }

        AudioSource engineAudio, trackAudio;
        AudioClip reloadClip;

        AudioSource Make3D(AudioClip clip, bool loop, float volume, float maxDistance)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip; src.loop = loop; src.volume = volume; src.playOnAwake = false;
            src.spatialBlend = 1f; src.minDistance = 6f; src.maxDistance = maxDistance; src.rolloffMode = AudioRolloffMode.Logarithmic; src.dopplerLevel = 0f;
            return src;
        }

        void EngineSound()
        {
            if (!engineAudio) return;
            bool running = HasDriver && (!TryGetComponent<Health>(out var h) || !h.IsDead);
            if (running && !engineAudio.isPlaying) { engineAudio.time = Random.Range(0f, engineAudio.clip ? engineAudio.clip.length * 0.9f : 0f); engineAudio.Play(); }
            if (!running && engineAudio.isPlaying) engineAudio.Stop();
            float speed = aiControlled && !driver ? Mathf.Abs(aiSpeed) : rb ? rb.linearVelocity.magnitude : 0f;
            float k = Mathf.Clamp01(speed / Mathf.Max(1f, maxForwardSpeed));
            float dt = Time.fixedDeltaTime;
            // the recording is of a tank on the move: idle a little lower and quieter, open up as it pulls away
            engineAudio.pitch = Mathf.Lerp(engineAudio.pitch, (customEngine ? 0.82f : 0.8f) + k * (customEngine ? 0.35f : 0.9f) + Mathf.Max(0f, aiPull) * 0.04f, dt * 2f);
            engineAudio.volume = Mathf.Lerp(engineAudio.volume, customEngine ? 0.55f + k * 0.45f : 0.6f, dt * 2f);
            if (trackAudio)
            {
                bool rolling = running || speed > 0.2f;
                float vol = Mathf.Clamp01(speed / 2.5f) * 0.9f;
                if (vol > 0.01f && !trackAudio.isPlaying) trackAudio.Play();
                if (vol <= 0.01f && trackAudio.isPlaying && trackAudio.volume < 0.02f) trackAudio.Stop();
                trackAudio.volume = Mathf.Lerp(trackAudio.volume, rolling ? vol : 0f, dt * 3f);
                trackAudio.pitch = 0.7f + k * 0.6f;
            }
        }
        bool customEngine;

        Rigidbody rb;
        SoldierController driver;
        float enteredAt;
        float throttle, aimYaw, aimPitch, nextShot, cycle = 1f;
        int barrel, roundsFired, lastFlash = -1;
        float flashOff;
        Camera cam;

        void Awake()
        {
            var fire = string.IsNullOrEmpty(fireSound) ? null : Resources.Load<AudioClip>("Audio/" + fireSound);
            if (!gunAudio) gunAudio = Make3D(fire ? fire : Sfx.GunClip("tank"), false, 1f, 1500f);
            if (!fireClip) fireClip = fire ? fire : Sfx.GunClip("tank");
            var engine = string.IsNullOrEmpty(engineSound) ? null : Resources.Load<AudioClip>("Audio/" + engineSound);
            customEngine = engine;
            engineAudio = Make3D(engine ? engine : Sfx.Engine, true, 0.6f, 300f);
            var tracks = string.IsNullOrEmpty(trackSound) ? null : Resources.Load<AudioClip>("Audio/" + trackSound);
            if (tracks) { trackAudio = Make3D(tracks, true, 0f, 160f); }
            reloadClip = string.IsNullOrEmpty(reloadSound) ? null : Resources.Load<AudioClip>("Audio/" + reloadSound);
            rb = GetComponent<Rigidbody>();
            rb.centerOfMass = new Vector3(0f, 0.6f, 0f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void Enter(SoldierController soldier)
        {
            if (aiControlled) return;
            driver = soldier; enteredAt = Time.time;
            soldier.SetControlActive(false);
            Debug.Log($"Stan's Game: {soldier.name} took the {name}");
            cam = soldier.cam;
            aimYaw = transform.eulerAngles.y;
            Cursor.lockState = CursorLockMode.Locked;
        }

        public void Exit()
        {
            if (!driver) return;
            Vector3 exit = transform.position - transform.right * 2.6f + Vector3.up * 0.5f;
            driver.SetControlActive(true, exit);
            driver = null;
            throttle = 0f;
        }

        void Update()
        {
            if (lastFlash >= 0 && Time.time > flashOff)
            {
                if (flashes != null && lastFlash < flashes.Length && flashes[lastFlash]) flashes[lastFlash].SetActive(false);
                lastFlash = -1;
            }
            if (!driver) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.eKey.wasPressedThisFrame && Time.time > enteredAt + 0.4f) { Exit(); return; }   // not the press that got him in (the order of Updates is not fixed)

            Vector2 look = GameInput.LookDelta * mouseSensitivity;
            aimYaw += look.x;
            aimPitch = Mathf.Clamp(aimPitch - look.y, -maxElevation, -minElevation);

            if (GameInput.MouseActive && (automatic ? mouse.leftButton.isPressed : mouse.leftButton.wasPressedThisFrame)) FireMainGun();
        }

        void FixedUpdate()
        {
            EngineSound();
            float target = 0f, steer = 0f;
            if (aiControlled && !driver)
            {
                target = Mathf.Clamp(aiThrottle, -1f, 1f) * (aiThrottle >= 0f ? maxForwardSpeed : maxReverseSpeed);
                steer = Mathf.Clamp(aiSteer, -1f, 1f);
            }
            else if (driver)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed) target = maxForwardSpeed;
                    if (kb.sKey.isPressed) target = -maxReverseSpeed;
                    if (kb.dKey.isPressed) steer += 1f;
                    if (kb.aKey.isPressed) steer -= 1f;
                }
            }
            if (aiControlled && !driver) { AIDrive(target, steer, Time.fixedDeltaTime); Aim(); return; }
            throttle = Mathf.MoveTowards(throttle, target, acceleration * Time.fixedDeltaTime);

            bool grounded = IsGrounded();
            if (grounded)
            {
                Vector3 fwd = transform.forward;
                Vector3 vel = rb.linearVelocity;
                float forwardSpeed = Vector3.Dot(vel, fwd);
                // tracks: kill sideways slip, push toward target speed
                Vector3 lateral = Vector3.Dot(vel, transform.right) * transform.right;
                rb.AddForce(-lateral * rb.mass * 4f * Time.fixedDeltaTime, ForceMode.Impulse);
                float speedError = throttle - forwardSpeed;
                rb.AddForce(fwd * speedError * rb.mass * 2.5f, ForceMode.Force);

                float turn = steer * turnRate * Mathf.Lerp(1f, 0.55f, Mathf.Abs(forwardSpeed) / maxForwardSpeed);
                if (forwardSpeed < -0.5f) turn = -turn;
                Quaternion dq = Quaternion.Euler(0f, turn * Time.fixedDeltaTime, 0f);
                rb.MoveRotation(rb.rotation * dq);
            }

            Aim();
        }

        void Aim()
        {
            // turret traverse toward camera yaw
            if (turret && (driver || aiControlled))
            {
                float localTarget = Mathf.DeltaAngle(transform.eulerAngles.y, aimYaw);
                if (maxTraverse > 0f) localTarget = Mathf.Clamp(localTarget, -maxTraverse, maxTraverse);
                float current = turret.localEulerAngles.y;
                float next = Mathf.MoveTowardsAngle(current, localTarget, traverseSpeed * Time.fixedDeltaTime);
                turret.localRotation = Quaternion.Euler(0f, next, 0f);
            }
            if (gun && (driver || aiControlled))
            {
                float current = gun.localEulerAngles.x;
                float next = Mathf.MoveTowardsAngle(current, aimPitch, elevateSpeed * Time.fixedDeltaTime);
                gun.localRotation = Quaternion.Euler(next, 0f, 0f);
            }
        }

        void LateUpdate()
        {
            if (!driver || !cam) return;
            Quaternion rot = Quaternion.Euler(aimPitch * 0.5f + 8f, aimYaw, 0f);
            Vector3 pivot = turret ? turret.position : transform.position;
            cam.transform.SetPositionAndRotation(pivot + rot * cameraOffset, rot);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, Mouse.current != null && Mouse.current.rightButton.isPressed ? 25f : 60f, Time.deltaTime * 8f);
        }

        bool IsGrounded()
        {
            if (groundProbes == null || groundProbes.Length == 0)
                return Physics.Raycast(transform.position + Vector3.up * 0.5f, -transform.up, 1.4f, ~0, QueryTriggerInteraction.Ignore);
            int hits = 0;
            foreach (var p in groundProbes)
                if (Physics.Raycast(p.position + transform.up * 0.3f, -transform.up, 0.8f, ~0, QueryTriggerInteraction.Ignore)) hits++;
            return hits >= groundProbes.Length / 2;
        }

        void FireMainGun()
        {
            if (Time.time < nextShot || !muzzle || !shellPrefab) return;
            int bi = muzzles != null && muzzles.Length > 0 ? barrel % muzzles.Length : 0;
            var mz = muzzles != null && muzzles.Length > 0 && muzzles[bi] ? muzzles[bi] : muzzle;
            barrel++;
            Flash(bi);
            bool change = magazineSize > 0 && ++roundsFired >= magazineSize;
            if (change) roundsFired = 0;
            cycle = change ? magazineReload : reloadTime;
            nextShot = Time.time + cycle;
            var shell = Instantiate(shellPrefab, mz.position, mz.rotation);
            // small arms and cannon scatter a little round the line of the bore
            Vector3 dir = automatic ? Quaternion.Euler(Random.Range(-0.25f, 0.25f), Random.Range(-0.25f, 0.25f), 0f) * mz.forward : mz.forward;
            Vector3 vel = dir * shellSpeed + rb.linearVelocity;
            shell.Launch(vel, gameObject);
            NetHooks.ProjectileLaunched?.Invoke(shellPrefab, mz.position, vel, gameObject);
            rb.AddForceAtPosition(-mz.forward * (recoil >= 0f ? recoil : shellSpeed * 40f), mz.position, ForceMode.Impulse);
            if (muzzleBlast) muzzleBlast.Play();
            if (gunAudio && fireClip) gunAudio.PlayOneShot(fireClip, automatic ? 0.6f : 1f);
            if (reloadClip && gunAudio && (!automatic || change)) StartCoroutine(Reload(Mathf.Min(2.2f, cycle * 0.4f)));
            NoiseEvents.Report(transform.position, 400f, gameObject);
            aiKick = 1f;
        }

        /// <summary>Shows the flash at this barrel for a moment (a different twist and size each round).</summary>
        void Flash(int i)
        {
            if (flashes == null || flashes.Length == 0) return;
            if (i >= flashes.Length) i = 0;
            if (lastFlash >= 0 && lastFlash < flashes.Length && flashes[lastFlash]) flashes[lastFlash].SetActive(false);
            var f = flashes[i];
            if (!f) return;
            f.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            f.transform.localScale = Vector3.one * Random.Range(0.8f, 1.2f);
            f.SetActive(true);
            lastFlash = i;
            flashOff = Time.time + (automatic ? 0.03f : 0.08f);
        }

        System.Collections.IEnumerator Reload(float after)
        {
            yield return new WaitForSeconds(after);
            if (gunAudio && reloadClip && (!TryGetComponent<Health>(out var h) || !h.IsDead)) gunAudio.PlayOneShot(reloadClip, 0.8f);
        }

        // ------------------------------------------------------------------ AI driving: rolled along the ground

        float aiSpeed, aiYaw, aiSway, aiPull, aiKick, aiBob;
        Vector3 aiPos; bool aiStarted;
        readonly RaycastHit[] groundHits = new RaycastHit[8];

        void AIDrive(float targetSpeed, float steer, float dt)
        {
            if (!rb.isKinematic) rb.isKinematic = true;   // driven, not pushed (so it can't stick on a kerb or bounce off the road)
            if (!aiStarted) { aiStarted = true; aiPos = transform.position; aiYaw = transform.eulerAngles.y; }
            bool dead = TryGetComponent<Health>(out var hp) && hp.IsDead;
            if (dead) { targetSpeed = 0f; steer = 0f; }
            // speed: slow to gather, quicker to brake (a 12-tonne hull on a petrol engine)
            float prev = aiSpeed;
            bool speedingUp = Mathf.Abs(targetSpeed) > Mathf.Abs(aiSpeed) && Mathf.Sign(targetSpeed) == Mathf.Sign(aiSpeed == 0f ? targetSpeed : aiSpeed);
            aiSpeed = Mathf.MoveTowards(aiSpeed, targetSpeed, (speedingUp ? aiAcceleration : aiBraking) * dt);
            float accel = (aiSpeed - prev) / dt;
            aiPull = Mathf.Lerp(aiPull, accel, 1f - Mathf.Exp(-3f * dt));
            // steering: the tracks can pivot it on the spot, but it turns wider the faster it goes
            float turn = steer * turnRate * Mathf.Lerp(1f, 0.45f, Mathf.Abs(aiSpeed) / Mathf.Max(1f, maxForwardSpeed));
            if (aiSpeed < -0.3f) turn = -turn;
            aiYaw += turn * dt;
            Vector3 fwd = Quaternion.Euler(0f, aiYaw, 0f) * Vector3.forward;
            aiPos += fwd * aiSpeed * dt;

            // on the ground under the four corners of the tracks
            var box = GetComponent<BoxCollider>();
            float halfL = box ? box.size.z * 0.42f : 2f, halfW = box ? box.size.x * 0.4f : 0.8f;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 f = Ground(aiPos + fwd * halfL), b = Ground(aiPos - fwd * halfL);
            Vector3 l = Ground(aiPos - right * halfW), r = Ground(aiPos + right * halfW);
            Vector3 along = (f - b).normalized, across = (r - l).normalized;
            Vector3 up = Vector3.Cross(along, across).normalized;
            if (up.y < 0.5f) up = Vector3.up;
            float y = (f.y + b.y + l.y + r.y) * 0.25f;
            aiPos.y = y;

            // the hull on its springs: the nose lifts as it pulls away and dips as it brakes, rocks back when the gun
            // fires, and shivers with the engine
            aiKick = Mathf.MoveTowards(aiKick, 0f, dt * 2.5f);
            float sway = Mathf.Clamp(-aiPull * 1.6f, -3f, 3f) - aiKick * 2.2f;
            aiSway = Mathf.Lerp(aiSway, sway, 1f - Mathf.Exp(-5f * dt));
            float t = Time.time;
            float shiver = dead ? 0f : (0.05f + Mathf.Abs(aiSpeed) * 0.03f) * Mathf.Sin(t * 41f) + Mathf.Abs(aiSpeed) * 0.05f * Mathf.Sin(t * 7.3f);
            aiBob = dead ? 0f : Mathf.Abs(aiSpeed) * 0.006f * Mathf.Sin(t * 11f);

            Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, up), up) * Quaternion.Euler(aiSway + shiver, 0f, shiver * 0.5f);
            rb.MovePosition(new Vector3(aiPos.x, y + 0.02f + aiBob, aiPos.z));
            rb.MoveRotation(rot);
        }

        /// <summary>The ground under a point (the terrain, roads, the bridge - nothing that moves, not this vehicle).</summary>
        Vector3 Ground(Vector3 p)
        {
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 4f, Vector3.down, groundHits, 12f, ~0, QueryTriggerInteraction.Ignore);
            // the highest surface it could have climbed onto (not a roof, a branch or a barrier pole overhead)
            float best = float.NegativeInfinity, lowest = float.PositiveInfinity, reach = aiPos.y + 1.2f;
            for (int i = 0; i < n; i++)
            {
                var c = groundHits[i].collider;
                if (c.transform.IsChildOf(transform) || c.attachedRigidbody || c is CharacterController) continue;
                float y = groundHits[i].point.y;
                if (y < lowest) lowest = y;
                if (y <= reach && y > best) best = y;
            }
            if (float.IsNegativeInfinity(best)) best = float.IsPositiveInfinity(lowest) ? aiPos.y : lowest;
            return new Vector3(p.x, best, p.z);
        }

        public float ReloadProgress => Mathf.Clamp01(1f - (nextShot - Time.time) / Mathf.Max(0.01f, cycle));
    }
}
