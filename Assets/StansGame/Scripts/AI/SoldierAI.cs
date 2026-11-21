using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Simple infantry AI for both sides. Finds the nearest visible enemy, moves toward a
    /// firing distance (or holds a post), and shoots in bursts with distance-based accuracy.
    /// Hearing gunfire makes sentries alert. No NavMesh needed (moves with CharacterController).
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(Health))]
    public class SoldierAI : MonoBehaviour, IDisableOnDeath
    {
        public enum Mode { HoldPosition, Advance, FollowPlayer }

        public Mode mode = Mode.HoldPosition;
        public Transform advanceTarget;
        public Weapon weapon;
        public float viewRange = 70f;
        public float nightViewRange = 35f;
        public float fieldOfView = 140f;
        public float preferredRange = 25f;
        public float moveSpeed = 3.0f;   // m/s, advancing at a jog in full kit
        [Range(0f, 1f)] public float skill = 0.5f;
        public bool startAlert = false;
        [Tooltip("Seconds a new target must be in view before the first shot (surprise, darkness).")]
        public float reactionTime = 2.5f;
        public Animator animator;

        [Header("The night of 5/6 June")]
        [Tooltip("Asleep in the bunkers and trenches until the alarm is raised (only the guards are awake).")]
        public bool sleepsUntilAlarm = false;

        /// <summary>True once the first glider is down: before that nobody on the ground knows anything is coming.</summary>
        public static bool GlidersDown { get; private set; }
        /// <summary>The garrison has been woken (the first shots, or a guard raising the alarm).</summary>
        public static bool Alarm { get; private set; }
        static float alarmTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetNight() { GlidersDown = false; Alarm = false; }

        public static void GliderLanded() => GlidersDown = true;

        /// <summary>Multiplayer, remote machines: the host's word on the night (gliders down, alarm raised).</summary>
        public static void SetFromHost(bool glidersDown, bool alarm)
        {
            GlidersDown = glidersDown;
            if (alarm && !Alarm) { Alarm = true; alarmTime = Time.time; }
        }

        public static void RaiseAlarm()
        {
            if (Alarm || !GlidersDown) return;
            Alarm = true; alarmTime = Time.time;
            Debug.Log("Stan's Game: the alarm is raised, the garrison wakes up");
        }

        bool asleep;
        float wakeAt = -1f;

        [Header("Moving about")]
        [Tooltip("Before the alarm: stroll around the post (sentries on their rounds). 0 = stand still.")]
        public float patrolRadius = 0f;
        [Tooltip("Before the alarm: walk these points in turn, round and round (a patrol). Overrides the patrol radius.")]
        public Vector3[] patrolRoute;
        [Tooltip("Seconds a patrol stands at each point of its route.")]
        public float patrolPause = 2f;
        int patrolIndex;
        public float walkSpeed = 1.4f;   // a sentry's stroll
        [Tooltip("In a fight: dash between firing positions within this distance of the post. 0 = stay put (MG nests, pillbox).")]
        public float combatRoam = 0f;
        public float runSpeed = 3.8f;    // a dash to the next cover, loaded
        [Header("Smoke")]
        [Tooltip("Smoke grenades he carries: thrown to cover an advance across open ground, or a retreat.")]
        public Shell smokeGrenade;
        public int smokeGrenades = 0;

        [Tooltip("Below this share of health, fall back away from the enemy.")]
        [Range(0f, 1f)] public float retreatAt = 0.3f;

        Health self;
        CharacterController cc;
        Health target;
        bool alert;
        float burstEnd, nextBurst, nextScan, vertical;
        Vector3 home;
        float targetSince;
        Vector3? moveGoal;
        float nextMoveDecision, pauseUntil;
        bool running;

        void OnEnable() { All.Add(this); self = GetComponent<Health>(); NoiseEvents.Noise += OnNoise; Weapon.ShotPassed += OnShot; SmokeCloud.Popped += OnSmoke; if (self) self.Damaged += OnDamaged; }
        void OnDisable() { All.Remove(this); NoiseEvents.Noise -= OnNoise; Weapon.ShotPassed -= OnShot; SmokeCloud.Popped -= OnSmoke; if (self) self.Damaged -= OnDamaged; if (cc && !cc.enabled && !self.IsDead) cc.enabled = true; }

        void Awake()
        {
            // older prefabs still carry the first, too-quick speeds
            if (runSpeed > 4f) runSpeed = 3.8f;
            if (moveSpeed > 3.1f) moveSpeed = 3.0f;
            cc = GetComponent<CharacterController>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!weapon) weapon = GetComponentInChildren<Weapon>(true);
            alert = startAlert;
            home = transform.position;
            asleep = sleepsUntilAlarm && !startAlert;
            if (!TryGetComponent(out stance)) { stance = gameObject.AddComponent<Stance>(); }
            stance.animator = animator;
        }

        Stance stance;
        StanceKind combatStance;
        /// <summary>Height to aim at on a soldier: chest height, lower when he kneels or lies down.</summary>
        static float AimHeight(Health h, float lo, float hi)
        {
            float f = h.TryGetComponent(out Stance st) ? st.HeightFactor : 1f;
            return Random.Range(lo, hi) * f;
        }

        void OnNoise(Vector3 pos, float radius, GameObject src)
        {
            if (!GlidersDown) return;
            RaiseAlarm();   // shots in the night carry: everyone wakes up
            if (!alert && !asleep && (pos - transform.position).sqrMagnitude < radius * radius) alert = true;
            // a grenade or shell bursting close makes a man get down
            float d = Vector3.Distance(pos, transform.position);
            if (!asleep && src != gameObject && d < 14f && radius > 60f)
            {
                suppression = Mathf.Min(1.5f, suppression + 0.5f * (1f - d / 14f));
                threatPos = src ? src.transform.position : pos; threatUntil = Time.time + 6f;
            }
        }

        void Update()
        {
            if (self.IsDead || !Net.Simulates) return;   // in multiplayer only the host runs the AI
            if (asleep)
            {
                // asleep in the dugout: wake a few seconds after the alarm (pulling on boots, grabbing a rifle)
                if (Alarm && wakeAt < 0f) wakeAt = Mathf.Max(Time.time, alarmTime) + Random.Range(4f, 14f);
                if (wakeAt >= 0f && Time.time >= wakeAt) { asleep = false; alert = true; }
                else
                {
                    vertical = cc.isGrounded ? -2f : vertical - 18f * Time.deltaTime;
                    cc.Move(new Vector3(0f, vertical, 0f) * Time.deltaTime);
                    if (animator) animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
                    return;
                }
            }
            suppression = Mathf.Max(0f, suppression - Time.deltaTime * 0.22f);
            if (Climb()) return;
            if (Swim()) return;

            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.4f;
                var t = FindTarget();
                if (t != target) targetSince = Time.time;
                // the man he was shooting at has gone into the smoke: keep firing where he was for a few seconds
                if (!t && target && !target.IsDead && weapon && alert
                    && SmokeCloud.Blocks(EyePos(), target.transform.position + Vector3.up * 1.2f))
                    BlindFire(target.transform.position + Vector3.up * 1f, Random.Range(3f, 7f));
                target = t;
            }

            Vector3 move = Vector3.zero;
            StanceKind wantStance = StanceKind.Stand;
            bool dugIn = mode == Mode.HoldPosition && combatRoam <= 0f;   // pillbox, MG nest, gun pit: stay at the weapon
            bool underFire = Time.time < threatUntil && suppression > 0.3f;
            // smoke: screen an advance over open ground when pinned down, or cover falling back
            if (target && smokeGrenades > 0 && smokeGrenade && Time.time > nextSmoke)
            {
                Vector3 st = target.transform.position - transform.position; st.y = 0f;
                float sd = st.magnitude;
                bool falling = self.Current < self.maxHealth * retreatAt && (combatRoam > 0f || mode != Mode.HoldPosition);
                if (falling && sd < 45f) ThrowSmoke(transform.position + st.normalized * Mathf.Min(8f, sd * 0.4f));
                else if ((underFire || suppression > 0.5f) && mode != Mode.HoldPosition && sd > 15f && sd < 70f)
                    ThrowSmoke(transform.position + st.normalized * Mathf.Min(sd * 0.45f, 18f));
            }

            // ---- under fire: get into cover, keep low, pop up to shoot back
            if (underFire && !dugIn && TakeCover(ref move, ref wantStance)) { }
            else if (target)
            {
                alert = true;
                if (Time.time - targetSince > reactionTime) RaiseAlarm();   // a guard shouts the alarm
                Vector3 to = target.transform.position - transform.position; to.y = 0f;
                float dist = to.magnitude;
                // pick a new firing position now and then: dash sideways or to cover, fall back when badly hurt
                if (Time.time > nextMoveDecision)
                {
                    nextMoveDecision = Time.time + Random.Range(3f, 7f);
                    // how to fight from here: dug-in posts (pillbox, MG nest, trench) shoot standing over the parapet;
                    // in the open a man kneels or gets down, and goes flat when he's been hit
                    if (dugIn) combatStance = StanceKind.Stand;
                    else
                    {
                        float r = Random.value, hurt = self.Current < self.maxHealth * 0.5f ? 0.3f : 0f;
                        float prone = (self.faction == Faction.British ? 0.4f : 0.25f) + hurt, kneel = 0.4f;
                        combatStance = r < prone ? StanceKind.Prone : r < prone + kneel ? StanceKind.Crouch : StanceKind.Stand;
                    }
                    moveGoal = null;
                    if (self.Current < self.maxHealth * retreatAt && (combatRoam > 0f || mode != Mode.HoldPosition))
                        moveGoal = FindSpot(transform.position - to.normalized * Random.Range(10f, 18f), 4f);
                    else if (mode == Mode.HoldPosition && combatRoam > 0f && Random.value < 0.7f)
                        moveGoal = FindSpot(home, combatRoam);
                    else if (mode != Mode.HoldPosition && dist > preferredRange)
                        moveGoal = FindSpot(transform.position + to.normalized * Mathf.Min(dist - preferredRange, 12f) + Random.insideUnitSphere * 4f, 3f);
                }
                bool dashing = false;
                if (moveGoal.HasValue)
                {
                    Vector3 g = Toward(moveGoal.Value);
                    if ((moveGoal.Value - transform.position).sqrMagnitude < 1f) moveGoal = null;
                    else { move = g * runSpeed; dashing = true; }
                }
                else if (mode != Mode.HoldPosition && dist > preferredRange) move = Toward(target.transform.position) * moveSpeed;
                Face(to);
                float react = reactionTime * (alert ? 0.5f : 1f) * Mathf.Lerp(1.4f, 0.7f, skill);
                wantStance = dashing || move.sqrMagnitude > 0.01f ? StanceKind.Stand : combatStance;
                // a dug-in man ducks below the parapet for a moment when the fire gets very close
                if (dugIn && suppression > 0.8f && Time.time < threatUntil) { wantStance = StanceKind.Crouch; }
                else if (Time.time - targetSince > react && (!dashing || Random.value < 0.35f)) Shoot(dashing ? dist * 1.8f : dist);
            }
            else if (Time.time < blindUntil && weapon && !asleep)
            {
                // firing blind into the smoke at where the enemy must be
                Vector3 to = blindPoint - transform.position; to.y = 0f;
                Face(to);
                wantStance = dugIn ? StanceKind.Stand : StanceKind.Crouch;
                if (Time.time > blindNextAim) { blindNextAim = Time.time + Random.Range(0.6f, 1.4f); blindAim = blindPoint + Random.insideUnitSphere * blindSpread; }
                ShootAt(blindAim, to.magnitude, 1.6f);
            }
            else if (mode == Mode.Advance && advanceTarget)
            {
                // his own spot round the objective, not the same point as everyone else
                Vector3 goal = advanceTarget.position + Slot(3f, 7f);
                Vector3 to = goal - transform.position; to.y = 0f;
                if (to.magnitude > 2f)
                {
                    // the fighting has started: move carefully, a bound at a time, then down on one knee to look and listen
                    if (Alarm && Cautious(out bool halted))
                    {
                        if (halted) { wantStance = StanceKind.Crouch; Face(to); }
                        else { move = Toward(goal) * moveSpeed * 0.7f; Face(move); }
                    }
                    else { move = Toward(goal) * moveSpeed; Face(move); }
                }
                else if (Alarm) { wantStance = StanceKind.Crouch; Face(advanceTarget.forward); }
            }
            else if (mode == Mode.FollowPlayer)
            {
                var player = FindAnyObjectByType<SoldierController>();
                if (player && player.enabled)
                {
                    wantStance = player.Stance ? player.Stance.Current : StanceKind.Stand;   // the section copies its leader
                    // a few metres behind and to the side of the leader, each man in his own place
                    Vector3 goal = player.transform.position - player.transform.forward * 4f + Slot(2.5f, 5f);
                    Vector3 to = goal - transform.position; to.y = 0f;
                    if (to.magnitude > 3f) { move = Toward(goal) * moveSpeed * (to.magnitude > 12f ? 1f : 0.75f); Face(move); }
                }
            }
            else if (mode == Mode.HoldPosition)
            {
                // the sappers going over the bridge for the charges: walk from point to point, kneel and look
                if (Searching)
                {
                    Vector3 goal = searchPoints[searchIndex];
                    Vector3 g = goal - transform.position; g.y = 0f;
                    if (searchUntil > 0f)
                    {
                        wantStance = StanceKind.Crouch;
                        if (Time.time > searchUntil) { searchIndex++; searchUntil = 0f; }
                    }
                    else if (g.magnitude < 0.8f) searchUntil = Time.time + searchPause * Random.Range(0.8f, 1.25f);
                    else { move = Toward(goal) * walkSpeed * 1.5f; Face(move); }
                }
                // a patrol walks its route; where the shooting finds them is where they fight
                else if (patrolRoute != null && patrolRoute.Length > 0 && !alert)
                {
                    home = transform.position;
                    if (Time.time > pauseUntil)
                    {
                        Vector3 goal = patrolRoute[patrolIndex % patrolRoute.Length];
                        Vector3 g = goal - transform.position; g.y = 0f;
                        if (g.magnitude < 0.8f) { patrolIndex = (patrolIndex + 1) % patrolRoute.Length; pauseUntil = Time.time + patrolPause; }
                        else { move = Toward(goal) * walkSpeed; Face(move); }
                    }
                }
                // on their rounds: walk to a spot near the post, wait, pick another
                else if (patrolRadius > 0f && !alert)
                {
                    if (!moveGoal.HasValue && Time.time > pauseUntil) moveGoal = FindSpot(home, patrolRadius);
                    if (moveGoal.HasValue)
                    {
                        Vector3 g = moveGoal.Value - transform.position; g.y = 0f;
                        if (g.magnitude < 0.8f) { moveGoal = null; pauseUntil = Time.time + Random.Range(2f, 6f); }
                        else { move = Toward(moveGoal.Value) * walkSpeed; Face(move); }
                    }
                }
                else
                {
                    Vector3 to = home - transform.position; to.y = 0f;
                    float near = watchDir.HasValue ? 1.2f : Mathf.Max(1.5f, combatRoam);
                    if (to.magnitude > near) { move = Toward(home) * runSpeed * (watchDir.HasValue ? 0.85f : 0.7f); Face(move); }
                    else if (watchDir.HasValue)
                    {
                        // in position: down on one knee, watching his arc
                        Face(watchDir.Value);
                        wantStance = StanceKind.Crouch;
                    }
                }
            }

            stance.Set(wantStance);
            if (stance.Current == StanceKind.Crouch) move *= 0.45f;
            else if (stance.Current == StanceKind.Prone) move *= 0.2f;
            if (stance.Changing) move *= 0.3f;
            if (Time.time < staggerUntil) move *= 0.35f;
            Drive(move);
        }

        // ------------------------------------------------------------------ moving smoothly

        Vector3 vel;            // horizontal velocity, eased so men start, stop and turn like men, not robots
        Vector3 knock;          // a push from a hit, fading out

        /// <summary>Unit direction to walk to reach dest (over the bridge if the canal is in the way).</summary>
        Vector3 Toward(Vector3 dest)
        {
            Vector3 w = Water.Waypoint(transform.position, dest) - transform.position; w.y = 0f;
            return w.sqrMagnitude > 0.0001f ? w.normalized : Vector3.zero;
        }

        // ------------------------------------------------------------------ steering round things (gliders, walls)

        float avoidSide, avoidUntil, stuckCheckAt, forceDetourUntil;
        Vector3 stuckFrom;
        static readonly RaycastHit[] castHits = new RaycastHit[16];

        /// <summary>
        /// No NavMesh: the men walk straight at where they're going. When something solid is in the way (a glider's
        /// fuselage, a wall, the pillbox) they slide along it on one side until the way is clear again, and if they
        /// still get nowhere they try the other side.
        /// </summary>
        Vector3 Avoid(Vector3 want)
        {
            float speed = want.magnitude;
            if (speed < 0.05f) { stuckCheckAt = Time.time + 1.5f; stuckFrom = transform.position; return want; }
            Vector3 dir = want / speed;

            // making no headway for a while although we want to move: go round the other way
            if (Time.time > stuckCheckAt + 1.0f)
            {
                Vector3 moved = transform.position - stuckFrom; moved.y = 0f;
                if (moved.magnitude < 0.35f * speed)
                {
                    avoidSide = avoidSide == 0f ? (Random.value < 0.5f ? 1f : -1f) : -avoidSide;
                    forceDetourUntil = Time.time + 1.8f;
                }
                stuckFrom = transform.position; stuckCheckAt = Time.time + 1.5f;
            }

            bool forced = Time.time < forceDetourUntil;
            // while going round a wall, keep to the same side until the way ahead is properly open again (past the corner)
            if (!forced && Clear(dir, avoidSide != 0f ? 3.5f : 1.6f))
            {
                if (Time.time > avoidUntil) avoidSide = 0f;
                return want;
            }
            // a wall he can get over (the side of a trench he's down in, sandbags, a low wall): climb out over it
            if (TryMantle(dir)) return Vector3.zero;
            if (avoidSide == 0f) avoidSide = NearerCorner(dir);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int a = forced ? 70 : 25; a <= 155; a += 15)
                {
                    Vector3 d = Quaternion.Euler(0f, a * avoidSide, 0f) * dir;
                    if (Clear(d, 1.3f)) { avoidUntil = Time.time + 2.5f; return d * speed; }
                }
                avoidSide = -avoidSide;
            }
            return dir * speed * 0.15f;     // boxed in: shuffle and let the stuck check try again
        }

        /// <summary>Which way round the wall ahead: look along it both ways for its end (up to 30 m) and take the nearer.</summary>
        float NearerCorner(Vector3 dir)
        {
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            float Gap(float sign)
            {
                Vector3 eye = transform.position + Vector3.up * 1.0f;
                for (float d = 2f; d <= 30f; d += 2f)
                {
                    if (Physics.Raycast(eye, side * sign, out RaycastHit along, d, ~0, QueryTriggerInteraction.Ignore) && Solid(along)) return 99f;   // walled in that way
                    Vector3 p = eye + side * sign * d;
                    if (!Physics.Raycast(p, dir, out RaycastHit h, 3f, ~0, QueryTriggerInteraction.Ignore) || !Solid(h)) return d;
                }
                return 99f;
            }
            float l = Gap(-1f), r = Gap(1f);
            if (l >= 99f && r >= 99f) return OpenerSide(dir);
            return r <= l ? 1f : -1f;
        }

        // ------------------------------------------------------------------ keeping their distance

        static readonly System.Collections.Generic.List<SoldierAI> All = new System.Collections.Generic.List<SoldierAI>();
        static int seeds;
        int seed = 0;   // this man's own number: his slot round a shared goal, which way he steps aside
        int Seed => seed != 0 ? seed : (seed = ++seeds * 2654435 + 977);
        const float Spread = 3.2f;   // metres between men on the move

        /// <summary>A push away from friends closer than Spread (stronger the closer they are).</summary>
        Vector3 Spacing()
        {
            Vector3 me = transform.position, push = Vector3.zero;
            for (int i = 0; i < All.Count; i++)
            {
                var o = All[i];
                if (o == this || !o.self || o.self.IsDead || !self || o.self.faction != self.faction) continue;
                Vector3 d = me - o.transform.position; d.y = 0f;
                float m = d.magnitude;
                if (m >= Spread) continue;
                if (m < 0.05f) d = new Vector3(Mathf.Sin(Seed), 0f, Mathf.Cos(Seed)); else d /= m;
                push += d * (Spread - m) / Spread;
            }
            return push.sqrMagnitude > 1f ? push.normalized : push;
        }

        /// <summary>This man's own place round a shared goal: fixed for him, between min and max metres out.</summary>
        Vector3 Slot(float min, float max)
        {
            int h = Seed * 7919;
            float a = (h & 1023) / 1023f * Mathf.PI * 2f, r = Mathf.Lerp(min, max, ((h >> 10) & 1023) / 1023f);
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
        }

        float boundUntil, haltUntil;
        /// <summary>Bounding: move 5-9 s, then kneel 2-4 s and look. halted = in the pause.</summary>
        bool Cautious(out bool halted)
        {
            if (Time.time > haltUntil && Time.time > boundUntil) boundUntil = Time.time + Random.Range(5f, 9f);
            if (Time.time > boundUntil && haltUntil < boundUntil) haltUntil = Time.time + Random.Range(2f, 4f);
            halted = Time.time < haltUntil;
            return true;
        }

        float OpenerSide(Vector3 dir)
        {
            float l = Room(Quaternion.Euler(0f, -60f, 0f) * dir), r = Room(Quaternion.Euler(0f, 60f, 0f) * dir);
            return Mathf.Abs(l - r) < 0.3f ? (Random.value < 0.5f ? -1f : 1f) : (r > l ? 1f : -1f);
        }

        float Room(Vector3 d)
        {
            Vector3 p = transform.position + Vector3.up * 1.0f;
            return Physics.Raycast(p, d, out RaycastHit h, 8f, ~0, QueryTriggerInteraction.Ignore) && Solid(h) ? h.distance : 8f;
        }

        bool Clear(Vector3 d, float dist)
        {
            float r = Mathf.Max(0.2f, cc.radius * 0.9f);
            Vector3 a = transform.position + Vector3.up * (cc.stepOffset + r + 0.05f);
            Vector3 b = transform.position + Vector3.up * Mathf.Max(cc.stepOffset + r + 0.1f, Mathf.Min(cc.height, 1.6f) - r);
            int n = Physics.CapsuleCastNonAlloc(a, b, r, d, castHits, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (castHits[i].distance > 0f && Solid(castHits[i])) return false;
            return true;
        }

        /// <summary>Something to walk round: not the ground or a slope, not ourselves, not other men (they move).</summary>
        bool Solid(RaycastHit h)
        {
            if (!h.collider || h.collider.transform.IsChildOf(transform)) return false;
            if (h.normal.y > 0.55f) return false;
            if (h.collider is TerrainCollider) return false;
            if (h.collider is CharacterController || h.collider.GetComponentInParent<SoldierAI>() || h.collider.GetComponentInParent<SoldierController>()) return false;
            return true;
        }

        void Drive(Vector3 move)
        {
            // keep a few metres from the next man: a bunch is one grenade's work
            Vector3 flat = new Vector3(move.x, 0f, move.z), push = Spacing();
            if (flat.sqrMagnitude > 0.01f) flat += push * 1.6f;
            else if (push.sqrMagnitude > 0.09f) flat = push * 0.9f;   // standing about: drift apart, gently
            Vector3 want = Avoid(flat);
            if (climbing) return;   // started to climb out: Climb() moves him from the next frame
            // speeding up takes a moment, stopping a little less; a sprinting man can't turn on a sixpence
            float accel = want.sqrMagnitude > vel.sqrMagnitude ? 5f : 9f;
            vel = Vector3.MoveTowards(vel, want, accel * Time.deltaTime);
            knock = Vector3.MoveTowards(knock, Vector3.zero, 6f * Time.deltaTime);
            vertical = cc.isGrounded ? -2f : vertical - 18f * Time.deltaTime;
            Vector3 step = vel + knock; step.y = vertical;
            cc.Move(step * Time.deltaTime);
            if (animator)
            {
                Vector3 local = transform.InverseTransformDirection(vel);
                Locomotion.Set(animator, local, 0.15f, stance == null || stance.Current == StanceKind.Stand);
                animator.SetBool("Aiming", target != null);
            }
        }

        // ------------------------------------------------------------------ under fire

        float suppression;      // 0..1+: how close the fire has been lately
        Vector3 threatPos;
        float threatUntil;
        Vector3? coverSpot;
        bool coverHigh;         // a wall he can stand behind (peeks round it) rather than a low bank or sandbags
        float coverUntil, peekSwitch, nextCoverSearch, staggerUntil;
        bool peeking;

        void OnShot(Vector3 from, Vector3 to, GameObject shooter)
        {
            if (!shooter || shooter == gameObject || self.IsDead || asleep) return;
            if (shooter.TryGetComponent(out Health h) && h.faction == self.faction) return;
            Vector3 chest = transform.position + Vector3.up * (stance ? stance.EyeHeight * 0.8f : 1.2f);
            Vector3 ab = to - from; float t = Mathf.Clamp01(Vector3.Dot(chest - from, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
            float d = Vector3.Distance(chest, from + ab * t);
            if (d > 3.5f) return;
            suppression = Mathf.Min(1.5f, suppression + 0.12f + 0.3f * (1f - d / 3.5f));
            threatPos = from; threatUntil = Time.time + 8f;
            alert = true;
        }

        void OnDamaged(Health h, DamageInfo info)
        {
            suppression = Mathf.Min(1.5f, suppression + 0.6f);
            threatUntil = Time.time + 10f;
            if (info.source) threatPos = info.source.transform.position;
            else threatPos = transform.position - info.direction.normalized * 20f;
            // stagger: a push away from the hit and a moment of slowness
            Vector3 push = info.direction; push.y = 0f;
            knock = push.normalized * Mathf.Clamp(info.amount * 0.05f, 0.8f, 2.5f);
            staggerUntil = Time.time + 0.45f;
            coverSpot = null;    // get somewhere better
        }

        /// <summary>Under fire: find cover, run there, then stay low and pop up now and then to shoot. False = no cover to be had.</summary>
        bool TakeCover(ref Vector3 move, ref StanceKind wantStance)
        {
            Vector3 toThreat = threatPos - transform.position; toThreat.y = 0f;
            if ((!coverSpot.HasValue || Time.time > coverUntil) && Time.time > nextCoverSearch)
            {
                nextCoverSearch = Time.time + 1.2f;
                coverSpot = FindCover(threatPos, 14f, out coverHigh);
                coverUntil = Time.time + Random.Range(10f, 18f);
                peeking = false; peekSwitch = Time.time + Random.Range(1.2f, 2.5f);
            }
            if (!coverSpot.HasValue)
            {
                // nowhere to hide: get flat where he is, and keep firing from there
                wantStance = suppression > 0.6f ? StanceKind.Prone : StanceKind.Crouch;
                Face(toThreat);
                if (target && suppression < 1.1f) Shoot(Vector3.Distance(transform.position, target.transform.position) * 1.3f);
                return true;
            }
            Vector3 c = coverSpot.Value;
            Vector3 toCover = c - transform.position; toCover.y = 0f;
            if (toCover.magnitude > 0.8f)
            {
                // the dash to cover: head down, run
                move = Toward(c) * runSpeed * 1.1f;
                Face(move);
                wantStance = StanceKind.Stand;
                return true;
            }
            // in cover: hide, then peek and fire, then hide again (longer while the fire is heavy)
            Face(toThreat);
            if (Time.time > peekSwitch)
            {
                peeking = !peeking && suppression < 1.2f;
                peekSwitch = Time.time + (peeking ? Random.Range(1.5f, 3f) : Random.Range(1.2f, 2.5f) * (0.6f + suppression));
            }
            if (peeking)
            {
                wantStance = coverHigh ? StanceKind.Stand : StanceKind.Crouch;   // over the bank, or round the wall
                if (coverHigh) move = Vector3.Cross(Vector3.up, toThreat.normalized) * 0.6f * (Mathf.PerlinNoise(Time.time, 3f) > 0.5f ? 1f : -1f);
                if (target) Shoot(Vector3.Distance(transform.position, target.transform.position));
            }
            else wantStance = coverHigh ? StanceKind.Crouch : StanceKind.Prone;
            return true;
        }

        /// <summary>Nearby spot with something solid between it and the threat. high = covers a standing man.</summary>
        Vector3? FindCover(Vector3 threat, float radius, out bool high)
        {
            high = false;
            Vector3 me = transform.position;
            Vector3? best = null; float bestScore = float.MaxValue;
            for (int ring = 0; ring < 4; ring++)
            {
                float r = ring == 0 ? 0f : radius * ring / 3f;
                int n = ring == 0 ? 1 : 10;
                for (int i = 0; i < n; i++)
                {
                    float a = (i + Random.value * 0.5f) / n * Mathf.PI * 2f;
                    Vector3 p = me + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit g, 8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (g.collider.GetComponentInParent<Health>() || g.normal.y < 0.7f) continue;
                    if (Mathf.Abs(g.point.y - me.y) > 1.8f || Water.SurfaceAt(g.point + Vector3.up * 0.1f, out _)) continue;
                    if (Toward(g.point) == Vector3.zero && r > 0f) continue;
                    Vector3 tgt = threat + Vector3.up * 1.2f;
                    bool low = Blocked(g.point + Vector3.up * 0.7f, tgt), tall = low && Blocked(g.point + Vector3.up * 1.5f, tgt);
                    if (!low) continue;
                    float score = Vector3.Distance(me, g.point) + (tall ? -2f : 0f) + (Vector3.Distance(g.point, threat) < Vector3.Distance(me, threat) - 6f ? 6f : 0f);
                    if (score < bestScore) { bestScore = score; best = g.point; high = tall; }
                }
            }
            return best;
        }

        bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; float len = d.magnitude;
            if (len < 1f) return false;
            if (!Physics.Raycast(from, d / len, out RaycastHit h, Mathf.Min(len, 4f), ~0, QueryTriggerInteraction.Ignore)) return false;
            return !h.collider.transform.IsChildOf(transform) && !h.collider.GetComponentInParent<Health>();
        }

        // ------------------------------------------------------------------ in the water

        bool climbing; Vector3 climbFrom, climbTo; float climbT;

        /// <summary>Floating with head and shoulders out; swims for the nearest bank or quay and climbs out.</summary>
        bool Swim()
        {
            if (!Water.Swimming(transform.position, out float surface)) return false;
            stance.Set(StanceKind.Stand);
            // the canal: make for the nearer quay; elsewhere: back the way he came
            Vector3 dir = Mathf.Abs(transform.position.x) < 23f ? new Vector3(Mathf.Sign(transform.position.x == 0f ? 1f : transform.position.x), 0f, 0f) : -transform.forward;
            if (advanceTarget && Mathf.Abs(transform.position.x) < 23f)
                dir = new Vector3(Mathf.Sign(advanceTarget.position.x - 0.01f), 0f, 0f);   // the side he was heading for
            Face(dir);
            if (Water.FindClimb(transform.position, dir, surface, transform, out Vector3 top))
            {
                climbing = true; climbFrom = transform.position; climbTo = top + dir * 0.2f; climbT = 0f;
                cc.enabled = false;
                return true;
            }
            float lift = (Water.FloatFeet(surface) - transform.position.y) * 2.5f;
            vel = Vector3.MoveTowards(vel, dir * 1.3f, 3f * Time.deltaTime);
            cc.Move(new Vector3(vel.x, Mathf.Clamp(lift, -3f, 2f), vel.z) * Time.deltaTime);
            vertical = 0f;
            if (animator) { animator.SetFloat("Speed", vel.magnitude, 0.2f, Time.deltaTime); animator.SetFloat("MoveX", 0f); animator.SetFloat("MoveZ", vel.magnitude, 0.2f, Time.deltaTime); }
            return true;
        }

        float nextMantleTry;

        /// <summary>
        /// Up and over: a wall right in front, with firm ground on top of it between knee and chest height above his
        /// feet and room to stand there - the side of a trench he has dropped into, a sandbag wall, a low garden
        /// wall. He climbs it (the same move as out of the canal) instead of walking into it for ever.
        /// </summary>
        bool TryMantle(Vector3 dir)
        {
            if (Time.time < nextMantleTry || !cc.enabled) return false;
            nextMantleTry = Time.time + 0.4f;
            Vector3 feet = transform.position;
            dir.y = 0f; if (dir.sqrMagnitude < 0.01f) return false; dir.Normalize();
            if (!Physics.Raycast(feet + Vector3.up * 0.55f, dir, out RaycastHit wall, cc.radius + 0.9f, ~0, QueryTriggerInteraction.Ignore) || !Solid(wall)) return false;
            Vector3 over = wall.point + dir * 0.5f; over.y = feet.y + 2.3f;
            if (!Physics.Raycast(over, Vector3.down, out RaycastHit top, 2.3f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (top.normal.y < 0.7f || top.collider.transform.IsChildOf(transform) || top.collider is CharacterController) return false;
            float h = top.point.y - feet.y;
            if (h < 0.45f || h > 1.95f) return false;
            if (Water.SurfaceAt(top.point + Vector3.up * 0.05f, out _)) return false;
            Vector3 stand = top.point + dir * 0.3f;
            float r = Mathf.Max(0.2f, cc.radius * 0.9f);
            if (Physics.CheckCapsule(stand + Vector3.up * (r + 0.12f), stand + Vector3.up * (1.7f - r), r, ~0, QueryTriggerInteraction.Ignore)) return false;
            climbing = true; climbFrom = feet; climbTo = stand; climbT = 0f;
            cc.enabled = false;
            Face(dir);
            return true;
        }

        // ------------------------------------------------------------------ orders: holding the bridge

        Vector3[] searchPoints;
        int searchIndex;
        float searchPause = 2.5f, searchUntil;
        Vector3? watchDir;
        /// <summary>Searching the bridge for demolition charges (the sappers after "Ham and Jam").</summary>
        public bool Searching => searchPoints != null && searchIndex < searchPoints.Length;

        /// <summary>Take up a position and hold it: go there, get down (kneeling) and watch the given direction;
        /// fight from there, moving no more than 'roam' metres. Used when the bridge has been taken.</summary>
        public void Defend(Vector3 spot, Vector3 watch, float roam = 4f)
        {
            mode = Mode.HoldPosition; advanceTarget = null; patrolRoute = null; patrolRadius = 0f;
            home = spot; combatRoam = roam; alert = true; moveGoal = null;
            watch.y = 0f; watchDir = watch.sqrMagnitude > 0.01f ? watch.normalized : (Vector3?)null;
            searchPoints = null;
        }

        /// <summary>Walk these points one after another, kneeling a moment at each to look (for the charges under the
        /// bridge), then take up the given position.</summary>
        public void SearchThenDefend(Vector3[] points, float pause, Vector3 spot, Vector3 watch)
        {
            Defend(spot, watch, 3f);
            searchPoints = points; searchIndex = 0; searchUntil = 0f; searchPause = pause;
        }

        bool Climb()
        {
            if (!climbing) return false;
            climbT += Time.deltaTime / 1.1f;
            float k = Mathf.SmoothStep(0f, 1f, climbT);
            // up the wall first, then over the edge
            Vector3 up = new Vector3(climbFrom.x, Mathf.Lerp(climbFrom.y, climbTo.y + 0.3f, Mathf.Clamp01(k * 1.6f)), climbFrom.z);
            transform.position = Vector3.Lerp(up, climbTo, Mathf.Clamp01((k - 0.5f) * 2f));
            if (animator) animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
            if (climbT >= 1f) { climbing = false; transform.position = climbTo; cc.enabled = true; vel = Vector3.zero; }
            return true;
        }

        /// <summary>A random spot near a centre that is solid ground at about the same level (no walking off the bridge into the canal).</summary>
        Vector3? FindSpot(Vector3 centre, float radius)
        {
            for (int i = 0; i < 6; i++)
            {
                Vector2 r = Random.insideUnitCircle * radius;
                Vector3 p = new Vector3(centre.x + r.x, transform.position.y + 3f, centre.z + r.y);
                if (!Physics.Raycast(p, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (Mathf.Abs(hit.point.y - transform.position.y) > 1.6f) continue;
                if (hit.point.y < -3.3f) continue;   // the canal bed, the river and the pond: stay out
                if (Water.SurfaceAt(hit.point + Vector3.up * 0.05f, out _)) continue;   // shallows at the water's edge too
                return hit.point;
            }
            return null;
        }

        void Face(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.01f) return;
            dir.y = 0f; if (dir.sqrMagnitude < 0.01f) return;
            // fast for a big turn, easing in at the end
            var want = Quaternion.LookRotation(dir);
            float ang = Quaternion.Angle(transform.rotation, want);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, Mathf.Clamp(ang * 5f, 60f, 420f) * Time.deltaTime);
        }

        Health FindTarget()
        {
            if (!GlidersDown && self.faction != Faction.British) return null;   // nothing to see yet: a quiet night
            float range = alert ? viewRange : nightViewRange;
            Health best = null; float bestD = range;
            Vector3 eye = transform.position + Vector3.up * (stance ? stance.EyeHeight : 1.6f);
            foreach (var h in Health.All)
            {
                if (!h || h.IsDead || !h.isActiveAndEnabled || h.faction == self.faction || h.Sheltered) continue;
                Vector3 to = h.transform.position + Vector3.up * (h.TryGetComponent(out Stance hs) ? Mathf.Max(0.2f, hs.EyeHeight - 0.3f) : 1.2f) - eye;
                float d = to.magnitude;
                if (d > bestD) continue;
                if (!alert && Vector3.Angle(transform.forward, to) > fieldOfView * 0.5f) continue;
                if (Physics.Raycast(eye, to.normalized, out RaycastHit hit, d, ~0, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(h.transform)) continue;
                if (SmokeCloud.Blocks(eye, eye + to)) continue;   // hidden in (or behind) smoke
                best = h; bestD = d;
            }
            // British players hidden in vehicles still count via the vehicle's Health
            return best;
        }

        Vector3 EyePos() => transform.position + Vector3.up * (stance ? stance.EyeHeight : 1.6f);

        // ------------------------------------------------------------------ smoke

        Vector3 blindPoint, blindAim;
        float blindUntil, blindNextAim, blindSpread = 3f, nextSmoke;

        void BlindFire(Vector3 at, float seconds)
        {
            blindPoint = at; blindUntil = Time.time + seconds; blindNextAim = 0f;
        }

        /// <summary>
        /// A smoke grenade has gone off. If it's the enemy's and he can see it, he expects them to come out of it: the
        /// MG and riflemen sweep the cloud for a while; and they know where the enemy is now. His own side's smoke he
        /// leaves alone.
        /// </summary>
        void OnSmoke(SmokeCloud s)
        {
            if (!Net.Simulates || self.IsDead || asleep || !weapon) return;
            var by = s.owner ? s.owner.GetComponentInParent<Health>() : null;
            if (by && by.faction == self.faction) return;
            Vector3 eye = EyePos(), c = s.transform.position + Vector3.up;
            float d = Vector3.Distance(eye, c);
            if (d > 80f) return;
            if (Physics.Raycast(eye, (c - eye).normalized, out RaycastHit hit, d - 1f, ~0, QueryTriggerInteraction.Ignore)) return;   // out of sight
            alert = true;
            threatPos = c; threatUntil = Time.time + 6f;
            blindSpread = s.radius * 0.6f;
            if (!target) BlindFire(c, Random.Range(4f, 10f) * Mathf.Lerp(1.2f, 0.7f, d / 80f));
        }

        /// <summary>Lob a smoke grenade to land at a point (high, ~40 degrees).</summary>
        void ThrowSmoke(Vector3 at)
        {
            nextSmoke = Time.time + Random.Range(25f, 40f);
            smokeGrenades--;
            Vector3 from = transform.position + Vector3.up * 1.6f + transform.forward * 0.4f;
            Vector3 flat = at - from; float h = flat.y; flat.y = 0f;
            float dist = Mathf.Max(2f, flat.magnitude), ang = 40f * Mathf.Deg2Rad, g = -Physics.gravity.y;
            float denom = 2f * Mathf.Cos(ang) * Mathf.Cos(ang) * (dist * Mathf.Tan(ang) - h);
            float speed = denom > 0.1f ? Mathf.Sqrt(g * dist * dist / denom) : 12f;
            Vector3 vel = flat.normalized * Mathf.Cos(ang) * speed + Vector3.up * Mathf.Sin(ang) * speed;
            var gr = Instantiate(smokeGrenade, from, Random.rotation);
            gr.Launch(vel, gameObject);
            NetHooks.ProjectileLaunched?.Invoke(smokeGrenade, from, vel, gameObject);
        }

        /// <summary>Shoot at a point (blind fire into smoke): like Shoot, with extra spread.</summary>
        void ShootAt(Vector3 point, float dist, float extraMiss)
        {
            if (!weapon || (stance && stance.Changing)) return;
            if (weapon.Ammo <= 0) { weapon.Reload(); return; }
            if (Time.time > burstEnd && Time.time < nextBurst) return;
            if (Time.time > nextBurst)
            {
                burstEnd = Time.time + Random.Range(0.3f, 1.0f);
                nextBurst = burstEnd + Random.Range(1.0f, 2.6f) * (1.5f - skill);
            }
            Vector3 eye = transform.position + Vector3.up * (stance ? stance.EyeHeight - 0.1f : 1.5f);
            float steady = stance ? stance.SpreadScale : 1f;
            Vector3 aim = point + Random.insideUnitSphere * (Mathf.Lerp(3.5f, 0.6f, skill) * (dist / 25f) * extraMiss);
            weapon.Fire(new Ray(eye, (aim - eye).normalized), true, gameObject, steady);
        }

        void Shoot(float dist)
        {
            if (!weapon || (stance && stance.Changing)) return;
            if (weapon.Ammo <= 0) { weapon.Reload(); return; }
            if (Time.time > burstEnd && Time.time < nextBurst) return;
            if (Time.time > nextBurst)
            {
                burstEnd = Time.time + Random.Range(0.3f, 1.0f);
                nextBurst = burstEnd + Random.Range(0.8f, 2.2f) * (1.5f - skill);
            }
            Vector3 eye = transform.position + Vector3.up * (stance ? stance.EyeHeight - 0.1f : 1.5f);
            Vector3 aimPoint = target.transform.position + Vector3.up * AimHeight(target, 0.8f, 1.6f);
            float steady = stance ? stance.SpreadScale : 1f;
            float miss = Mathf.Lerp(3.5f, 0.6f, skill) * (dist / 25f) * Mathf.Lerp(1f, steady, 0.5f);
            aimPoint += Random.insideUnitSphere * miss;
            weapon.Fire(new Ray(eye, (aimPoint - eye).normalized), true, gameObject, steady);
        }
    }
}
