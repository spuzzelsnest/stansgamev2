using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>
    /// The network side of a drivable truck (NetTruck, multiplayer only). The truck asks it for the seat and sends
    /// the driver's keys to the host; it hands back who is driving and how far the front wheels are turned.
    /// </summary>
    public interface ITruckLink
    {
        bool Occupied { get; }
        bool LocalIsDriver { get; }
        bool TakenOver { get; }
        float Steer { get; }
        /// <summary>Host: take the seat for this player, if it is free.</summary>
        bool ServerClaim(ulong clientId);
        void ServerRelease();
        /// <summary>Client: ask the host for the seat / give it up / send the keys.</summary>
        void RequestEnter();
        void RequestExit();
        void SendInput(float throttle, float steer, bool handbrake);
        /// <summary>Sound the horn on every machine.</summary>
        void SendHorn();
    }

    /// <summary>
    /// A lorry anyone can drive (Opel Blitz): walk up to it and press E. Third-person chase camera (the mouse looks
    /// round, the view swings back behind the lorry when you drive on), W/S accelerate and brake/reverse, A/D steer,
    /// Space handbrake, E get out by the driver's door.
    ///
    /// It steers like a real lorry: the front wheels turn (the inner one a little more than the outer, as with real
    /// steering geometry) and the lorry pivots round its rear axle, so it needs room to turn and swings its nose wide.
    /// Less lock at speed. It rolls over the ground under its four wheels (pitching and rolling with the road, over
    /// the bridge deck), drops off edges, rolls back on hills without the handbrake, and stops dead against walls,
    /// trees, other vehicles and people instead of passing through them.
    ///
    /// Until someone climbs in, an AmbientVehicle on the same object drives it on its route (or leaves it where the
    /// alarm stopped it); from the first time it is driven it belongs to the players.
    ///
    /// Multiplayer: the host moves the lorry for everyone (it is a network object). A client who gets in sends his
    /// keys to the host and watches the lorry come back over the network; the steering angle comes back as well,
    /// so the front wheels turn on every screen. The driver's soldier rides along in the cab out of sight.
    /// </summary>
    public class TruckController : MonoBehaviour
    {
        [Header("Drive")]
        public string displayName = "Opel Blitz";
        public float maxForwardSpeed = 16f;   // ~58 km/h: plenty on these roads
        public float maxReverseSpeed = 4.5f;
        public float acceleration = 3.2f;
        public float braking = 9f;
        public float coasting = 1.3f;
        [Tooltip("Front wheel lock at a standstill (degrees). At full speed it has about half of it.")]
        public float maxSteer = 34f;
        public float steerSpeed = 75f;        // degrees a second at the wheels
        [Tooltip("The highest step (m) a wheel can climb: a kerb, not a wall.")]
        public float stepHeight = 0.55f;

        [Header("Parts")]
        public Transform[] wheels;
        public float wheelRadius = 0.44f;
        public Light[] lamps;

        [Tooltip("A recording in Resources/Audio for the engine (car_engine for the Kübelwagen); empty: the lorry engine.")]
        public string engineSound = "";

        [Header("Camera")]
        public float cameraDistance = 9.5f;
        public float cameraHeight = 2.4f;
        public float mouseSensitivity = 0.1f;

        /// <summary>Set by NetTruck when a network session is running.</summary>
        [System.NonSerialized] public ITruckLink link;

        public bool Occupied => link != null ? link.Occupied : localDriver != null;
        public bool TakenOver => link != null ? link.TakenOver : takenOver;
        /// <summary>The front wheels' angle (degrees, + = right).</summary>
        public float SteerAngle => link != null && !Net.Simulates ? link.Steer : steer;
        public float Speed => speed;
        public bool DrivenByMe => localDriver != null;

        SoldierController localDriver, pending;
        float pendingUntil;
        bool takenOver;
        AmbientVehicle ambient;
        BoxCollider box;
        Rigidbody rb;
        AudioSource engine;

        // the lorry's own state (host / offline)
        float speed, steer, yaw, fallSpeed;
        Vector3 pos;
        bool simStarted;
        float remoteThrottle, remoteSteer, remoteInputAt; bool remoteHandbrake = true;

        // wheels
        struct Wheel { public Transform t; public Quaternion rest; public Vector3 up, right; public float x, z; public bool front; public float spin; }
        Wheel[] ws;
        float wheelBase = 3.6f, frontZ = 1.8f, rearZ = -1.8f, halfTrack = 0.85f;
        Vector3 lastPos;

        // camera
        float camYaw, camPitch = 12f, lastLook;
        float hintUntil, enteredAt, seenSpeed;
        bool recordedEngine;
        AudioSource horn;

        /// <summary>The horn sounds here (called on every machine in co-op).</summary>
        public void PlayHorn() { if (horn && horn.clip && !horn.isPlaying) horn.Play(); }

        static readonly List<TruckController> all = new List<TruckController>();
        public static IReadOnlyList<TruckController> All => all;
        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        void Awake()
        {
            ambient = GetComponent<AmbientVehicle>();
            box = GetComponent<BoxCollider>();
            rb = GetComponent<Rigidbody>();
            if (rb) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }   // moved by this script, not pushed about
            engine = gameObject.AddComponent<AudioSource>();
            engine.clip = string.IsNullOrEmpty(engineSound) ? Sfx.TruckEngine : (Sfx.Recorded(engineSound) ?? Sfx.TruckEngine);
            recordedEngine = engine.clip != Sfx.Engine;
            engine.loop = true; engine.spatialBlend = 1f; engine.playOnAwake = false;
            horn = gameObject.AddComponent<AudioSource>();
            horn.clip = Sfx.TruckHorn; horn.spatialBlend = 1f; horn.playOnAwake = false; horn.minDistance = 8f; horn.maxDistance = 300f;
            engine.minDistance = 6f; engine.maxDistance = 220f; engine.volume = 0.6f; engine.pitch = 1.1f;
            SetupWheels();
            lastPos = transform.position;
        }

        void SetupWheels()
        {
            var list = new List<Wheel>();
            if (wheels != null)
                foreach (var t in wheels)
                {
                    if (!t) continue;
                    Vector3 local = transform.InverseTransformPoint(t.position);
                    var p = t.parent ? t.parent : transform;
                    list.Add(new Wheel { t = t, rest = t.localRotation, x = local.x, z = local.z,
                                         up = p.InverseTransformDirection(transform.up).normalized,
                                         right = p.InverseTransformDirection(transform.right).normalized });
                }
            if (list.Count >= 2)
            {
                float zMin = float.MaxValue, zMax = float.MinValue, xMax = 0f;
                foreach (var w in list) { zMin = Mathf.Min(zMin, w.z); zMax = Mathf.Max(zMax, w.z); xMax = Mathf.Max(xMax, Mathf.Abs(w.x)); }
                float mid = (zMin + zMax) * 0.5f;
                for (int i = 0; i < list.Count; i++) { var w = list[i]; w.front = w.z > mid; list[i] = w; }
                frontZ = zMax; rearZ = zMin; wheelBase = Mathf.Max(1.5f, zMax - zMin); halfTrack = Mathf.Max(0.5f, xMax);
            }
            else if (box) { frontZ = box.center.z + box.size.z * 0.3f; rearZ = box.center.z - box.size.z * 0.3f; wheelBase = frontZ - rearZ; halfTrack = box.size.x * 0.4f; }
            ws = list.ToArray();
        }

        /// <summary>When the players take over, the wheels go back to the model's own straight-ahead pose (kept from
        /// Awake). Never take the pose they happen to be in: if they were still turned, the lorry would carry that
        /// steering angle for good, and the wheels would wobble as they rolled.</summary>
        void RestWheels() { if (ws != null) for (int i = 0; i < ws.Length; i++) ws[i].spin = 0f; }

        // ------------------------------------------------------------------ getting in and out

        /// <summary>Can this soldier get in now? (Not while someone else is driving, nor while it's parked off the map.)</summary>
        public bool CanEnter => !Occupied && pending == null && transform.position.y > -AmbientVehicle.HiddenDepth * 0.5f;

        public void Enter(SoldierController soldier)
        {
            if (!soldier || !CanEnter) return;
            if (link != null && Net.IsRemoteClient) { pending = soldier; pendingUntil = Time.time + 2f; link.RequestEnter(); return; }
            if (link != null && !link.ServerClaim(Net.LocalClientId)) return;
            if (link == null) takenOver = true;
            Begin(soldier);
        }

        void Begin(SoldierController soldier)
        {
            pending = null;
            localDriver = soldier;
            soldier.SetControlActive(false);
            soldier.InVehicle = true;
            Cursor.lockState = CursorLockMode.Locked;
            camYaw = transform.eulerAngles.y; camPitch = 12f; lastLook = -10f;
            hintUntil = Time.time + 8f; enteredAt = Time.time;
            TakeOverFromAmbient();
        }

        public void Exit()
        {
            if (!localDriver) return;
            var s = localDriver;
            localDriver = null;
            s.InVehicle = false;
            s.SetControlActive(true, ExitSpot());
            if (link != null) { if (Net.IsRemoteClient) link.RequestExit(); else link.ServerRelease(); }
        }

        /// <summary>Out by the driver's door (on the left of a German lorry), or the other side, or behind if those are blocked.</summary>
        Vector3 ExitSpot()
        {
            float side = (box ? box.size.x * 0.5f : halfTrack) + 0.8f;
            float cabZ = frontZ - 0.8f;
            var tries = new[] { new Vector3(-side, 0f, cabZ), new Vector3(side, 0f, cabZ), new Vector3(0f, 0f, (box ? box.center.z - box.size.z * 0.5f : rearZ) - 1.2f), new Vector3(-side, 0f, rearZ) };
            foreach (var t in tries)
            {
                Vector3 p = transform.TransformPoint(t);
                p.y = GroundBelow(p + Vector3.up * 3f, 8f, p.y);
                if (!Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.5f, 0.35f, ~0, QueryTriggerInteraction.Ignore))
                    return p + Vector3.up * 0.1f;
            }
            return transform.position + Vector3.up * ((box ? box.center.y + box.size.y * 0.5f : 2.5f) + 0.2f);   // on the roof, if it's wedged in
        }

        void TakeOverFromAmbient()
        {
            if (ambient && ambient.enabled) ambient.Release();
            RestWheels();
            pos = transform.position; yaw = transform.eulerAngles.y; speed = 0f; steer = 0f; fallSpeed = 0f;
            simStarted = true;
        }

        /// <summary>Host: a client's keys.</summary>
        public void ServerRemoteInput(float throttle, float steerIn, bool handbrake)
        {
            remoteThrottle = Mathf.Clamp(throttle, -1f, 1f); remoteSteer = Mathf.Clamp(steerIn, -1f, 1f); remoteHandbrake = handbrake;
            remoteInputAt = Time.time;
        }

        /// <summary>Host: a client has taken the seat.</summary>
        public void ServerTakeOver() { if (!simStarted || (ambient && ambient.enabled)) TakeOverFromAmbient(); }

        // ------------------------------------------------------------------ every frame

        float sendAt;

        void Update()
        {
            // a client waiting for the host to give him the seat
            if (pending)
            {
                if (link != null && link.LocalIsDriver) Begin(pending);
                else if (Time.time > pendingUntil) pending = null;
            }
            if (localDriver)
            {
                var h = localDriver.GetComponent<Health>();
                if (h && h.IsDead) { var s = localDriver; localDriver = null; s.InVehicle = false; if (link != null) { if (Net.IsRemoteClient) link.RequestExit(); else link.ServerRelease(); } }
                else if (link != null && Net.IsRemoteClient && !link.LocalIsDriver && !pending) { var s = localDriver; localDriver = null; s.InVehicle = false; s.SetControlActive(true, ExitSpot()); }   // the host took it back (it left)
                else if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame && Time.time > enteredAt + 0.4f) { Exit(); }   // not the press that got him in
                else if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame && horn && horn.clip && !horn.isPlaying)
                {
                    // H: the horn - in co-op the host plays it on every machine
                    if (link != null) link.SendHorn(); else PlayHorn();
                }
            }

            if (TakenOver && ambient && ambient.enabled) { ambient.Release(); RestWheels(); }

            float th = 0f, st = 0f; bool hb = false;
            if (localDriver) ReadKeys(out th, out st, out hb);

            if (link != null && Net.IsRemoteClient)
            {
                if (localDriver && Time.time >= sendAt) { sendAt = Time.time + 1f / 30f; link.SendInput(th, st, hb); }
                return;   // the host moves it
            }
            if (!Net.Simulates || !TakenOver) return;
            if (!simStarted) TakeOverFromAmbient();

            if (!localDriver)
            {
                bool remote = link != null && link.Occupied && Time.time - remoteInputAt < 0.6f;
                th = remote ? remoteThrottle : 0f; st = remote ? remoteSteer : 0f; hb = remote ? remoteHandbrake : true;
            }
            Drive(th, st, hb, Mathf.Min(Time.deltaTime, 0.05f));
        }

        void ReadKeys(out float throttle, out float steering, out bool handbrake)
        {
            throttle = 0f; steering = 0f; handbrake = false;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steering += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steering -= 1f;
            handbrake = kb.spaceKey.isPressed;
        }

        // ------------------------------------------------------------------ driving (host / offline)

        readonly Collider[] overlap = new Collider[24];
        readonly HashSet<Collider> touching = new HashSet<Collider>();

        void Drive(float throttle, float steerIn, bool handbrake, float dt)
        {
            if (dt <= 0f) return;
            // speed: W pulls away (less hard near the top), S brakes and then backs up, nothing: it coasts down
            if (handbrake) speed = Mathf.MoveTowards(speed, 0f, braking * dt);
            else if (throttle > 0.1f)
                speed = speed < -0.05f ? Mathf.MoveTowards(speed, 0f, braking * dt)
                                       : Mathf.MoveTowards(speed, maxForwardSpeed, acceleration * (1f - 0.55f * speed / maxForwardSpeed) * dt);
            else if (throttle < -0.1f)
                speed = speed > 0.3f ? Mathf.MoveTowards(speed, 0f, braking * dt) : Mathf.MoveTowards(speed, -maxReverseSpeed, acceleration * 0.7f * dt);
            else speed = Mathf.MoveTowards(speed, 0f, coasting * dt);

            // steering: the wheels take a moment to turn and come back; less lock the faster it goes
            float lockNow = maxSteer * Mathf.Lerp(1f, 0.45f, Mathf.Abs(speed) / maxForwardSpeed);
            float target = steerIn * lockNow;
            steer = Mathf.MoveTowards(steer, target, (Mathf.Abs(target) < Mathf.Abs(steer) ? steerSpeed * 1.4f : steerSpeed) * dt);

            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 fwd = yawRot * Vector3.forward;

            // hills: it rolls back unless held
            float slope = transform.forward.y;
            if (!handbrake && Mathf.Abs(throttle) < 0.1f && Mathf.Abs(slope) > 0.04f) speed -= 9.81f * slope * 0.6f * dt;
            else if (handbrake && Mathf.Abs(speed) < 0.2f) speed = 0f;
            if (Mathf.Abs(speed) < 0.01f && Mathf.Abs(throttle) < 0.1f) speed = 0f;

            // round the rear axle (bicycle model): the back wheels follow, the nose swings
            float newYaw = yaw;
            Vector3 rear = pos + fwd * rearZ;
            rear += fwd * speed * dt;
            if (Mathf.Abs(steer) > 0.01f) newYaw += speed / wheelBase * Mathf.Tan(steer * Mathf.Deg2Rad) * Mathf.Rad2Deg * dt;
            Vector3 newFwd = Quaternion.Euler(0f, newYaw, 0f) * Vector3.forward;
            Vector3 newPos = rear - newFwd * rearZ;
            newPos.y = pos.y;

            // on the ground under the wheels: pitch and roll from front/back and left/right
            Vector3 right = Vector3.Cross(Vector3.up, newFwd);
            float reach = pos.y + stepHeight;
            Vector3 f = GroundAt(newPos + newFwd * frontZ, reach), b = GroundAt(newPos + newFwd * rearZ, reach);
            Vector3 l = GroundAt(newPos + newFwd * ((frontZ + rearZ) * 0.5f) - right * halfTrack, reach);
            Vector3 r = GroundAt(newPos + newFwd * ((frontZ + rearZ) * 0.5f) + right * halfTrack, reach);
            float groundY = (f.y + b.y + l.y + r.y) * 0.25f;
            Vector3 along = f - b, across = r - l;
            Vector3 up = Vector3.Cross(along.normalized, across.normalized).normalized;
            if (up.y < 0.5f) up = Vector3.up;
            float y;
            if (groundY < pos.y - 0.05f) { fallSpeed += 9.81f * dt; y = Mathf.Max(groundY, pos.y - fallSpeed * dt); }
            else { fallSpeed = 0f; y = groundY; }
            newPos.y = y;
            Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(newFwd, up).normalized, up);

            // walls, trees, other vehicles, people: stop against them instead of passing through
            if (Blocked(newPos, rot))
            {
                if (Mathf.Abs(speed) > 3f && engine) engine.pitch *= 0.8f;
                speed = 0f;
                return;
            }

            pos = newPos; yaw = newYaw;
            transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Would the body at this pose run into something it isn't already touching? (Ground and kerbs below the
        /// step height don't count; whatever it was already wedged against doesn't stop it backing off.)</summary>
        bool Blocked(Vector3 p, Quaternion rot)
        {
            if (!box) return false;
            float lift = stepHeight + 0.15f;
            Vector3 half = box.size * 0.5f - new Vector3(0.05f, lift * 0.5f, 0.05f);
            if (half.y < 0.2f) return false;
            Vector3 c = box.center + new Vector3(0f, lift * 0.5f, 0f);
            touching.Clear();
            int n = Physics.OverlapBoxNonAlloc(transform.TransformPoint(c), half, overlap, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) touching.Add(overlap[i]);
            n = Physics.OverlapBoxNonAlloc(p + rot * c, half, overlap, rot, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = overlap[i];
                if (col.transform.IsChildOf(transform) || touching.Contains(col)) continue;
                if (col is TerrainCollider) continue;
                if (localDriver && col.transform.IsChildOf(localDriver.transform)) continue;
                if (col is CharacterController cc && !cc.enabled) continue;
                return true;
            }
            return false;
        }

        readonly RaycastHit[] hits = new RaycastHit[12];

        /// <summary>The surface a wheel would be on: the highest one it could climb onto (not a roof or a branch overhead).</summary>
        Vector3 GroundAt(Vector3 p, float reach) => new Vector3(p.x, GroundBelow(new Vector3(p.x, reach + 0.05f, p.z), 30f, pos.y), p.z);

        float GroundBelow(Vector3 from, float depth, float fallback)
        {
            int n = Physics.RaycastNonAlloc(from, Vector3.down, hits, depth, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c.transform.IsChildOf(transform) || c is CharacterController) continue;
                if (c.attachedRigidbody && !c.attachedRigidbody.isKinematic) continue;
                if (hits[i].point.y > best) best = hits[i].point.y;
            }
            return float.IsNegativeInfinity(best) ? fallback : best;
        }

        // ------------------------------------------------------------------ what everyone sees

        void LateUpdate()
        {
            bool visible = transform.position.y > -AmbientVehicle.HiddenDepth * 0.5f;
            if (TakenOver && ws != null)
            {
                // the wheels roll with the ground it covers, the front pair turns with the steering
                Vector3 moved = transform.position - lastPos;
                float dist = Vector3.Dot(moved, transform.forward);
                if (moved.sqrMagnitude > 400f) dist = 0f;
                float st = SteerAngle;
                float radius = Mathf.Abs(st) > 0.05f ? wheelBase / Mathf.Tan(Mathf.Abs(st) * Mathf.Deg2Rad) : 0f;
                for (int i = 0; i < ws.Length; i++)
                {
                    var w = ws[i];
                    if (!w.t) continue;
                    w.spin = Mathf.Repeat(w.spin + dist / wheelRadius * Mathf.Rad2Deg, 360f);
                    float a = 0f;
                    if (w.front && radius > 0f)
                    {
                        float s = Mathf.Sign(st);
                        a = s * Mathf.Atan(wheelBase / Mathf.Max(0.5f, radius - s * w.x)) * Mathf.Rad2Deg;   // the inner wheel turns more
                    }
                    w.t.localRotation = Quaternion.AngleAxis(a, w.up) * Quaternion.AngleAxis(w.spin, w.right) * w.rest;
                    ws[i] = w;
                }
                bool running = visible && Occupied;
                if (lamps != null) foreach (var lp in lamps) if (lp) lp.enabled = running;
                if (running && !engine.isPlaying) engine.Play(); else if (!running && engine.isPlaying) engine.Stop();
                if (running)
                {
                    float k = Mathf.Clamp01(seenSpeed / maxForwardSpeed);
                    // a recording already sounds like the engine: only a little higher as it speeds up; the synthesised one needs more
                    engine.pitch = Mathf.Lerp(engine.pitch, recordedEngine ? 0.9f + k * 0.4f : 0.95f + k * 0.75f, Time.deltaTime * 3f);
                    engine.volume = Mathf.Lerp(engine.volume, 0.5f + k * 0.4f, Time.deltaTime * 3f);
                }
            }
            float dtv = Mathf.Max(0.001f, Time.deltaTime);
            Vector3 mv = transform.position - lastPos;
            seenSpeed = mv.sqrMagnitude > 400f ? 0f : Mathf.Lerp(seenSpeed, mv.magnitude / dtv, Mathf.Min(1f, dtv * 6f));   // how fast it's going, as this machine sees it
            lastPos = transform.position;

            if (localDriver) { RideAlong(); ChaseCamera(); }
        }

        /// <summary>The driver's soldier sits in the cab (out of sight): enemies aim at the lorry, and the soldier is
        /// where the lorry is when he gets out.</summary>
        void RideAlong()
        {
            Vector3 seat = transform.TransformPoint(new Vector3(-0.45f, 0.4f, frontZ - 0.9f));
            localDriver.transform.SetPositionAndRotation(seat, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        }

        void ChaseCamera()
        {
            var cam = localDriver.cam ? localDriver.cam : Camera.main;
            if (!cam) return;
            Vector2 look = GameInput.LookDelta * mouseSensitivity;
            if (look.sqrMagnitude > 0.0004f) lastLook = Time.time;
            camYaw += look.x;
            camPitch = Mathf.Clamp(camPitch - look.y, -5f, 50f);
            // after a moment without the mouse, swing back behind the lorry once it's moving
            float moving = Mathf.Clamp01((seenSpeed - 0.5f) / 3f);
            if (Time.time - lastLook > 1.5f && moving > 0f)
            {
                camYaw = Mathf.LerpAngle(camYaw, transform.eulerAngles.y, Time.deltaTime * 2.2f * moving);
                camPitch = Mathf.Lerp(camPitch, 12f, Time.deltaTime * 1.5f);
            }
            Quaternion rot = Quaternion.Euler(camPitch, camYaw, 0f);
            Vector3 pivot = transform.position + Vector3.up * cameraHeight;
            Vector3 desired = pivot + rot * new Vector3(0f, 0f, -cameraDistance);
            Vector3 dir = desired - pivot;
            int n = Physics.SphereCastNonAlloc(pivot, 0.25f, dir.normalized, hits, dir.magnitude, ~0, QueryTriggerInteraction.Ignore);
            float best = dir.magnitude;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c.transform.IsChildOf(transform) || c.transform.IsChildOf(localDriver.transform) || c is CharacterController) continue;
                if (hits[i].distance > 0f && hits[i].distance < best) best = hits[i].distance;
            }
            desired = pivot + dir.normalized * Mathf.Max(1.2f, best);
            cam.transform.SetPositionAndRotation(desired, rot);
            if (cam.nearClipPlane < 0.1f) cam.nearClipPlane = 0.1f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 60f + Mathf.Clamp01(seenSpeed / maxForwardSpeed) * 8f, Time.deltaTime * 4f);
        }

        void OnGUI()
        {
            if (!localDriver || Time.time > hintUntil) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height - 120, Screen.width, 26), $"{displayName}:  W / S  drive and brake  ·  A / D  steer  ·  Space  handbrake  ·  H  horn  ·  E  get out", style);
        }

        void OnDestroy()
        {
            if (localDriver) { localDriver.InVehicle = false; localDriver.SetControlActive(true, transform.position + Vector3.up * 3f); }
        }
    }
}
