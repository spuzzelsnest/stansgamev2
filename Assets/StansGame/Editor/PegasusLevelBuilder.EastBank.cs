using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The east bank south of the Ranville road as the RAF verticals of June 1944 show it (Joan's indications:
    /// blue = trench, orange = trees): a zig-zag fire trench running south between the canal and the east-bank road,
    /// and a belt of trees and scrub between that road and the field where glider 91 stopped. Points measured on
    /// "aerial photo bridge June 1944(_2)-indications" in game metres (the canal at the bridge as the scale).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        // the trench's centre line (zig-zag from the larger photo, the south end from the vertical; kept just off the road)
        static readonly Vector2[] EastBankTrench =
        {
            new Vector2(21.8f, -7f), new Vector2(21.9f, -9.2f), new Vector2(20.3f, -12.1f), new Vector2(21.6f, -15.3f),
            new Vector2(20.7f, -18.5f), new Vector2(20.8f, -20.3f), new Vector2(22.5f, -24.5f), new Vector2(20.9f, -29.4f),
            new Vector2(20.3f, -30.4f), new Vector2(17.3f, -32.1f), new Vector2(16.8f, -34.1f), new Vector2(14.4f, -35.8f),
            new Vector2(14.7f, -37.8f), new Vector2(15.5f, -41f),
        };

        /// <summary>x of the east-bank road's centre line at z (south of the Ranville road).</summary>
        static float BankRoadX(float z)
        {
            var r = EastBankRoadSouth;
            for (int i = 0; i < r.Length - 1; i++)
                if (z <= r[i].y && z >= r[i + 1].y) return Mathf.Lerp(r[i].x, r[i + 1].x, Mathf.InverseLerp(r[i].y, r[i + 1].y, z));
            return z > r[0].y ? r[0].x : r[r.Length - 1].x;
        }

        static float DistToLine(Vector2 p, Vector2[] line)
        {
            float d = float.MaxValue;
            for (int i = 0; i < line.Length - 1; i++)
            {
                Vector2 a = line[i], ab = line[i + 1] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                d = Mathf.Min(d, Vector2.Distance(p, a + ab * t));
            }
            return d;
        }

        /// <summary>
        /// A zig-zag fire trench dug along a line: the ground opened cell by cell (one terrain-hole cell wide, ~1.4 m,
        /// 4-connected so it can be walked), each cell lined with planks where it meets solid ground, duckboards on the
        /// floor, a fire step and a heaped parapet on the field side, and the garrison's riflemen in it.
        /// </summary>
        static void ZigzagTrench(Transform parent, Vector2[] line, List<Health> men, int riflemen)
        {
            var g = Group("Fire trench (east bank, 1944 photos)", parent);
            if (!terrainRef) return;
            var td = terrainRef.terrainData; var tp = terrainRef.transform.position;
            int res = td.holesResolution;
            float c = td.size.x / res;
            // walk the line in small steps, collecting the cells it passes through (filling diagonal steps)
            var cells = new List<Vector2Int>(); var set = new HashSet<Vector2Int>();
            void Add(Vector2Int k) { if (set.Add(k)) cells.Add(k); }
            Vector2Int? last = null;
            for (int s = 0; s < line.Length - 1; s++)
            {
                float len = Vector2.Distance(line[s], line[s + 1]);
                for (float t = 0f; t <= len; t += 0.25f)
                {
                    Vector2 p = Vector2.Lerp(line[s], line[s + 1], t / len);
                    // keep clear of the road (it runs just east of the trench)
                    p.x = Mathf.Min(p.x, BankRoadX(p.y) - 2.5f - 1.1f);
                    p.x = Mathf.Max(p.x, Water.QuayHalf + 2.2f);
                    var k = new Vector2Int(Mathf.FloorToInt((p.x - tp.x) / c), Mathf.FloorToInt((p.y - tp.z) / c));
                    if (last.HasValue && last.Value != k && last.Value.x != k.x && last.Value.y != k.y)
                        Add(new Vector2Int(k.x, last.Value.y));   // a diagonal step: open the corner cell too
                    Add(k); last = k;
                }
            }
            foreach (var k in cells) td.SetHoles(k.x, k.y, new bool[1, 1]);   // false = hole

            var planks = TexMat("Trench_Planks", "Planks_Revetment.png", new Color(0.85f, 0.8f, 0.75f), 0.1f, false, "Planks_Revetment_N.png");
            var mud = TexMat("Trench_Floor", Root + "/Textures/Ground/Ground_Mud_Albedo.jpg", new Color(0.7f, 0.65f, 0.6f), 0.25f, false, Root + "/Textures/Ground/Ground_Mud_Normal.png");
            var bark = PropMaterial("Brush_Bark");
            const float D = 1.3f, T = 0.08f;
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            for (int n = 0; n < cells.Count; n++)
            {
                var k = cells[n];
                Vector3 centre = new Vector3(tp.x + (k.x + 0.5f) * c, 0f, tp.z + (k.y + 0.5f) * c);
                // each cell is its own object: set on the ground under it afterwards (heights relative to that ground)
                var cell = new GameObject("Trench bay").transform; cell.SetParent(g); cell.position = centre;
                Prim(PrimitiveType.Cube, "Floor", cell, new Vector3(0f, -D - 0.1f, 0f), Vector3.zero, new Vector3(c, 0.2f, c), mud, true);
                for (int i = 0; i < 3; i++)
                    Prim(PrimitiveType.Cube, "Duckboard slat", cell, new Vector3(0f, -D + 0.04f, (i - 1) * 0.4f), new Vector3(0f, Random.Range(-4f, 4f), 0f), new Vector3(0.6f, 0.04f, 0.12f), planks);
                foreach (var dv in dirs)
                {
                    if (set.Contains(k + dv)) continue;   // open to the next bay
                    var off = new Vector3(dv.x, 0f, dv.y) * (c * 0.5f - T * 0.5f);
                    var size = dv.x != 0 ? new Vector3(T, D + 0.1f, c) : new Vector3(c, D + 0.1f, T);
                    Prim(PrimitiveType.Cube, "Revetment", cell, off + Vector3.up * (-D * 0.5f + 0.05f), Vector3.zero, size, planks, true);
                    // stakes holding the planks
                    var along = dv.x != 0 ? Vector3.forward : Vector3.right;
                    foreach (float a in new[] { -0.4f, 0.4f })
                        Prim(PrimitiveType.Cylinder, "Stake", cell, off * 0.92f + along * a + Vector3.up * (-D * 0.5f + 0.2f), new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f)), new Vector3(0.08f, D * 0.5f + 0.2f, 0.08f), bark);
                    // a fire step against the field side, where the men stand to shoot over the parapet
                    if (dv.x == 1) Prim(PrimitiveType.Cube, "Fire step", cell, new Vector3(c * 0.5f - 0.3f, -D + 0.25f, 0f), Vector3.zero, new Vector3(0.45f, 0.5f, c - 0.1f), planks, true);
                }
                // the spoil heaped on the canal side (the road runs along the field side), every other bay as one bank
                if (n % 2 == 0 && centre.x - 3.3f > Water.QuayHalf + 1.2f) EarthMound(centre - new Vector3(c * 0.5f + 1.3f, 0f, 0f), 90f, 3.0f, 1.6f, 0.4f);
                if (n % 9 == 4) Spade(cell, new Vector3(-c * 0.5f + 0.2f, -D, 0.3f), -90f, 14f);
            }
            // the riflemen, spread along it, standing on the fire step facing the field and the gliders
            for (int i = 0; i < riflemen && cells.Count > 0; i++)
            {
                var k = cells[Mathf.Clamp(Mathf.RoundToInt((i + 0.5f) / riflemen * cells.Count), 0, cells.Count - 1)];
                Vector3 p = new Vector3(tp.x + (k.x + 0.5f) * c + 0.3f, -0.75f, tp.z + (k.y + 0.5f) * c);
                var h = Soldier($"Rifleman trench {i + 1}", g, p, 95f + i * 12f);
                if (h) men.Add(h);
            }
            Debug.Log($"Stan's Game: east-bank fire trench, {cells.Count} bays ({c:F2} m) from z {line[0].y:F0} to {line[line.Length - 1].y:F0}");
        }

        /// <summary>
        /// The belt of trees and scrub between the east-bank road and the landing field (orange on the photos; pb01
        /// shows tall trees behind the gliders): small round trees and bushes with a few tall ones, kept off the road
        /// and out from under glider 91's wings, where its nose ran into the scrub.
        /// </summary>
        static void EastBankCopse(Transform trees, Material poplarBark, Material leaves, Material leaves2)
        {
            var g = Group("Trees by the landing field (1944 photos)", trees);
            var g91 = GliderRests[0];
            var rot91 = Quaternion.Euler(0f, g91.yaw + g91.slew, 0f);
            bool UnderGlider(Vector3 p)
            {
                var l = Quaternion.Inverse(rot91) * (p - g91.pos);   // glider frame: x along the wings, z along the body
                return (Mathf.Abs(l.x) < 15f && Mathf.Abs(l.z - 0.5f) < 3.5f) || (Mathf.Abs(l.x) < 2.5f && l.z > -12f && l.z < 11.5f);
            }
            int n = 0;
            for (float z = -21f; z > -60f; z -= 3.2f)
            {
                float west = BankRoadX(z) + 2.5f + 1.5f;
                float east = Mathf.Lerp(34.5f, 31.5f, Mathf.InverseLerp(-21f, -60f, z));
                for (float x = west; x < east; x += 3.4f)
                {
                    var p = new Vector3(x + Random.Range(-1.1f, 1.1f), 0f, z + Random.Range(-1.1f, 1.1f));
                    if (UnderGlider(p) || DistToLine(new Vector2(p.x, p.z), GliderTrack) < 2.2f) continue;
                    float r = Random.value;
                    if (r < 0.14f) TallPoplar(g, p, poplarBark, Random.value < 0.5f ? leaves : leaves2);
                    else
                    {
                        var t = new GameObject("Tree (copse)"); t.transform.SetParent(g); t.transform.position = p;
                        int kind = r < 0.55f ? 3 : 4;
                        AddSmallTree(t, kind, kind == 3 ? Random.Range(5.5f, 8f) : Random.Range(3.5f, 5f));
                    }
                    n++;
                }
            }
            Debug.Log($"Stan's Game: {n} trees in the copse between the east-bank road and the landing field");
        }
    }
}
