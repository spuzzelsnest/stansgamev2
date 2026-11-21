using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Drives a TankController along waypoints (the Bénouville road toward the bridge) the way a crew would at night:
    /// at a walking-to-cycling pace, easing round the bends and slowing for the junction, and halting to shoot - guns
    /// of 1944 were fired from a standstill. When it sees an enemy it stops, turns the hull if the gun can't traverse
    /// that far (a self-propelled gun in a casemate), lays the gun, fires, waits for the loader, and moves on once the
    /// target is gone or after a few rounds. It stops for its own infantry in the road, and halts for good at the end
    /// of the road or when it is knocked out.
    /// </summary>
    [RequireComponent(typeof(TankController), typeof(Health))]
    public class TankAI : MonoBehaviour
    {
        public Transform[] waypoints;
        [Tooltip("Road speed when nothing is happening (m/s). 3.5 m/s is about 13 km/h - cautious, at night, in the dark.")]
        public float cruiseSpeed = 3.5f;
        [Tooltip("Speed round a sharp bend (m/s).")]
        public float bendSpeed = 1.6f;
        public float engageRange = 160f;
        public float waypointRadius = 6f;
        [Tooltip("Rounds fired at one halt before it pushes on (if the target is still there it stops again).")]
        public int roundsPerHalt = 2;

        [Tooltip("Write what it is doing to the console for its first half minute (for testing).")]
        public bool logToConsole;
        [Header("Now (read only)")]
        [SerializeField] int wp;
        [SerializeField] string doing;
        [SerializeField] float steerNow, speedWanted, angleNow;
        TankController tank;
        Health self;
        float nextScan, haltUntil, lostSince = -1f, nextMove;
        int roundsHere;
        Health target;
        bool halted;

        void Awake()
        {
            tank = GetComponent<TankController>();
            self = GetComponent<Health>();
            tank.aiControlled = true;
            self.Died += (h, i) => { tank.aiThrottle = 0f; tank.aiSteer = 0f; enabled = false; };
        }

        void Update()
        {
            if (self.IsDead || !Net.Simulates) return;
            if (Time.time > nextScan)
            {
                nextScan = Time.time + 0.6f;
                var t = FindTarget();
                if (t) { target = t; lostSince = -1f; }
                else if (target && lostSince < 0f) lostSince = Time.time;
            }
            if (target && (target.IsDead || (lostSince > 0f && Time.time - lostSince > 3f))) target = null;

            float throttle = 0f, steer = 0f;
            bool roadDone = waypoints == null || wp >= waypoints.Length;

            if (target)
            {
                // halt, swing the hull if the gun can't come round, lay, fire
                Vector3 p = target.transform.position + Vector3.up * 1f;
                tank.AIAimAt(p);
                halted = true;
                bool stopped = Mathf.Abs(tank.Speed) < 0.25f;
                if (stopped && !tank.CanTraverseTo(p))
                {
                    Vector3 to = p - transform.position;
                    steer = Mathf.Clamp(Vector3.SignedAngle(transform.forward, Flat(to), Vector3.up) / 20f, -1f, 1f) * 0.6f;
                }
                else if (stopped && tank.Loaded && tank.GunLaid(p, 1.5f) && Time.time > haltUntil)
                {
                    if (tank.AIFire()) { roundsHere++; haltUntil = Time.time + Random.Range(1.5f, 3f); }
                }
                // after a few rounds, push on a little before stopping again (if the road goes on)
                if (roundsHere >= roundsPerHalt && !roadDone && Time.time > haltUntil)
                {
                    roundsHere = 0; target = null; halted = false;
                    pushOnUntil = Time.time + Random.Range(4f, 7f);
                }
            }
            else if (halted)
            {
                // the target is gone: a moment to look round, then on
                halted = false; roundsHere = 0;
                nextMove = Time.time + Random.Range(1.5f, 3f);
            }

            if (!target && !roadDone)
            {
                // follow the road: aim at the next waypoint, cutting in towards the one after as it gets close
                Vector3 pos = transform.position;
                // passed it (close, or already beyond it along the next stretch of road): on to the next
                while (!roadDone && (Flat(waypoints[wp].position - pos).magnitude < waypointRadius
                       || (wp + 1 < waypoints.Length && Vector3.Dot(Flat(pos - waypoints[wp].position), Flat(waypoints[wp + 1].position - waypoints[wp].position)) > 0f)))
                { wp++; roadDone = wp >= waypoints.Length; }
                if (!roadDone)
                {
                    Vector3 aim = waypoints[wp].position;
                    float dist = Flat(aim - pos).magnitude;
                    // look a few metres ahead along the road, so it eases round the corner instead of driving to the point
                    const float lookAhead = 10f;
                    if (wp + 1 < waypoints.Length && dist < lookAhead)
                        aim += Flat(waypoints[wp + 1].position - aim).normalized * (lookAhead - dist);
                    float angle = Vector3.SignedAngle(transform.forward, Flat(aim - pos), Vector3.up);
                    angleNow = angle;
                    steer = Mathf.Clamp(angle / 20f, -1f, 1f);
                    // slow for bends, crawl the last stretch, pivot on the spot for a sharp turn
                    float speed = Mathf.Lerp(cruiseSpeed, bendSpeed, Mathf.Clamp01(Mathf.Abs(angle) / 45f));
                    if (wp == waypoints.Length - 1) speed = Mathf.Min(speed, 0.6f + dist * 0.15f);
                    if (Mathf.Abs(angle) > 70f) speed = 0f;
                    if (Time.time < nextMove) speed = 0f;
                    if (FriendInTheWay()) speed = 0f;
                    throttle = speed / Mathf.Max(1f, tank.maxForwardSpeed);
                }
            }

            tank.aiThrottle = throttle;
            tank.aiSteer = steer;
            steerNow = steer; speedWanted = throttle * tank.maxForwardSpeed;
            doing = target ? $"engaging {target.name}" : roadDone ? "end of the road" : Time.time < nextMove ? "pausing" : "driving";
            if (logUntil < 0f) logUntil = Time.time + 30f;   // a line a second in the console for its first half minute on the road
            if (logToConsole && logUntil > Time.time && Time.time > nextLog)
            {
                nextLog = Time.time + 1f;
                Debug.Log($"TankAI {name}: {doing}, wp {wp}, angle {angleNow:0}, steer {steer:0.00}, throttle {throttle:0.00}, speed {tank.Speed:0.0}, yaw {transform.eulerAngles.y:0}, pos {transform.position:F1}");
            }
        }

        float pushOnUntil, nextLog, logUntil = -1f;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>One of our own soldiers in the road just ahead.</summary>
        bool FriendInTheWay()
        {
            Vector3 fwd = transform.forward, pos = transform.position;
            foreach (var h in Health.All)
            {
                if (h == self || h.IsDead || !h.enabled || h.faction != self.faction || h.GetComponent<TankController>()) continue;
                Vector3 to = Flat(h.transform.position - pos);
                float ahead = Vector3.Dot(to, fwd);
                if (ahead > 0f && ahead < 7f && Mathf.Abs(Vector3.Dot(to, transform.right)) < 2f) return true;
            }
            return false;
        }

        Health FindTarget()
        {
            if (Time.time < pushOnUntil) return null;   // pushing on to a new firing spot
            Health best = null; float bestD = engageRange;
            Vector3 eye = transform.position + Vector3.up * 2.4f;
            foreach (var h in Health.All)
            {
                if (h.IsDead || !h.enabled || h.faction == self.faction || h.Sheltered) continue;
                Vector3 to = h.transform.position + Vector3.up - eye;
                float d = to.magnitude;
                if (d > bestD) continue;
                if (Physics.Raycast(eye, to.normalized, out RaycastHit hit, d, ~0, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(h.transform) && !hit.collider.transform.IsChildOf(transform)) continue;
                if (SmokeCloud.Blocks(eye, h.transform.position + Vector3.up)) continue;   // smoke hides them
                best = h; bestD = d;
            }
            return best;
        }
    }
}
