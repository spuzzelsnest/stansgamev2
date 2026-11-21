using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Makes a parachute canopy behave like silk without a cloth simulation. In the air the dome breathes and its skirt
    /// ripples in the wind. On landing the air goes out of it: the apex sags first, the canopy tips over downwind and
    /// the nylon settles onto the ground in loose folds, with the rigging lines stretched back towards the jumper.
    /// The canopy root's origin is where the risers meet, with +Y up the lines.
    /// </summary>
    public class CanopyCloth : MonoBehaviour
    {
        public float breathe = 0.035f;        // radial pulse of the dome
        public float skirtFlutter = 0.12f;    // ripple of the lower edge, metres
        public float collapseSeconds = 3.2f;

        class Part
        {
            public MeshFilter mf;
            public Mesh mesh;
            public Vector3[] rest;    // canopy-root space
            public Vector3[] target;  // laid out on the ground, canopy-root space after the collapse starts
            public float[] delay, seed;
            public Vector3[] work;
            public Matrix4x4 toRoot, fromRoot;
        }

        readonly List<Part> parts = new List<Part>();
        float height = 1f, maxRadius = 1f, phase, collapseStart = -1f;
        int frame;

        void Awake()
        {
            phase = Random.value * 10f;
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var p = new Part { mf = mf, mesh = mf.mesh };   // a copy of its own: every canopy moves differently
                p.mesh.MarkDynamic();
                p.toRoot = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                p.fromRoot = p.toRoot.inverse;
                var v = p.mesh.vertices;
                p.rest = new Vector3[v.Length]; p.work = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    p.rest[i] = p.toRoot.MultiplyPoint3x4(v[i]);
                    height = Mathf.Max(height, p.rest[i].y);
                    maxRadius = Mathf.Max(maxRadius, new Vector2(p.rest[i].x, p.rest[i].z).magnitude);
                }
                parts.Add(p);
            }
        }

        public bool Collapsing => collapseStart >= 0f;

        /// <summary>The jumper is down: let the air out. The canopy is unparented and laid out downwind of him.</summary>
        public void Collapse(Vector3 jumperFeet, Vector3 wind, float jumperYaw)
        {
            if (Collapsing) return;
            Vector3 down = wind; down.y = 0f;
            if (down.sqrMagnitude < 0.01f) down = Quaternion.Euler(0f, jumperYaw, 0f) * Vector3.forward;
            down.Normalize();

            // Keep the canopy where it is in the world while we take it off the jumper, then pose the root on the
            // ground at his feet facing downwind. The rest pose is re-expressed in the new root space so the
            // collapse starts exactly from what the player saw.
            var worldRest = new List<Vector3[]>();
            foreach (var p in parts)
            {
                var w = new Vector3[p.rest.Length];
                var cur = p.mesh.vertices;
                for (int i = 0; i < w.Length; i++) w[i] = p.mf.transform.TransformPoint(cur[i]);
                worldRest.Add(w);
            }
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(jumperFeet, Quaternion.LookRotation(down, Vector3.up));
            transform.localScale = Vector3.one;   // lay-out distances are in metres

            // ground along the lay-out line, so the silk follows a sloping street or field
            float len = height * 0.95f;
            float g0 = Ground(jumperFeet, jumperFeet.y), g1 = Ground(jumperFeet + down * len, jumperFeet.y);
            float slope = (g1 - g0) / Mathf.Max(1f, len);
            float baseY = g0 - jumperFeet.y;

            for (int k = 0; k < parts.Count; k++)
            {
                var p = parts[k];
                p.toRoot = transform.worldToLocalMatrix * p.mf.transform.localToWorldMatrix;
                p.fromRoot = p.toRoot.inverse;
                var w = worldRest[k];
                p.target = new Vector3[w.Length]; p.delay = new float[w.Length]; p.seed = new float[w.Length];
                var orig = p.rest;   // in the old (upright) root frame: tells us where on the canopy each vertex sits
                for (int i = 0; i < w.Length; i++)
                {
                    Vector3 o = orig[i];
                    float h = Mathf.Clamp01(o.y / height);                     // 0 at the risers, 1 at the apex
                    float dome = Mathf.InverseLerp(0.55f, 0.75f, h);           // 1 on the fabric, 0 on the lines
                    float s = Hash(o);
                    // lay-out: lines stretch back to the jumper, the fabric piles up at the far end in folds
                    float along = h * len + o.z * 0.35f * dome;
                    float across = o.x * Mathf.Lerp(0.15f, 0.75f, dome) + Mathf.Sin(o.z * 2.3f + s * 6f) * 0.25f * dome;
                    float fold = dome * (0.06f + 0.32f * Mathf.Abs(Mathf.Sin(o.x * 1.7f + o.z * 1.1f + s))) * (1f - 0.4f * h)
                                 + 0.03f;
                    p.target[i] = new Vector3(across, baseY + slope * along + fold, along);
                    // the apex goes first, the skirt follows, the lines last; a little scatter so it doesn't move as one
                    p.delay[i] = (1f - h) * 0.45f + s * 0.2f + (1f - dome) * 0.25f;
                    p.seed[i] = s;
                    p.rest[i] = p.toRoot.MultiplyPoint3x4(p.mf.transform.InverseTransformPoint(w[i]));
                }
            }
            collapseStart = Time.time;
        }

        static float Ground(Vector3 at, float fallback)
        {
            return Physics.Raycast(at + Vector3.up * 6f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y : fallback;
        }

        static float Hash(Vector3 v)
        {
            float n = Mathf.Sin(v.x * 12.9898f + v.y * 78.233f + v.z * 37.719f) * 43758.5453f;
            return n - Mathf.Floor(n);
        }

        void LateUpdate()
        {
            if (parts.Count == 0) return;
            frame++;
            if (Collapsing) CollapseStep();
            else if (frame % 2 == 0) Breathe();   // half rate is plenty for a slow pulse
        }

        void Breathe()
        {
            float t = Time.time + phase;
            float pulse = 1f + breathe * Mathf.Sin(t * 2.1f) + breathe * 0.4f * Mathf.Sin(t * 3.7f + 1.3f);
            foreach (var p in parts)
            {
                for (int i = 0; i < p.rest.Length; i++)
                {
                    Vector3 r = p.rest[i];
                    float h = r.y / height;
                    float rad = new Vector2(r.x, r.z).magnitude / maxRadius;
                    // the skirt is the widest, lowest part of the fabric
                    float skirt = Mathf.Clamp01((rad - 0.75f) * 4f) * Mathf.Clamp01(1f - Mathf.Abs(h - 0.68f) * 5f);
                    float ang = Mathf.Atan2(r.z, r.x);
                    float wave = Mathf.Sin(ang * 9f + t * 5.5f) * 0.6f + Mathf.Sin(ang * 5f - t * 3.1f) * 0.4f;
                    float k = Mathf.Lerp(1f, pulse, Mathf.Clamp01(h * 1.4f));
                    var q = new Vector3(r.x * k, r.y + (pulse - 1f) * -1.5f * h, r.z * k);
                    q.y += wave * skirtFlutter * skirt;
                    q += new Vector3(r.x, 0f, r.z).normalized * (wave * skirtFlutter * 0.5f * skirt);
                    p.work[i] = p.fromRoot.MultiplyPoint3x4(q);
                }
                p.mesh.vertices = p.work;
                p.mesh.RecalculateBounds();
            }
        }

        void CollapseStep()
        {
            float t = (Time.time - collapseStart) / collapseSeconds;
            bool settled = t > 1.6f;
            foreach (var p in parts)
            {
                for (int i = 0; i < p.rest.Length; i++)
                {
                    float u = Mathf.Clamp01((t - p.delay[i]) / 0.75f);
                    // fabric has no weight to speak of: it slides and sinks rather than drops
                    float e = u * u * (3f - 2f * u);
                    Vector3 a = p.rest[i], b = p.target[i];
                    Vector3 q = Vector3.LerpUnclamped(a, b, e);
                    // a last billow of trapped air carries it downwind as it sinks
                    float billow = Mathf.Sin(u * Mathf.PI);
                    q.z += billow * 1.2f * (0.5f + p.seed[i]);
                    q.y += billow * 0.6f * Mathf.Clamp01(a.y / height);
                    // and the silk keeps rippling for a moment after it is down
                    if (u >= 1f)
                    {
                        float after = Mathf.Clamp01(1.6f - t) / 0.6f;
                        q.y += Mathf.Sin(b.z * 3f + b.x * 2f - Time.time * 7f) * 0.05f * after * Mathf.Clamp01(b.y * 6f);
                    }
                    p.work[i] = p.fromRoot.MultiplyPoint3x4(q);
                }
                p.mesh.vertices = p.work;
                if (frame % 2 == 0 || settled) { p.mesh.RecalculateNormals(); p.mesh.RecalculateBounds(); }
            }
            if (settled) enabled = false;   // lies where it fell
        }
    }
}
