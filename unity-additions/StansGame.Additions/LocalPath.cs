using System.Collections.Generic;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Small grid pathfinder for getting a soldier out of a pocket (between a glider, wreckage and
    /// wire, for example). It plans on a 0.7 m grid in a 44 m square around the soldier, against the
    /// real colliders, and returns the route to the reachable cell closest to the goal. Steps up to
    /// <see cref="Obstacle.MaxHeight"/> count as climbable (AiUnstick climbs them on the way).
    /// Other soldiers are ignored: they move.
    /// </summary>
    public static class LocalPath
    {
        const float Cell = 0.7f;
        const int Half = 31;                 // 63 x 63 cells
        const int Size = Half * 2 + 1;
        const int MaxExpand = 3500;
        const float Unknown = float.MinValue;

        static readonly float[] ground = new float[Size * Size];
        static readonly bool[] known = new bool[Size * Size];
        static readonly float[] cost = new float[Size * Size];
        static readonly int[] from = new int[Size * Size];
        static readonly bool[] closed = new bool[Size * Size];
        static readonly RaycastHit[] hits = new RaycastHit[16];
        static readonly Collider[] overlaps = new Collider[16];
        static int lastFrame = -1;

        /// <summary>Only one plan per frame across all soldiers, to keep the frame time even.</summary>
        public static bool CanPlanThisFrame { get { return lastFrame != Time.frameCount; } }

        public static List<Vector3> Find(CharacterController cc, Vector3 goal)
        {
            lastFrame = Time.frameCount;
            Vector3 start = cc.transform.position;
            float feetY = start.y;
            float r = cc.radius * 0.9f;

            for (int i = 0; i < known.Length; i++) { known[i] = false; closed[i] = false; cost[i] = float.MaxValue; from[i] = -1; }

            int s = Index(Half, Half);
            ground[s] = feetY;
            known[s] = true;
            cost[s] = 0f;

            // Simple open list: a binary heap would be faster, but 3,500 expansions is cheap enough.
            List<int> open = new List<int>();
            open.Add(s);
            int best = s;
            float bestH = Flat(start - goal);
            int expanded = 0;

            while (open.Count > 0 && expanded < MaxExpand)
            {
                int oi = 0;
                float of = float.MaxValue;
                for (int i = 0; i < open.Count; i++)
                {
                    float f = cost[open[i]] + Flat(Pos(open[i], start) - goal);
                    if (f < of) { of = f; oi = i; }
                }
                int c = open[oi];
                open[oi] = open[open.Count - 1];
                open.RemoveAt(open.Count - 1);
                if (closed[c]) continue;
                closed[c] = true;
                expanded++;

                float h = Flat(Pos(c, start) - goal);
                if (h < bestH) { bestH = h; best = c; }
                if (h < 1.5f) break;

                int cx = c % Size, cz = c / Size;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (nx < 0 || nz < 0 || nx >= Size || nz >= Size) continue;
                    int n = Index(nx, nz);
                    if (closed[n]) continue;
                    if (!known[n]) { ground[n] = Probe(Pos(n, start), feetY, r, cc.height); known[n] = true; }
                    if (ground[n] == Unknown) continue;
                    float rise = ground[n] - ground[c];
                    if (rise > Obstacle.MaxHeight || rise < -2.5f) continue;
                    // Diagonal moves must not cut a blocked corner.
                    if (dx != 0 && dz != 0)
                    {
                        int a = Index(cx + dx, cz), b = Index(cx, cz + dz);
                        if (!known[a]) { ground[a] = Probe(Pos(a, start), feetY, r, cc.height); known[a] = true; }
                        if (!known[b]) { ground[b] = Probe(Pos(b, start), feetY, r, cc.height); known[b] = true; }
                        if (ground[a] == Unknown || ground[b] == Unknown) continue;
                    }
                    float step = (dx != 0 && dz != 0 ? 1.414f : 1f) * Cell;
                    if (rise > Obstacle.MinHeight) step += 3f;   // climbing is slower than walking round
                    float nc = cost[c] + step;
                    if (nc < cost[n]) { cost[n] = nc; from[n] = c; open.Add(n); }
                }
            }

            if (best == s || bestH > Flat(start - goal) - 2f) return null;   // no real progress possible

            List<Vector3> path = new List<Vector3>();
            for (int c = best; c != -1; c = from[c])
            {
                Vector3 p = Pos(c, start);
                p.y = ground[c];
                path.Add(p);
            }
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Nearest spot within 8 m where the soldier can stand on the ground (not on top of something
        /// higher than where it is now), preferring spots towards the goal. For soldiers wedged in
        /// wreckage that neither climbing nor a route gets out.
        /// </summary>
        public static bool FindFreeSpot(CharacterController cc, Vector3 goal, out Vector3 spot)
        {
            Vector3 here = cc.transform.position;
            float r = cc.radius * 0.9f;
            spot = here;
            for (float ring = 1f; ring <= 8f; ring += 0.7f)
            {
                bool found = false;
                float bestScore = float.MaxValue;
                for (int a = 0; a < 360; a += 20)
                {
                    Vector3 p = here + Quaternion.Euler(0f, a, 0f) * Vector3.forward * ring;
                    float y = Probe(p, here.y, r, cc.height);
                    if (y == Unknown || y > here.y + 0.3f) continue;
                    p.y = y;
                    float score = Flat(p - goal);
                    if (score < bestScore) { bestScore = score; spot = p; found = true; }
                }
                if (found) return true;
            }
            return false;
        }

        static int Index(int x, int z) { return z * Size + x; }

        static Vector3 Pos(int i, Vector3 start)
        {
            return new Vector3(start.x + (i % Size - Half) * Cell, start.y, start.z + (i / Size - Half) * Cell);
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        /// <summary>Ground height at the cell, or Unknown when a soldier cannot stand there.</summary>
        static float Probe(Vector3 p, float feetY, float r, float height)
        {
            Vector3 top = new Vector3(p.x, feetY + Obstacle.MaxHeight + 1.2f, p.z);
            int n = Physics.RaycastNonAlloc(top, Vector3.down, hits, Obstacle.MaxHeight + 4.5f, ~0, QueryTriggerInteraction.Ignore);
            float y = Unknown;
            float nearest = float.MaxValue;
            Collider surface = null;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider is CharacterController) continue;
                if (hits[i].distance < nearest) { nearest = hits[i].distance; y = hits[i].point.y; surface = hits[i].collider; }
            }
            if (Obstacle.IsGlider(surface)) return Unknown;   // never plan a route over a glider
            if (y == Unknown) return Unknown;
            Vector3 g = new Vector3(p.x, y, p.z);
            int m = Physics.OverlapCapsuleNonAlloc(g + Vector3.up * (r + 0.4f), g + Vector3.up * Mathf.Max(r + 0.41f, height - r), r, overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < m; i++)
                if (!(overlaps[i] is CharacterController)) return Unknown;
            return y;
        }
    }
}
