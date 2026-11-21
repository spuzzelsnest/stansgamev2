using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Airspeed AS.51 Horsa Mk I, built as real meshes with painted textures (after the three-view in
    /// _downloads/Horsa glider.png and the June 1944 photos pb03, pb06, pb13):
    /// 20.4 m long, 26.8 m span. A long rounded fuselage whose belly sweeps up into a slim tail boom, the blunt
    /// glazed nose, a shoulder wing with a straight trailing edge and tapered leading edge with rounded tips, high
    /// tailplane and a tall fin with a rounded top. Dark Green / Dark Earth disruptive camouflage on top, night black
    /// below and on the fuselage sides; Type B roundels on the upper wing, Type C1 on the fuselage, fin flash,
    /// round cabin windows, the chalk number on the nose, and the D-Day stripes (3 white, 2 black) on the wings
    /// and round the rear fuselage.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string GenMeshDir = Root + "/Models/Generated";
        const string GenTexDir = Root + "/Textures/Generated";
        const float HorsaCy = 2.2f;   // fuselage centre line above the ground, wheels down

        // fuselage stations from the nose (+z) to the tail: z, half width, half height, centre-line offset
        static readonly float[,] HorsaStations =
        {
            { 10.05f, 0.05f, 0.05f, -0.16f },
            {  9.92f, 0.45f, 0.50f, -0.15f },
            {  9.70f, 0.75f, 0.86f, -0.12f },
            {  9.35f, 0.98f, 1.12f, -0.08f },
            {  8.80f, 1.12f, 1.30f, -0.03f },
            {  8.00f, 1.19f, 1.39f,  0.00f },
            {  6.00f, 1.20f, 1.40f,  0.02f },
            {  2.00f, 1.20f, 1.40f,  0.02f },
            { -1.00f, 1.14f, 1.30f,  0.10f },
            { -3.50f, 0.95f, 1.02f,  0.36f },
            { -6.00f, 0.70f, 0.74f,  0.62f },
            { -8.50f, 0.50f, 0.52f,  0.80f },
            { -9.90f, 0.36f, 0.38f,  0.88f },
            { -10.35f, 0.10f, 0.12f, 0.92f },
        };
        const float SuperN = 2.6f;   // cross-section: between an ellipse (2) and a rounded box

        static readonly Color32 CDarkGreen = new Color(0.22f, 0.25f, 0.15f);
        static readonly Color32 CDarkEarth = new Color(0.38f, 0.30f, 0.19f);
        static readonly Color32 CNight = new Color(0.045f, 0.045f, 0.05f);
        static readonly Color32 CWhite = new Color(0.88f, 0.88f, 0.85f);
        static readonly Color32 CStripeBlack = new Color(0.03f, 0.03f, 0.03f);
        static readonly Color32 CBlue = new Color(0.13f, 0.18f, 0.42f);
        static readonly Color32 CRed = new Color(0.62f, 0.12f, 0.10f);
        static readonly Color32 CYellow = new Color(0.86f, 0.72f, 0.20f);
        static readonly Color32 CGlass = new Color(0.10f, 0.13f, 0.16f);
        static readonly Color32 CPorthole = new Color(0.30f, 0.34f, 0.38f);

        static GameObject BuildHorsa(string name, Transform parent, int chalk)
        {
            foreach (var d in new[] { Root + "/Models", GenMeshDir, Root + "/Textures", GenTexDir })
                if (!AssetDatabase.IsValidFolder(d)) AssetDatabase.CreateFolder(Path.GetDirectoryName(d).Replace('\\', '/'), Path.GetFileName(d));

            var g = new GameObject(name); g.transform.SetParent(parent);
            var T = g.transform;

            // ---- meshes (shared by all three gliders) and painted textures (the fuselage carries the chalk number)
            var fuselageMesh = SaveMesh(HorsaFuselageMesh(HorsaBreakZ, HorsaNoseZ), "Horsa_Fuselage_Front");
            var noseMesh = HorsaFuselageMesh(HorsaNoseZ, float.MaxValue);
            // the nose in two: the glazed upper half (it smashed in the landings) and the plywood lower half
            float cyNose = HorsaCy + HorsaProfile(9.0f).c;
            var noseGlazingMesh = SaveMesh(KeepTriangles(noseMesh, c => c.y > cyNose - 0.25f, "Horsa_Nose_Glazing"), "Horsa_Nose_Glazing");
            var noseLowerMesh = SaveMesh(KeepTriangles(noseMesh, c => c.y <= cyNose - 0.25f, "Horsa_Nose_Lower"), "Horsa_Nose_Lower");
            var rearMesh = SaveMesh(HorsaFuselageMesh(float.MinValue, HorsaBreakZ), "Horsa_Fuselage_Rear");
            var wingFull = (Surface("Horsa_Wing", -13.4f, 13.4f, true, true,
                s => Mathf.Lerp(4.0f, 2.3f, Mathf.Abs(s) / 13.4f), s => Mathf.Lerp(-0.4f, -0.2f, Mathf.Abs(s) / 13.4f),
                s => Mathf.Lerp(0.75f, 0.24f, Mathf.Abs(s) / 13.4f), 1.3f, 0.62f,
                (s, t, z) => new Vector3(s, HorsaCy + 1.62f + Mathf.Abs(s) * 0.03f + t, z), 64, 18));
            // the wing in three: centre section and the two outer panels (they could tear at the joint)
            var wingMesh = SaveMesh(KeepTriangles(wingFull, c => Mathf.Abs(c.x) <= HorsaWingJoint, "Horsa_Wing_Centre"), "Horsa_Wing_Centre");
            var wingPortMesh = SaveMesh(KeepTriangles(wingFull, c => c.x < -HorsaWingJoint, "Horsa_Wing_Port"), "Horsa_Wing_Port");
            var wingStbdMesh = SaveMesh(KeepTriangles(wingFull, c => c.x > HorsaWingJoint, "Horsa_Wing_Starboard"), "Horsa_Wing_Starboard");
            var tailMesh = SaveMesh(Surface("Horsa_Tailplane", -5.0f, 5.0f, true, true,
                s => Mathf.Lerp(-7.5f, -7.95f, Mathf.Abs(s) / 5f), s => Mathf.Lerp(-9.8f, -9.55f, Mathf.Abs(s) / 5f),
                s => Mathf.Lerp(0.24f, 0.10f, Mathf.Abs(s) / 5f), 0.8f, 0.6f,
                (s, t, z) => new Vector3(s, HorsaCy + 1.36f + t, z), 32, 12), "Horsa_Tailplane");
            var finMesh = SaveMesh(Surface("Horsa_Fin", 0f, 2.75f, false, true,
                s => Mathf.Lerp(-6.9f, -9.0f, Mathf.Pow(s / 2.75f, 0.8f)), s => Mathf.Lerp(-10.4f, -10.3f, s / 2.75f),
                s => Mathf.Lerp(0.30f, 0.12f, s / 2.75f), 1.0f, 0.5f,
                (s, t, z) => new Vector3(t, HorsaCy + 1.05f + s, z), 28, 14), "Horsa_Fin");

            if (chalk == 91) horsaPainted.Clear();   // first glider of a build: repaint everything
            var fusMat = HorsaMat($"Horsa_Fuselage_{chalk}", 1024, 2048, (u, v) => PaintFuselage(u, v, chalk.ToString()));
            var wingMat = HorsaMat("Horsa_Wing", 2048, 512, PaintWing);
            var tailMat = HorsaMat("Horsa_Tailplane", 1024, 256, PaintTailplane);
            var finMat = HorsaMat("Horsa_Fin", 512, 512, PaintFin);

            MeshPart("Fuselage", T, fuselageMesh, fusMat);
            MeshPart("Wing", T, wingMesh, wingMat);
            var wood = Mat("Horsa_Raw_Plywood", new Color(0.62f, 0.50f, 0.34f), 0.1f);
            var frameMat = Mat("Horsa_Nose_Frame", new Color(0.16f, 0.15f, 0.13f), 0.3f, 0.3f);
            // the outer wing panels hang from the wing joint (GliderApproach bends them down where one tore)
            foreach (float side in new[] { -1f, 1f })
            {
                var piv = new GameObject(side < 0 ? "Wing outer port" : "Wing outer starboard").transform;
                piv.SetParent(T, false);
                Vector3 p = new Vector3(side * HorsaWingJoint, HorsaCy + 1.62f + HorsaWingJoint * 0.03f, 1.4f);
                piv.localPosition = p;
                var panel = MeshPart("Outer wing panel", piv, side < 0 ? wingPortMesh : wingStbdMesh, wingMat);
                panel.transform.localPosition = -p;
                // splintered spars and ribs at the torn joint (shown only when it tore)
                var spl = new GameObject("Splinters").transform; spl.SetParent(piv, false);
                var rnd = new System.Random(chalk * 7 + (side < 0 ? 1 : 2));
                for (int i = 0; i < 7; i++)
                {
                    float z = Mathf.Lerp(-0.2f, 2.9f, (float)rnd.NextDouble()) - 1.4f, len = 0.3f + 0.7f * (float)rnd.NextDouble();
                    var sp = Prim(PrimitiveType.Cube, "Splinter", spl, new Vector3(-side * len * 0.4f, (float)rnd.NextDouble() * 0.2f - 0.05f, z),
                        new Vector3((float)rnd.NextDouble() * 20f - 10f, (float)rnd.NextDouble() * 30f - 15f, (float)rnd.NextDouble() * 30f - 15f),
                        new Vector3(len, 0.04f + 0.05f * (float)rnd.NextDouble(), 0.05f + 0.06f * (float)rnd.NextDouble()), wood);
                }
                Prim(PrimitiveType.Cube, "Torn rib", spl, new Vector3(0f, 0.05f, 0.3f), new Vector3(0f, 0f, side * 8f), new Vector3(0.04f, 0.32f, 2.6f), wood);
                spl.gameObject.SetActive(false);
            }
            // the nose hangs from the bottom of its joint: it can be stove in (squashed and bent down) or lose its glazing
            var noseSec = new GameObject("Nose section").transform;
            noseSec.SetParent(T, false);
            Vector3 np = new Vector3(0f, HorsaCy - 1.3f, HorsaNoseZ);
            noseSec.localPosition = np;
            MeshPart("Nose lower", noseSec, noseLowerMesh, fusMat).transform.localPosition = -np;
            MeshPart("Nose glazing", noseSec, noseGlazingMesh, fusMat).transform.localPosition = -np;
            // the bare frame of the glazed nose (seen when the glazing has gone): hoops and stringers over the top half
            var frame = new GameObject("Nose frame").transform; frame.SetParent(noseSec, false);
            Vector3 Ring(float z, float th) { var (w, hh, c) = HorsaProfile(z); var q = HorsaSection(th, w * 1.01f, hh * 1.01f); return new Vector3(q.x, HorsaCy + c + q.y, z) - np; }
            float[] hoops = { HorsaNoseZ + 0.15f, 8.55f, 9.0f, 9.4f, 9.72f };
            for (int r = 0; r < hoops.Length; r++)
                for (int j = 0; j < 10; j++)
                {
                    float a0 = Mathf.Lerp(-1.75f, 1.75f, j / 10f), a1 = Mathf.Lerp(-1.75f, 1.75f, (j + 1) / 10f);
                    Strut(frame, "Hoop", Ring(hoops[r], a0), Ring(hoops[r], a1), 0.05f, frameMat);
                }
            foreach (float th in new[] { -1.75f, -1.2f, -0.6f, 0f, 0.6f, 1.2f, 1.75f })
                for (int r = 0; r < hoops.Length - 1; r++)
                    Strut(frame, "Stringer", Ring(hoops[r], th), Ring(hoops[r + 1], th), 0.045f, frameMat);
            Strut(frame, "Stringer", Ring(hoops[hoops.Length - 1], 0f), Ring(9.98f, 0f), 0.045f, frameMat);
            // the cockpit floor and the two pilots' seats inside it
            Prim(PrimitiveType.Cube, "Cockpit floor", frame, new Vector3(0f, HorsaCy - 0.35f, 9.0f) - np, Vector3.zero, new Vector3(1.6f, 0.06f, 1.7f), wood);
            foreach (float sx in new[] { -0.4f, 0.4f })
                Prim(PrimitiveType.Cube, "Pilot seat", frame, new Vector3(sx, HorsaCy - 0.05f, 8.75f) - np, new Vector3(-10f, 0f, 0f), new Vector3(0.5f, 0.6f, 0.08f), frameMat);
            frame.gameObject.SetActive(false);
            // the rear fuselage with the tail surfaces hangs from a hinge at the bottom of the break: in a hard
            // landing it can snap and drop (GliderApproach does that for chalk 92, which broke its back)
            var tailSection = new GameObject("Tail section").transform;
            tailSection.SetParent(T, false);
            tailSection.localPosition = new Vector3(0f, HorsaCy - 1.3f, HorsaBreakZ);
            var rear = MeshPart("Rear fuselage", T, rearMesh, fusMat);
            var tp = MeshPart("Tailplane", T, tailMesh, tailMat);
            var fin = MeshPart("Fin and rudder", T, finMesh, finMat);
            foreach (var part in new[] { rear, tp, fin }) if (part) part.transform.SetParent(tailSection, true);

            // colliders: cabin, tail boom, wing (soldiers take cover behind and on the wreck)
            AddBox(g, new Vector3(0f, HorsaCy, 3.2f), new Vector3(2.4f, 2.8f, 13.4f));
            AddBox(g, new Vector3(0f, HorsaCy + 0.6f, -6.5f), new Vector3(1.5f, 1.6f, 7.5f));
            AddBox(g, new Vector3(0f, HorsaCy + 1.75f, 1.8f), new Vector3(26.0f, 0.5f, 3.6f));

            // ---- small parts
            var metal = Mat("Gear_Metal", new Color(0.25f, 0.25f, 0.24f), 0.4f, 0.6f);
            var tyre = Mat("Tyre", new Color(0.06f, 0.06f, 0.06f), 0.2f);
            var night = Mat("Horsa_Night", new Color(0.04f, 0.04f, 0.045f), 0.1f);
            Prim(PrimitiveType.Cylinder, "Pitot tube", T, new Vector3(0f, HorsaCy + 1.45f, 8.9f), new Vector3(90f, 0f, 0f), new Vector3(0.04f, 0.35f, 0.04f), metal);
            // main undercarriage: wheels outboard on V struts to the lower fuselage, an oleo leg up to the wing root
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 hub = new Vector3(side * 2.1f, 0.5f, 2.6f);
                Prim(PrimitiveType.Cylinder, "Main wheel", T, hub, new Vector3(0f, 0f, 90f), new Vector3(1.0f, 0.16f, 1.0f), tyre);
                Strut(T, "Front radius rod", hub, new Vector3(side * 0.85f, HorsaCy - 1.15f, 3.4f), 0.07f, metal);
                Strut(T, "Rear radius rod", hub, new Vector3(side * 0.85f, HorsaCy - 1.15f, 1.8f), 0.07f, metal);
                Strut(T, "Oleo leg", hub + Vector3.up * 0.1f, new Vector3(side * 1.25f, HorsaCy + 1.55f, 2.4f), 0.11f, metal);
            }
            Vector3 noseHub = new Vector3(0f, 0.36f, 8.0f);
            Prim(PrimitiveType.Cylinder, "Nose wheel", T, noseHub, new Vector3(0f, 0f, 90f), new Vector3(0.72f, 0.13f, 0.72f), tyre);
            Strut(T, "Nose leg", noseHub, new Vector3(0f, HorsaCy - 1.2f, 7.9f), 0.09f, metal);
            var bumper = Prim(PrimitiveType.Cube, "Tail bumper", T, new Vector3(0f, HorsaCy - 0.42f, -9.6f), Vector3.zero, new Vector3(0.12f, 0.25f, 0.5f), night);
            bumper.transform.SetParent(tailSection, true);

            var dust = new GameObject("Skid dust").AddComponent<ParticleSystem>();
            dust.transform.SetParent(g.transform, false); dust.transform.localPosition = new Vector3(0f, 0.3f, 4f);
            var main = dust.main; main.startLifetime = 2.5f; main.startSpeed = 2f; main.startSize = 3f; main.startColor = new Color(0.3f, 0.28f, 0.24f, 0.5f); main.playOnAwake = false;
            var em = dust.emission; em.rateOverTime = 40f;
            dust.GetComponent<ParticleSystemRenderer>().sharedMaterial = Mat("Dust", new Color(0.3f, 0.28f, 0.24f));
            return g;
        }

        // ------------------------------------------------------------------ geometry

        static (float w, float h, float c) HorsaProfile(float z)
        {
            int n = HorsaStations.GetLength(0);
            if (z >= HorsaStations[0, 0]) return (HorsaStations[0, 1], HorsaStations[0, 2], HorsaStations[0, 3]);
            if (z <= HorsaStations[n - 1, 0]) return (HorsaStations[n - 1, 1], HorsaStations[n - 1, 2], HorsaStations[n - 1, 3]);
            int i = 0; while (i < n - 2 && z < HorsaStations[i + 1, 0]) i++;
            float t = (HorsaStations[i, 0] - z) / (HorsaStations[i, 0] - HorsaStations[i + 1, 0]);
            float CR(int k)
            {
                float p0 = HorsaStations[Mathf.Max(i - 1, 0), k], p1 = HorsaStations[i, k], p2 = HorsaStations[i + 1, k], p3 = HorsaStations[Mathf.Min(i + 2, n - 1), k];
                float v = 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
                return Mathf.Clamp(v, Mathf.Min(p1, p2) - 0.03f, Mathf.Max(p1, p2) + 0.03f);
            }
            return (Mathf.Max(0.02f, CR(1)), Mathf.Max(0.02f, CR(2)), CR(3));
        }

        static float SPow(float a) => Mathf.Sign(a) * Mathf.Pow(Mathf.Abs(a), 2f / SuperN);

        /// <summary>θ = 0 on top, -π/2 port, +π/2 starboard; the UV seam runs along the belly.</summary>
        static Vector2 HorsaSection(float theta, float w, float h) => new Vector2(w * SPow(Mathf.Sin(theta)), h * SPow(Mathf.Cos(theta)));

        /// <summary>Where the fuselage is built in two pieces (the rear section can break off in a crash landing).</summary>
        const float HorsaBreakZ = -1.6f;
        /// <summary>Where the nose joins the cabin, and the wing joint (centre section / outer panels).</summary>
        const float HorsaNoseZ = 8.0f, HorsaWingJoint = 7.0f;

        /// <summary>A copy of a mesh with only the triangles whose centre passes the test (UVs and all kept).</summary>
        static Mesh KeepTriangles(Mesh src, Func<Vector3, bool> keep, string name)
        {
            var v = src.vertices; var t = src.triangles; var kept = new List<int>();
            for (int i = 0; i < t.Length; i += 3)
                if (keep((v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f)) { kept.Add(t[i]); kept.Add(t[i + 1]); kept.Add(t[i + 2]); }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetUVs(0, new List<Vector2>(src.uv)); m.SetTriangles(kept, 0);
            if (src.normals != null && src.normals.Length == v.Length) m.SetNormals(new List<Vector3>(src.normals)); else m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh HorsaFuselageMesh() => HorsaFuselageMesh(float.MinValue, float.MaxValue);

        /// <summary>The fuselage between two stations (front piece: zFrom = break; rear piece: zTo = break).</summary>
        static Mesh HorsaFuselageMesh(float zFrom, float zTo)
        {
            float zNose = HorsaStations[0, 0], zTail = HorsaStations[HorsaStations.GetLength(0) - 1, 0];
            var all = new List<float>();
            int ns = HorsaStations.GetLength(0);
            for (int i = 0; i < ns - 1; i++)
            {
                int sub = (HorsaStations[i, 0] - HorsaStations[i + 1, 0]) > 2f ? 8 : 4;
                for (int k = 0; k < sub; k++) all.Add(Mathf.Lerp(HorsaStations[i, 0], HorsaStations[i + 1, 0], k / (float)sub));
            }
            all.Add(zTail);
            // keep the stations in range, with an exact ring at the cut
            var zs = new List<float>();
            if (zTo < zNose) zs.Add(zTo);
            foreach (float z in all) if (z < zTo - 0.01f && z > zFrom + 0.01f) zs.Add(z);
            if (zFrom > zTail) zs.Add(zFrom);
            zNose = zs[0]; float zEnd = zs[zs.Count - 1];
            const int around = 48;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            foreach (float z in zs)
            {
                var (w, h, c) = HorsaProfile(z);
                for (int j = 0; j <= around; j++)
                {
                    float theta = -Mathf.PI + 2f * Mathf.PI * j / around;
                    var p = HorsaSection(theta, w, h);
                    verts.Add(new Vector3(p.x, HorsaCy + c + p.y, z));
                    uvs.Add(new Vector2(j / (float)around, (z - HorsaStations[HorsaStations.GetLength(0) - 1, 0]) / (HorsaStations[0, 0] - HorsaStations[HorsaStations.GetLength(0) - 1, 0])));
                }
            }
            int row = around + 1;
            for (int r = 0; r < zs.Count - 1; r++)
                for (int j = 0; j < around; j++)
                {
                    int a = r * row + j, b = a + 1, c2 = a + row, d = c2 + 1;
                    tris.AddRange(new[] { a, c2, b, b, c2, d });
                }
            // caps at the ends (the nose and tail tips, or the raw edge of the break)
            foreach (var (ring, z) in new[] { (0, zNose), (zs.Count - 1, zEnd) })
            {
                var (w, h, c) = HorsaProfile(z);
                int centre = verts.Count;
                verts.Add(new Vector3(0f, HorsaCy + c, z + (ring == 0 ? 0.02f : -0.02f)));
                uvs.Add(new Vector2(0.5f, ring == 0 ? 1f : 0f));
                for (int j = 0; j < around; j++)
                {
                    int a = ring * row + j, b = a + 1;
                    if (ring == 0) tris.AddRange(new[] { centre, a, b }); else tris.AddRange(new[] { centre, b, a });
                }
            }
            var m = new Mesh { name = $"Horsa_Fuselage_{zFrom:0}_{zTo:0}" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            FixOutward(m, new Vector3(0f, HorsaCy, 3f));
            return m;
        }

        /// <summary>
        /// A lifting surface (wing, tailplane, fin). s runs along the span from s0 to s1; le/te give the leading and
        /// trailing edge z; thick the section thickness. Rounded at the chosen ends. Top surface UVs fill the upper
        /// half of the texture, the bottom surface the lower half, both mapped straight from (s, z).
        /// </summary>
        static Mesh Surface(string name, float s0, float s1, bool roundStart, bool roundEnd,
            Func<float, float> le, Func<float, float> te, Func<float, float> thick, float tipR, float topShare,
            Func<float, float, float, Vector3> map, int ns, int nc)
        {
            float zMin = float.MaxValue, zMax = float.MinValue;
            for (int i = 0; i <= 50; i++) { float s = Mathf.Lerp(s0, s1, i / 50f); zMin = Mathf.Min(zMin, te(s)); zMax = Mathf.Max(zMax, le(s)); }
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int side = 0; side < 2; side++)   // 0 = top, 1 = bottom
            {
                int baseIdx = verts.Count;
                for (int i = 0; i <= ns; i++)
                {
                    float k = i / (float)ns;
                    float s = roundStart && roundEnd ? Mathf.Lerp(s0, s1, 0.5f - 0.5f * Mathf.Cos(Mathf.PI * k))
                            : roundEnd ? Mathf.Lerp(s0, s1, Mathf.Sin(0.5f * Mathf.PI * k)) : Mathf.Lerp(s0, s1, k);
                    float f = 1f;
                    if (roundEnd && s1 - s < tipR) { float q = (tipR - (s1 - s)) / tipR; f = Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
                    if (roundStart && s - s0 < tipR) { float q = (tipR - (s - s0)) / tipR; f = Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
                    float l = le(s), tr = te(s), mid = (l + tr) * 0.5f, half = (l - tr) * 0.5f * Mathf.Max(f, 0.001f);
                    float L = mid + half, Tz = mid - half, th = thick(s) * Mathf.Lerp(0.4f, 1f, f);
                    for (int j = 0; j <= nc; j++)
                    {
                        float c = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * j / nc);
                        float gC = (0.2969f * Mathf.Sqrt(c) - 0.126f * c - 0.3516f * c * c + 0.2843f * c * c * c - 0.1036f * c * c * c * c) / 0.1001f;
                        float t = side == 0 ? th * topShare * gC : -th * (1f - topShare) * gC;
                        float z = Mathf.Lerp(L, Tz, c);
                        verts.Add(map(s, t, z));
                        float u = (s - s0) / (s1 - s0), vv = (z - zMin) / (zMax - zMin);
                        uvs.Add(new Vector2(u, side == 0 ? 0.5f + 0.5f * vv : 0.5f * vv));
                    }
                }
                int row = nc + 1;
                for (int i = 0; i < ns; i++)
                    for (int j = 0; j < nc; j++)
                    {
                        int a = baseIdx + i * row + j, b = a + 1, c2 = a + row, d = c2 + 1;
                        if (side == 0) tris.AddRange(new[] { a, c2, d, a, d, b }); else tris.AddRange(new[] { a, d, c2, a, b, d });
                    }
            }
            var m = new Mesh { name = name };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            // make sure the top surface faces away from the bottom one (the fin's axis swap mirrors the winding)
            Vector3 nTop = m.normals[(ns / 2) * (nc + 1) + nc / 3];
            Vector3 dir = verts[(ns / 2) * (nc + 1) + nc / 3] - verts[(ns + 1) * (nc + 1) + (ns / 2) * (nc + 1) + nc / 3];
            if (Vector3.Dot(nTop, dir) < 0f) Flip(m);
            return m;
        }

        static void FixOutward(Mesh m, Vector3 inside)
        {
            m.RecalculateNormals(); m.RecalculateBounds();
            var v = m.vertices; var n = m.normals; int votes = 0;
            for (int i = 0; i < v.Length; i += 7) votes += Vector3.Dot(n[i], v[i] - new Vector3(inside.x, inside.y, v[i].z)) >= 0f ? 1 : -1;
            if (votes < 0) Flip(m);
        }

        static void Flip(Mesh m)
        {
            var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3) { int a = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = a; }
            m.triangles = t; m.RecalculateNormals();
        }

        static Mesh SaveMesh(Mesh m, string name)
        {
            string path = $"{GenMeshDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { existing.Clear(); EditorUtility.CopySerialized(m, existing); existing.name = name; EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static GameObject MeshPart(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void AddBox(GameObject g, Vector3 centre, Vector3 size) { var b = g.AddComponent<BoxCollider>(); b.center = centre; b.size = size; }

        static void Strut(Transform parent, string name, Vector3 a, Vector3 b, float dia, Material m)
        {
            var go = Prim(PrimitiveType.Cylinder, name, parent, (a + b) * 0.5f, Vector3.zero, new Vector3(dia, (b - a).magnitude * 0.5f, dia), m);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        }

        // ------------------------------------------------------------------ textures

        static Texture2D PaintTexture(string name, int w, int h, Func<float, float, Color32> paint)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = paint((x + 0.5f) / w, (y + 0.5f) / h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            string path = $"{GenTexDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp) { imp.maxTextureSize = 2048; imp.anisoLevel = 8; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static readonly HashSet<string> horsaPainted = new HashSet<string>();

        static Material HorsaMat(string name, int w, int h, Func<float, float, Color32> paint)
        {
            if (horsaPainted.Contains(name)) return Mat(name, Color.white, 0.22f);
            horsaPainted.Add(name);
            return TexturedMat(name, PaintTexture(name, w, h, paint));
        }

        static Material TexturedMat(string name, Texture2D tex)
        {
            var m = Mat(name, Color.white, 0.22f);
            m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            m.SetFloat("_Cull", 0f);   // both faces: thin surfaces seen from below
            EditorUtility.SetDirty(m);
            return m;
        }

        static Color32 Camo(float a, float b)
        {
            float n = Mathf.PerlinNoise(a * 0.11f + 11.3f, b * 0.11f + 4.7f) + 0.35f * Mathf.PerlinNoise(a * 0.33f + 2.1f, b * 0.33f + 9.4f);
            return n > 0.72f ? CDarkEarth : CDarkGreen;
        }

        static Color32 Shade(Color32 c, float k) => new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), 255);

        static Color32 PaintFuselage(float u, float v, string number)
        {
            float zNose = HorsaStations[0, 0], zTail = HorsaStations[HorsaStations.GetLength(0) - 1, 0];
            float theta = -Mathf.PI + 2f * Mathf.PI * u, z = Mathf.Lerp(zTail, zNose, v);
            var (w, h, _) = HorsaProfile(z);
            var p = HorsaSection(theta, w, h);
            float yr = p.y, side = Mathf.Abs(theta);
            float arc = theta * (w + h) * 0.5f;

            // camouflage on the spine, night black down the sides and underneath (wavy demarcation)
            Color32 col = yr > h * (0.45f + 0.07f * Mathf.Sin(z * 1.1f)) ? Camo(z, arc) : CNight;

            // invasion stripes round the rear fuselage: white, black, white, black, white
            if (z > -5.9f && z < -3.9f) col = ((int)((z + 5.9f) / 0.4f)) % 2 == 0 ? CWhite : CStripeBlack;

            if (side > 0.85f && side < 2.3f)
            {
                // the big loading door on the port side behind the cockpit
                if (p.x < 0f && z > 5.6f && z < 7.0f && yr > -1.0f && yr < 0.7f &&
                    (Mathf.Abs(z - 5.6f) < 0.035f || Mathf.Abs(z - 7.0f) < 0.035f || Mathf.Abs(yr + 1.0f) < 0.035f || Mathf.Abs(yr - 0.7f) < 0.035f))
                    col = new Color32(40, 40, 40, 255);
                // round cabin windows
                foreach (float pz in new[] { 4.9f, 3.7f, 2.5f, 1.3f, 0.1f, -1.1f })
                {
                    if (p.x < 0f && pz > 5.4f) continue;
                    float d = Mathf.Sqrt((z - pz) * (z - pz) + (yr - 0.45f) * (yr - 0.45f));
                    if (d < 0.165f) col = CPorthole; else if (d < 0.2f) col = new Color32(25, 25, 25, 255);
                }
                // Type C1 roundel
                {
                    float d = Mathf.Sqrt((z + 2.55f) * (z + 2.55f) + (yr - 0.05f) * (yr - 0.05f));
                    if (d < 0.725f) col = d < 0.24f ? CRed : d < 0.32f ? CWhite : d < 0.645f ? CBlue : CYellow;
                }
                // chalk number on both sides of the nose, read from the front on the port side and the rear on starboard
                if (z > 7.15f && z < 8.15f && yr < -0.15f && yr > -0.8f)
                {
                    float r = p.x < 0f ? 8.1f - z : z - 7.2f;
                    if (Glyph(number, r, -0.2f - yr, 0.36f, 0.55f, 0.12f)) col = CWhite;
                }
            }

            // the glazed nose: framed panes over the upper front and sides of the cockpit
            if (z > 8.2f && yr > -0.2f * h - (z - 8.2f) * 0.35f)
            {
                bool frame = Mathf.Repeat(z - 8.2f, 0.45f) < 0.05f || Mathf.Repeat(theta + 3f, 0.36f) < 0.04f;
                col = frame ? CDarkGreen : Shade(CGlass, 0.9f + 0.25f * Mathf.Clamp01(yr / h));
            }
            return col;
        }

        static readonly Dictionary<char, string[]> Font5x7 = new Dictionary<char, string[]>
        {
            { '0', new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" } },
            { '1', new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" } },
            { '2', new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" } },
            { '3', new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" } },
            { '4', new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" } },
            { '5', new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" } },
            { '6', new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" } },
            { '7', new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" } },
            { '8', new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" } },
            { '9', new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" } },
        };

        /// <summary>Is the point (r along the text, q down from its top), in metres, inside a stroke of the text?</summary>
        static bool Glyph(string text, float r, float q, float cw, float ch, float gap)
        {
            if (r < 0f || q < 0f || q >= ch) return false;
            int i = (int)(r / (cw + gap));
            if (i >= text.Length) return false;
            float x = r - i * (cw + gap);
            if (x >= cw || !Font5x7.TryGetValue(text[i], out var rows)) return false;
            int col = (int)(x / cw * 5f), rw = (int)(q / ch * 7f);
            return rows[rw][col] == '1';
        }

        static float WingLE(float s) => Mathf.Lerp(4.0f, 2.3f, Mathf.Abs(s) / 13.4f);
        static float WingTE(float s) => Mathf.Lerp(-0.4f, -0.2f, Mathf.Abs(s) / 13.4f);

        static Color32 PaintWing(float u, float v)
        {
            bool top = v >= 0.5f;
            float vv = top ? (v - 0.5f) * 2f : v * 2f;
            float s = Mathf.Lerp(-13.4f, 13.4f, u), z = Mathf.Lerp(-0.4f, 4.0f, vv), a = Mathf.Abs(s);
            Color32 col = top ? Camo(s, z) : CNight;
            float le = WingLE(s), te = WingTE(s), chord = le - te;
            // control surfaces: ailerons outboard, flaps inboard (hinge lines and their ends)
            float hinge = le - 0.76f * chord;
            if (Mathf.Abs(z - hinge) < 0.025f && z > te && ((a > 8.6f && a < 12.6f) || (a > 1.3f && a < 6.0f))) col = Shade(col, 0.45f);
            if (z < hinge && z > te && (Mathf.Abs(a - 8.6f) < 0.025f || Mathf.Abs(a - 6.0f) < 0.025f || Mathf.Abs(a - 1.3f) < 0.025f)) col = Shade(col, 0.45f);
            // invasion stripes round the wing, inboard of the roundels
            if (a > 6.2f && a < 8.4f) col = ((int)((a - 6.2f) / 0.44f)) % 2 == 0 ? CWhite : CStripeBlack;
            // Type B roundels on the upper wing
            if (top)
            {
                float d = Mathf.Sqrt((a - 9.9f) * (a - 9.9f) + (z - (WingLE(9.9f) + WingTE(9.9f)) * 0.5f) * (z - (WingLE(9.9f) + WingTE(9.9f)) * 0.5f));
                if (d < 1.0f) col = d < 0.4f ? CRed : CBlue;
            }
            return col;
        }

        static Color32 PaintTailplane(float u, float v)
        {
            bool top = v >= 0.5f;
            float vv = top ? (v - 0.5f) * 2f : v * 2f;
            float s = Mathf.Lerp(-5f, 5f, u), z = Mathf.Lerp(-9.8f, -7.5f, vv);
            Color32 col = top ? Camo(s + 40f, z) : CNight;
            float le = Mathf.Lerp(-7.5f, -7.95f, Mathf.Abs(s) / 5f), te = Mathf.Lerp(-9.8f, -9.55f, Mathf.Abs(s) / 5f);
            if (Mathf.Abs(z - (le - 0.68f * (le - te))) < 0.025f && Mathf.Abs(s) > 0.3f) col = Shade(col, 0.45f);   // elevator hinge
            return col;
        }

        static Color32 PaintFin(float u, float v)
        {
            float vv = v >= 0.5f ? (v - 0.5f) * 2f : v * 2f;
            float s = u * 2.75f - 0.17f, z = Mathf.Lerp(-10.4f, -6.9f, vv);   // s measured from the top of the boom
            Color32 col = Camo(z + 70f, s);
            if (Mathf.Abs(z + 9.75f) < 0.025f && s > 0.1f) col = Shade(col, 0.45f);   // rudder hinge
            // fin flash: red leading, then white, blue trailing
            if (s > 0.25f && s < 1.45f && z < -8.8f && z > -9.62f) col = z > -9.07f ? CRed : z > -9.35f ? CWhite : CBlue;
            return col;
        }
    }
}
