using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Gets AI soldiers moving again when they run into something. The AI steers in a straight line
    /// with no pathfinding, so the British sections that leave the gliders walk into the wreckage
    /// and stay there. When a soldier wants to move but has not got anywhere for a second:
    /// <list type="number">
    /// <item>a low obstacle straight ahead is climbed with the AI's own climb movement (the canal bank one);</item>
    /// <item>when advancing or following the player, a route out is planned with <see cref="LocalPath"/> and
    /// the soldier is led along it by moving a temporary advance target, then handed back its own target;</item>
    /// <item>otherwise (in a fight) it sidesteps to the more open side.</item>
    /// </list>
    /// </summary>
    public sealed class AiUnstick : MonoBehaviour
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo velField = typeof(SoldierAI).GetField("vel", Any);
        static readonly FieldInfo climbing = typeof(SoldierAI).GetField("climbing", Any);
        static readonly FieldInfo climbFrom = typeof(SoldierAI).GetField("climbFrom", Any);
        static readonly FieldInfo climbTo = typeof(SoldierAI).GetField("climbTo", Any);
        static readonly FieldInfo climbT = typeof(SoldierAI).GetField("climbT", Any);
        static readonly FieldInfo targetField = typeof(SoldierAI).GetField("target", Any);

        public static int Climbs, Routes, Detours, RoutesDone, Rescues;

        SoldierAI ai;
        CharacterController cc;
        Health health;
        Vector3 lastPos;
        float stuckFor;
        float nextCheck;
        Vector3 detourDir;
        float detourUntil;
        Vector3 progressPos;
        float progressSince;

        // Route following
        List<Vector3> route;
        int routeIndex;
        Transform waypoint;
        Transform savedTarget;
        SoldierAI.Mode savedMode;
        float routeUntil;
        float nextRouteAllowed;

        void Awake()
        {
            ai = GetComponent<SoldierAI>();
            cc = GetComponent<CharacterController>();
            health = GetComponent<Health>();
            lastPos = transform.position;
            progressPos = transform.position;
            progressSince = Time.time;
            nextCheck = Time.time + Random.Range(0f, 0.25f);   // spread the checks over frames
        }

        static bool Fields() { return velField != null && climbing != null && climbFrom != null && climbTo != null && climbT != null; }

        void OnDisable() { EndRoute(); }
        void OnDestroy() { EndRoute(); if (waypoint != null) Destroy(waypoint.gameObject); }

        void Update()
        {
            // Only where the AI is simulated (offline or co-op host); clients just show the host's result.
            if (ai == null || cc == null || !ai.enabled || !Fields() || !Net.Simulates) return;
            if (health != null && health.IsDead) { EndRoute(); enabled = false; return; }
            if ((bool)climbing.GetValue(ai)) { stuckFor = 0f; detourUntil = 0f; lastPos = transform.position; return; }

            if (route != null) FollowRoute();

            // Sidestepping: push along the obstacle on top of the AI's own movement.
            if (Time.time < detourUntil && cc.enabled) cc.Move(detourDir * 2.6f * Time.deltaTime);

            if (Time.time < nextCheck) return;
            float dt = 0.25f + (Time.time - nextCheck);
            nextCheck = Time.time + 0.25f;

            Vector3 vel = (Vector3)velField.GetValue(ai);
            vel.y = 0f;
            Vector3 moved = transform.position - lastPos;
            moved.y = 0f;
            lastPos = transform.position;

            bool wantsToMove = vel.magnitude > 1f;
            bool getsNowhere = moved.magnitude < 0.25f * Mathf.Max(0.5f, vel.magnitude) * dt;

            // Last resort: still in the same place after 8 s of trying (wedged in wreckage, on top of a
            // glider): put the soldier on the nearest free ground towards where it is going.
            if (!wantsToMove || Flat(transform.position - progressPos) > 1.5f) { progressPos = transform.position; progressSince = Time.time; }
            else if (Time.time - progressSince > 8f && cc.enabled)
            {
                progressSince = Time.time;
                Vector3 spot;
                if (LocalPath.FindFreeSpot(cc, transform.position + vel.normalized * 10f, out spot))
                {
                    EndRoute();
                    cc.enabled = false;
                    transform.position = spot + Vector3.up * 0.05f;
                    cc.enabled = true;
                    progressPos = transform.position;
                    Rescues++;
                    return;
                }
            }

            if (!wantsToMove || !getsNowhere || !cc.enabled) { stuckFor = 0f; return; }
            stuckFor += dt;
            if (stuckFor < 0.9f) return;
            stuckFor = 0f;

            Vector3 dir = vel.normalized;
            Vector3 top;
            bool person = false;
            if (cc.isGrounded && Obstacle.FindLedge(cc, dir, 0.8f, out top, out person))
            {
                climbing.SetValue(ai, true);
                climbFrom.SetValue(ai, transform.position);
                climbTo.SetValue(ai, top + Vector3.up * 0.05f);
                climbT.SetValue(ai, 0f);
                cc.enabled = false;   // SoldierAI.Climb turns it back on when the climb ends
                Climbs++;
                return;
            }

            if (route == null && !person && TryRoute()) return;

            Vector3 left = Vector3.Cross(Vector3.up, dir);
            Vector3 side = Open(left) >= Open(-left) ? left : -left;
            detourDir = (side * 0.85f + dir * 0.15f).normalized;
            detourUntil = Time.time + (person ? 0.6f : 1.2f);
            Detours++;
        }

        bool InFight()
        {
            return targetField != null && targetField.GetValue(ai) as Object != null;
        }

        bool TryRoute()
        {
            if (Time.time < nextRouteAllowed || InFight() || !LocalPath.CanPlanThisFrame) return false;
            Vector3 goal;
            if (ai.mode == SoldierAI.Mode.Advance && ai.advanceTarget != null) goal = ai.advanceTarget.position;
            else if (ai.mode == SoldierAI.Mode.FollowPlayer && AdditionsBootstrap.LocalPlayer() != null) goal = AdditionsBootstrap.LocalPlayer().transform.position;
            else return false;

            nextRouteAllowed = Time.time + 4f;
            List<Vector3> path = LocalPath.Find(cc, goal);
            if (path == null || path.Count < 3) return false;

            if (waypoint == null)
            {
                waypoint = new GameObject(name + " route point").transform;
                waypoint.gameObject.hideFlags = HideFlags.HideInHierarchy;
            }
            route = path;
            routeIndex = 0;
            savedTarget = ai.advanceTarget;
            savedMode = ai.mode;
            ai.mode = SoldierAI.Mode.Advance;
            ai.advanceTarget = waypoint;
            routeUntil = Time.time + 4f + path.Count * 0.6f;
            Routes++;
            FollowRoute();
            return true;
        }

        void FollowRoute()
        {
            if (InFight() || Time.time > routeUntil) { EndRoute(); return; }
            Vector3 here = transform.position;
            // Keep the point more than 2 m ahead: the AI stops advancing within 2 m of its target.
            while (routeIndex < route.Count - 1 && Flat(route[routeIndex] - here) < 2.8f) routeIndex++;
            if (routeIndex >= route.Count - 1 && Flat(route[route.Count - 1] - here) < 2.2f)
            {
                RoutesDone++;
                EndRoute();
                return;
            }
            waypoint.position = route[routeIndex];
        }

        void EndRoute()
        {
            if (route == null) return;
            route = null;
            if (ai != null && ai.advanceTarget == waypoint)
            {
                ai.advanceTarget = savedTarget;
                ai.mode = savedMode;
            }
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        float Open(Vector3 dir)
        {
            Vector3 feet = transform.position + cc.center - Vector3.up * (cc.height * 0.5f);
            float r = cc.radius * 0.9f;
            RaycastHit hit;
            if (Physics.CapsuleCast(feet + Vector3.up * (cc.stepOffset + r + 0.05f), feet + Vector3.up * (cc.height - r), r, dir, out hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                return hit.distance;
            return 6f;
        }
    }
}
