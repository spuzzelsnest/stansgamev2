using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// A German lorry going about its business before the gliders land: it drives a road route (blackout headlamp on,
    /// engine running, wheels turning, following the ground and the bridge deck), waits for anyone standing in its way,
    /// leaves the map at the end of the route and comes round again a while later. Once a player climbs in
    /// (TruckController) it hands the lorry over for good. Once the alarm is raised it stops
    /// where it is, lamp and engine off, and stays there as cover; if it was off the map then, it does not come back.
    ///
    /// Multiplayer: only the host drives it (offline play: this machine). It is a network object; the other machines
    /// get its position from the host and work out the rest (wheels, lamp, engine sound) from how it moves.
    /// "Off the map" is a parking spot far below the ground, so being hidden is just another position.
    /// </summary>
    public class AmbientVehicle : MonoBehaviour
    {
        public Vector3[] route;
        public float speed = 8f;             // ~30 km/h: a lorry in the blackout
        public float firstDeparture = 15f;   // seconds after the game starts
        public float repeatEvery = 140f;     // seconds between runs
        public Transform[] wheels;
        public float wheelRadius = 0.45f;
        public Light[] lamps;
        [Tooltip("Stop for good where it is when the alarm goes up (Pegasus). Off: carry on through the shooting.")]
        public bool stopOnAlarm = true;
        [Tooltip("How many times it drives the route (0 = for ever).")]
        public int maxRuns = 0;
        int runs;

        public const float HiddenDepth = 800f;
        Vector3 Hidden => route != null && route.Length > 0 ? route[0] + Vector3.down * HiddenDepth : Vector3.down * HiddenDepth;

        float clock, departAt, travelled, routeLength;
        bool driving, parked, started;
        Vector3 lastPos;
        AudioSource engine;

        void Awake()
        {
            routeLength = 0f;
            if (route != null) for (int i = 0; i < route.Length - 1; i++) routeLength += Vector3.Distance(Flat(route[i]), Flat(route[i + 1]));
            engine = gameObject.AddComponent<AudioSource>();
            engine.clip = Sfx.TruckEngine; engine.loop = true; engine.spatialBlend = 1f;
            engine.minDistance = 6f; engine.maxDistance = 220f; engine.volume = 0.55f;
            engine.pitch = engine.clip == Sfx.Engine ? 1.25f : 1f;   // a lorry, not a Panzer (the recording is a lorry already)
            lastPos = transform.position;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        bool Visible => transform.position.y > Hidden.y + HiddenDepth * 0.5f;

        void Update()
        {
            if (!Net.Simulates || parked || route == null || route.Length < 2) return;   // the host drives; the others watch
            if (!started) { started = true; clock = 0f; departAt = firstDeparture; if (Visible) Place(Hidden, transform.rotation, true); }
            clock += Time.deltaTime;
            if (stopOnAlarm && SoldierAI.Alarm) { parked = true; return; }   // the shooting has started: the driver stops and gets down
            if (!driving)
            {
                if (clock < departAt || (maxRuns > 0 && runs >= maxRuns)) return;
                driving = true; travelled = 0f; runs++;
                Vector3 p0 = PointAt(0f, out Vector3 d0); p0.y = GroundY(p0, p0.y + 3f);
                Place(p0, Quaternion.LookRotation(d0), true);
                return;
            }

            // somebody in the road ahead: wait for him to get out of the way
            Vector3 ahead = transform.position + transform.forward * 4.5f + Vector3.up;
            foreach (var c in Physics.OverlapSphere(ahead, 2.2f, ~0, QueryTriggerInteraction.Ignore))
                if (c is CharacterController) return;

            travelled += speed * Time.deltaTime;
            if (travelled >= routeLength)
            {
                driving = false; departAt = clock + repeatEvery;
                Place(Hidden, transform.rotation, true);
                return;
            }
            Vector3 p = PointAt(travelled, out Vector3 dir);
            Vector3 front = PointAt(Mathf.Min(travelled + 2f, routeLength), out _);
            Vector3 rear = PointAt(Mathf.Max(travelled - 2f, 0f), out _);
            float from = transform.position.y + 3f;
            p.y = GroundY(p, from); front.y = GroundY(front, from); rear.y = GroundY(rear, from);
            Vector3 fwd = front - rear;
            if (fwd.sqrMagnitude < 0.01f) fwd = dir;
            Place(p, Quaternion.LookRotation(fwd.normalized, Vector3.up), false);
        }

        /// <summary>Every machine: wheels, blackout lamp and engine from what the lorry is doing.</summary>
        void LateUpdate()
        {
            Vector3 moved = Flat(transform.position - lastPos);
            lastPos = transform.position;
            bool visible = Visible;
            float dist = moved.magnitude;
            if (visible && dist < 20f && wheels != null)
            {
                float deg = dist / wheelRadius * Mathf.Rad2Deg * Mathf.Sign(Vector3.Dot(moved, transform.forward) + 1e-4f);
                foreach (var w in wheels) if (w) w.RotateAround(w.position, transform.right, deg);
            }
            bool running = visible && (!stopOnAlarm || !SoldierAI.Alarm);
            if (lamps != null) foreach (var l in lamps) if (l) l.enabled = running;
            if (running && !engine.isPlaying) engine.Play(); else if (!running && engine.isPlaying) engine.Stop();
        }

        /// <summary>The players have taken the lorry (TruckController): stop driving it, for good, on this machine.</summary>
        public void Release()
        {
            parked = true;
            if (engine && engine.isPlaying) engine.Stop();
            if (lamps != null) foreach (var l in lamps) if (l) l.enabled = false;
            enabled = false;
        }

        void Place(Vector3 p, Quaternion r, bool jump)
        {
#if STANS_NET
            if (jump && Net.Active)
            {
                var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
                if (nt && nt.IsSpawned) { nt.Teleport(p, r, transform.localScale); lastPos = p; return; }
            }
#endif
            transform.SetPositionAndRotation(p, r);
            if (jump) lastPos = p;
        }

        Vector3 PointAt(float d, out Vector3 dir)
        {
            for (int i = 0; i < route.Length - 1; i++)
            {
                Vector3 a = Flat(route[i]), b = Flat(route[i + 1]);
                float len = Vector3.Distance(a, b);
                if (d <= len || i == route.Length - 2)
                {
                    dir = (b - a).normalized;
                    return Vector3.Lerp(route[i], route[i + 1], len > 0f ? Mathf.Clamp01(d / len) : 0f);
                }
                d -= len;
            }
            dir = Vector3.forward;
            return route[route.Length - 1];
        }

        /// <summary>The road surface under a point: the bridge deck or the ground, never the lorry itself.</summary>
        float GroundY(Vector3 p, float from)
        {
            if (from < Hidden.y + HiddenDepth * 0.5f) from = p.y + 3f;   // coming up from the parking spot
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, from, p.z), Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider is CharacterController) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? p.y : best;
        }
    }
}
