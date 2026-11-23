using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Where the water is (the canal, the Orne): every water surface with a WaterRipple registers here.
    /// Used for swimming (soldiers float with head and shoulders out, and climb out at the quay walls)
    /// and to keep the AI from walking into the canal.
    /// </summary>
    public static class Water
    {
        static readonly List<Renderer> surfaces = new List<Renderer>();

        public static void Register(Renderer r) { if (r && !surfaces.Contains(r)) surfaces.Add(r); }
        public static void Unregister(Renderer r) => surfaces.Remove(r);

        /// <summary>The height of the water surface nearest to p (within maxDistance across): for the reflections.</summary>
        public static bool NearestSurface(Vector3 p, float maxDistance, out float y)
        {
            y = 0f; float best = maxDistance * maxDistance; bool found = false;
            foreach (var r in surfaces)
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                var q = b.ClosestPoint(new Vector3(p.x, b.center.y, p.z));
                float d = (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z);
                if (d < best || (found && Mathf.Approximately(d, best) && b.center.y > y)) { best = d; y = b.center.y; found = true; }
            }
            return found;
        }

        /// <summary>Is there water over this point? Returns its surface height.</summary>
        public static bool SurfaceAt(Vector3 p, out float y)
        {
            y = float.NegativeInfinity;
            bool found = false;
            foreach (var r in surfaces)
            {
                if (!r) continue;
                var b = r.bounds;
                if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
                float sy = b.center.y;
                if (p.y > sy + 0.3f || p.y < sy - 12f) continue;    // standing on the bank above it, or far below
                if (!found || sy > y) { y = sy; found = true; }
            }
            return found;
        }

        /// <summary>Deep enough to swim: the feet are more than ~1.1 m under the surface.</summary>
        public static bool Swimming(Vector3 feet, out float surface) => SurfaceAt(feet, out surface) && feet.y < surface - 1.1f;

        /// <summary>Feet height while floating: head and shoulders out of the water.</summary>
        public static float FloatFeet(float surface) => surface - 1.25f;

        /// <summary>
        /// A place to climb out: a wall or bank within reach in this direction whose top is no more than
        /// ~2.4 m above the water (the canal quays are ~1.8 m). Returns the spot on top to climb to.
        /// </summary>
        public static bool FindClimb(Vector3 feet, Vector3 dir, float surface, Transform self, out Vector3 top)
        {
            top = default;
            dir.y = 0f; if (dir.sqrMagnitude < 0.01f) return false; dir.Normalize();
            Vector3 chest = new Vector3(feet.x, surface + 0.2f, feet.z);
            if (!Physics.Raycast(chest, dir, out RaycastHit wall, 1.3f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (self && wall.collider.transform.IsChildOf(self)) return false;
            Vector3 probe = wall.point + dir * 0.7f + Vector3.up * 3.5f;
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit floor, 6f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (floor.normal.y < 0.6f) return false;
            float rise = floor.point.y - surface;
            if (rise < -0.2f || rise > 2.4f) return false;
            top = floor.point;
            return true;
        }

        // ---------------------------------------------------------------- the canal and its one crossing

        const float BridgeHalfWidth = 2.8f, BridgeEnd = 16f;   // the 1934 bridge: ~5.5 m roadway, ~31 m long
        /// <summary>Set by BridgeLift while the bridge is up (or moving): nobody can cross the canal.</summary>
        public static bool BridgeOpen;
        // where to wait for the bridge on each bank (the east one clear of the counterweight pit)
        const float WaitWest = -22f, WaitEast = 23.5f;
        /// <summary>The east quay (the face the rolling segments sit behind), as |x| from the bridge's centre line.</summary>
        public const float QuayHalf = 11.2f;
        /// <summary>
        /// The west quay. In 1944 the lifting span did not reach the west bank: its free end rested on a concrete pier
        /// standing in the canal, and a short fixed concrete span joined the pier to the west quay ("Pegasus bridge -
        /// river side": photo from the canal; the June 1944 vertical gives the fixed span ~22% of the canal's width).
        /// </summary>
        public const float WestQuay = 19.0f;
        /// <summary>The west quay runs this far up and down the canal from the bridge; beyond, natural banks.</summary>
        public const float QuayLength = 16.5f;
        /// <summary>
        /// The east bank is a long buttressed concrete quay (pb01: behind the gliders; the June 1944 verticals show the
        /// straight bank both ways from the bridge): this far south (z negative) and north of the bridge.
        /// </summary>
        public const float EastQuaySouth = 85f, EastQuayNorth = 40f;

        /// <summary>The quay face on this side (|x|) at distance z along the canal, or -1 where the bank is natural.</summary>
        public static float QuayFace(float z, float side)
        {
            if (side < 0f) return Mathf.Abs(z) <= QuayLength ? WestQuay : -1f;
            return z >= -EastQuaySouth && z <= EastQuayNorth ? QuayHalf : -1f;
        }
        /// <summary>
        /// Half width of the open canal in 1944, away from the bridge: about 38 m of water, measured against the 31 m
        /// bridge on the RAF photos (the vertical of June 1944 gives ~40 m, the oblique of 24 March ~34 m). The canal
        /// was widened in the early 1960s (the bridge was lengthened by 5 m), so today's - and the LiDAR's - is wider.
        /// </summary>
        public const float OpenHalf = 19.0f;

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>
        /// The water's edge of the Caen canal on one side (side -1 west, +1 east), as |x| in metres, at distance z
        /// along the canal from the bridge. June 1944 vertical photographs: concrete quays at the bridge (the east one long);
        /// away from them the canal, with natural banks that wander a little, and a broad sandy shore on the
        /// east side beside the landing field.
        /// </summary>
        public static float CanalShore(float z, float side)
        {
            float quay = side < 0f ? WestQuay : QuayHalf;
            float t = side < 0f ? Smooth(QuayLength, 42f, Mathf.Abs(z))
                                : Mathf.Max(Smooth(EastQuaySouth, EastQuaySouth + 22f, -z), Smooth(EastQuayNorth, EastQuayNorth + 22f, z));
            float open = OpenHalf + 0.9f * Mathf.Sin(z * 0.019f + side * 1.9f) + 0.55f * Mathf.Sin(z * 0.061f + side * 0.7f + 1.3f);
            if (side > 0f) open += 1.3f * Smooth(25f, 60f, -z) * (1f - Smooth(140f, 190f, -z));   // the shore by LZ X
            return Mathf.Lerp(quay, open, t);
        }

        static bool InCanal(float x, float z) => Mathf.Abs(x) < CanalShore(z, Mathf.Sign(x)) + 0.8f && Mathf.Abs(z) > BridgeHalfWidth;

        /// <summary>
        /// Where to walk next to get to dest without going into the canal: straight there if the way is dry,
        /// otherwise to this side's end of the bridge, then over it.
        /// </summary>
        public static Vector3 Waypoint(Vector3 from, Vector3 dest)
        {
            bool crosses = false;
            for (int i = 1; i <= 16 && !crosses; i++)
            {
                var q = Vector3.Lerp(from, dest, i / 16f);
                crosses = InCanal(q.x, q.z);
            }
            if (!crosses) return dest;
            if (BridgeOpen) return new Vector3(from.x < 0f ? WaitWest : WaitEast, from.y, Mathf.Clamp(from.z, -2f, 2f));   // wait for it to come down
            bool onBridge = Mathf.Abs(from.z) < BridgeHalfWidth - 0.8f && Mathf.Abs(from.x) < BridgeEnd + 2f;
            if (onBridge) return new Vector3(Mathf.Sign(dest.x) * BridgeEnd, from.y, 0f);   // over to the far end
            // to this side's end of the bridge (if dest is on this side the line only clips the water: go round)
            return new Vector3(Mathf.Sign(from.x) * BridgeEnd, from.y, Mathf.Clamp(from.z, -1f, 1f) * 0f);
        }
    }
}
