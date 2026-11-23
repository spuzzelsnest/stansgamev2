using System.Collections.Generic;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Proving Ground: a village pond behind the hamlet (Joan, 23 Nov) to try the swimming in - round, not square, with
    /// the ground levelled round it, a shelving shore on the village side to wade in from, steeper banks elsewhere, a
    /// little landing stage to jump off into the deep middle (3 m), reeds at the edge and trees round it. A gap in the
    /// southern hedgerow leads to it. The terrain is dug out below 0 for it (ProvingTerrain sits PgBase metres lower).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static readonly Vector2 PondCentre = new Vector2(-8f, -100f);
        const float PondRadius = 14f, PondDepth = 3.2f, PondRim = 0.35f, PgBase = 6f;
        static float pondRimY = float.NaN;

        /// <summary>The pond's edge (s = 1) in the direction a (radians): round, but not a perfect circle.</summary>
        static float PondR(float a) => PondRadius * (1f + 0.10f * Mathf.Sin(2f * a + 0.7f) + 0.06f * Mathf.Sin(3f * a + 2.1f) + 0.03f * Mathf.Sin(5f * a));

        /// <summary>The level of the ground round the pond (the natural ground at its middle).</summary>
        static float PondRimY => float.IsNaN(pondRimY) ? (pondRimY = PgNaturalHeight(PondCentre.x, PondCentre.y)) : pondRimY;
        static float PondWaterY => PondRimY - PondRim;

        /// <summary>The ground with the pond dug into it: water's edge at s = 1, the bank up to the levelled rim, then
        /// back to the farmland.</summary>
        static float PgPondShape(float x, float z, float natural)
        {
            float dx = x - PondCentre.x, dz = z - PondCentre.y;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d > PondRadius * 2.4f) return natural;
            float a = Mathf.Atan2(dz, dx), s = d / PondR(a);
            if (s >= 2f) return natural;
            float rim = PondRimY, wl = rim - PondRim;
            if (s >= 1.15f) return Mathf.Lerp(rim, natural, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.4f, 2f, s)));
            if (s >= 1f) return Mathf.Lerp(wl, rim, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 1.15f, s)));
            // under water: a long shelving shore to the north (the village side), steeper banks round the rest
            float beach = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a - Mathf.PI / 2f)), 2f);
            float k = Mathf.Lerp(0.8f, 2.2f, beach);
            return wl - PondDepth * Mathf.Pow(1f - s, k);
        }

        static void PgPond(Transform env, List<Bounds> keepOut)
        {
            bool wasFlat = flatGround; flatGround = false;
            try
            {
                var g = Group("Pond (swimming practice)", env);
                float wl = PondWaterY;
                var c3 = new Vector3(PondCentre.x, wl, PondCentre.y);
                keepOut.Add(new Bounds(c3, new Vector3(PondRadius * 3.2f, 10f, PondRadius * 3.2f)));

                // the water: a disc following the edge (a little beyond it, under the bank)
                const int seg = 72;
                var v = new List<Vector3> { Vector3.zero }; var tri = new List<int>();
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg, r = PondR(a) * 1.05f;
                    v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                    tri.Add(0); tri.Add(1 + (i + 1) % seg); tri.Add(1 + i);   // clockwise seen from above: faces up
                }
                var mesh = new Mesh { name = "Pond_Water" };
                mesh.SetVertices(v); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                string dir = Root + "/Models/Generated";
                if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder(Root + "/Models", "Generated");
                UnityEditor.AssetDatabase.DeleteAsset($"{dir}/Pond_Water.asset");
                UnityEditor.AssetDatabase.CreateAsset(mesh, $"{dir}/Pond_Water.asset");
                var water = new GameObject("Water");
                water.transform.SetParent(g); water.transform.position = c3;
                water.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = water.AddComponent<MeshRenderer>();
                mr.sharedMaterial = WaterShaderMaterial(false, true) ?? WaterMaterial();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                water.AddComponent<WaterRipple>().flow = new Vector2(0.001f, 0.002f);
                water.layer = 4;

                Vector3 Edge(float deg, float s)
                {
                    float a = deg * Mathf.Deg2Rad, r = PondR(a) * s;
                    var p = new Vector3(PondCentre.x + Mathf.Cos(a) * r, 0f, PondCentre.y + Mathf.Sin(a) * r);
                    p.y = GroundAt(p); return p;
                }

                // a landing stage on the west bank, out over the deep water
                var wood = Mat("Pond_Planks", new Color(0.33f, 0.25f, 0.17f), 0.1f);
                var stage = new GameObject("Landing stage").transform; stage.SetParent(g);
                Vector3 shore = Edge(180f, 1.12f); shore.y = PondRimY;
                var inward = (c3 - shore); inward.y = 0f; inward.Normalize();
                stage.SetPositionAndRotation(shore, Quaternion.LookRotation(inward));
                const float len = 6f, deckY = 0.45f;
                Prim(PrimitiveType.Cube, "Deck", stage, new Vector3(0f, deckY - PondRim, len / 2f), Vector3.zero, new Vector3(1.4f, 0.08f, len), wood, true);
                for (int i = 0; i <= 3; i++)
                    foreach (float sx in new[] { -0.62f, 0.62f })
                        Prim(PrimitiveType.Cylinder, "Post", stage, new Vector3(sx, deckY - PondRim - 1.2f, i * len / 3f), Vector3.zero, new Vector3(0.12f, 1.25f, 0.12f), wood);
                Prim(PrimitiveType.Cube, "Ladder rail", stage, new Vector3(0.55f, -0.4f, len + 0.05f), new Vector3(-10f, 0f, 0f), new Vector3(0.05f, 1.6f, 0.05f), wood);
                Prim(PrimitiveType.Cube, "Ladder rail", stage, new Vector3(-0.05f, -0.4f, len + 0.05f), new Vector3(-10f, 0f, 0f), new Vector3(0.05f, 1.6f, 0.05f), wood);

                // reeds in clumps along the water's edge (not on the shore you wade in from, not at the stage)
                var reed = Mat("Pond_Reed", new Color(0.30f, 0.36f, 0.16f), 0.2f);
                var reedTop = Mat("Pond_Reed_Head", new Color(0.30f, 0.20f, 0.11f), 0.15f);
                Random.InitState(1906);
                foreach (float deg in new[] { -160f, -125f, -95f, -60f, -30f, 10f, 30f, 145f, 210f })
                {
                    var clump = new GameObject("Reeds").transform; clump.SetParent(g);
                    var at = Edge(deg, 0.97f); at.y = wl;
                    clump.position = at;
                    for (int i = 0; i < 9; i++)
                    {
                        float h = Random.Range(1.1f, 1.9f);
                        var off = new Vector3(Random.Range(-0.9f, 0.9f), 0f, Random.Range(-0.9f, 0.9f));
                        var tilt = new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f));
                        Prim(PrimitiveType.Cylinder, "Reed", clump, off + new Vector3(0f, h / 2f - 0.4f, 0f), tilt, new Vector3(0.025f, h / 2f + 0.4f, 0.025f), reed);
                        if (i % 3 == 0) Prim(PrimitiveType.Capsule, "Bulrush head", clump, off + new Vector3(0f, h - 0.3f, 0f), tilt, new Vector3(0.06f, 0.12f, 0.06f), reedTop);
                    }
                }

                // trees round it, leaning their crowns over the water; bushes between
                var bark = Mat("Bark", new Color(0.20f, 0.17f, 0.13f));
                var poplarBark = Mat("Bark_Poplar", new Color(0.30f, 0.29f, 0.26f));
                var broad = Mat("Broadleaf_Leaves", new Color(0.08f, 0.14f, 0.07f));
                var poplar = Mat("Poplar_Leaves", new Color(0.10f, 0.16f, 0.08f));
                foreach (var (deg, s) in new[] { (-150f, 1.55f), (-105f, 1.7f), (-55f, 1.5f), (-15f, 1.65f), (25f, 1.55f), (140f, 1.6f), (215f, 1.75f) })
                {
                    var p = Edge(deg, s);
                    if (deg == -105f || deg == 215f) TallPoplar(g, p, poplarBark, poplar); else Broadleaf(g, p, bark, broad);
                }
                foreach (var (deg, s) in new[] { (-170f, 1.35f), (-80f, 1.3f), (-35f, 1.4f), (5f, 1.3f), (160f, 1.35f), (235f, 1.3f) })
                {
                    var b = new GameObject("Bush"); b.transform.SetParent(g); b.transform.position = Edge(deg, s);
                    AddSmallTree(b, 4, Random.Range(1.3f, 2.4f));
                }
                // a small painted board on a post at the shore you wade in from (the floating text was far too big)
                var signAt = Edge(90f, 1.35f);
                var sign = new GameObject("Sign: swimming practice").transform; sign.SetParent(g); sign.position = signAt;
                var board = Mat("Pond_Sign_Board", new Color(0.82f, 0.78f, 0.66f), 0.1f);
                Prim(PrimitiveType.Cube, "Post", sign, new Vector3(0f, 0.6f, 0f), Vector3.zero, new Vector3(0.08f, 1.2f, 0.08f), wood, true);
                Prim(PrimitiveType.Cube, "Board", sign, new Vector3(0f, 1.15f, 0.05f), Vector3.zero, new Vector3(1.1f, 0.34f, 0.03f), board);   // on the village (north) side
                FitLabel(sign, "SWIMMING PRACTICE", signAt + new Vector3(0f, 1.15f, 0.072f), 1.0f, 0.24f, new Color(0.12f, 0.1f, 0.08f), Quaternion.Euler(0f, 180f, 0f));
            }
            finally { flatGround = wasFlat; }
        }
    }
}
