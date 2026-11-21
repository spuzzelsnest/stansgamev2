using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// "Euston II", the Orne half of the coup de main (Joan, Nov 2026; Neil Barber's "The Pegasus and Orne Bridges" for
    /// reference): two Horsas on landing zone Y, the strip of land between the canal and the river - chalk 96 with
    /// Lt Dennis Fox's 17 Platoon about 300 m from the Ranville bridge, chalk 95 with Lt Tod Sweeney's 23 Platoon far
    /// short of it to the north - and a small German guard on the bridge (a sentry on the swing span, an MG 42 dug in
    /// at the east end, riflemen in the trench beside it). The third Orne glider, chalk 94 (Capt Priday, Lt Hooper's
    /// 22 Platoon), landed by the Dives: it is only a message. See OrneAssault for what happens at run time.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static void PegasusOrneElement(Transform lz, Transform defences, Transform actors, GameObject rifle, GameObject bren, GameObject sten)
        {
            var g = Group("LZ Y - Orne gliders (Euston II)", lz);
            float bx = (horsaWestX + horsaEastX) * 0.5f;
            if (horsaEastX <= horsaWestX) { bx = OrneCentreX(0f); horsaWestX = bx - 25f; horsaEastX = bx + 25f; horsaDeckY = GroundAt(new Vector3(bx, 0f, 0f)) + 3f; }

            // the bridge guard
            var guardGroup = Group("Orne bridge guard", defences);
            var guard = new List<Health>();
            Vector3 east = new Vector3(horsaEastX, 0f, 0f);
            Health Add(string n, Vector3 p, float yaw, bool onDeck = false)
            {
                p.y = onDeck ? horsaDeckY : GroundAt(p);
                var h = Soldier(n, guardGroup, p, yaw);
                if (h) guard.Add(h);
                return h;
            }
            Add("Sentry on the Orne bridge (west end)", new Vector3(horsaWestX - 5f, 0f, 1.8f), 90f);
            Add("MG42 at the Orne bridge (east end)", east + new Vector3(5f, 0f, 5f), 270f);
            Add("Rifleman, Orne bridge trench 1", east + new Vector3(8f, 0f, -6f), 270f);
            Add("Rifleman, Orne bridge trench 2", east + new Vector3(9f, 0f, 8f), 250f);
            Add("NCO, Orne bridge", east + new Vector3(14f, 0f, -2f), 270f);
            Sandbags(guardGroup, east + new Vector3(3.5f, 0f, 5f), 270f);

            // invisible kerbs along both sides of the swing span and its ramps: men fighting their way over keep to the deck
            // instead of stepping off into the Orne (Ignore Raycast, so bullets and grenades pass)
            var kerbs = new GameObject("Orne bridge kerbs (invisible)").transform;
            var hbGroup = GameObject.Find("Horsa Bridge (Ranville, River Orne)");   // with the bridge, out of reach of the snap-to-ground pass
            kerbs.SetParent(hbGroup ? hbGroup.transform : guardGroup);
            float kx0 = horsaWestX - 3f, kx1 = horsaEastX + 3f;
            foreach (float side in new[] { -1f, 1f })
            {
                var k = new GameObject(side < 0 ? "Kerb south" : "Kerb north"); k.transform.SetParent(kerbs);
                k.layer = 2;
                k.transform.position = new Vector3((kx0 + kx1) * 0.5f, horsaDeckY, side * 3.6f);
                var bc = k.AddComponent<BoxCollider>(); bc.size = new Vector3(kx1 - kx0, 6f, 0.4f);
            }

            // the two gliders that got there, coming in from the north over the river mouth. Placed from the June 1944
            // aerial photo (Photos/Pegasus Bridge/Pic_BridgesAerial_high_indications.jpg, ~1.5 m a pixel from the Horsa's
            // 27 m span): 96 about 190 m north of the bridge and 230 m west of the river; 95 about 1.2 km north and 260 m
            // west - beyond this map, so it comes down at the north edge and Sweeney's men have the longest run.
            var fox = OrneGlider(g, "Horsa glider 96 (Lt Fox, 17 Platoon)", 96, DrySpot(new Vector3(OrneCentreX(190f) - 230f, 0f, 190f), bx), 188f, 8f, 70f);
            var sweeney = OrneGlider(g, "Horsa glider 95 (Lt Sweeney, 23 Platoon)", 95, DrySpot(new Vector3(OrneCentreX(730f) - 260f, 0f, 730f), bx), 182f, -6f, 95f);

            var target = new GameObject("Objective: Orne bridge (east end)").transform;
            target.SetParent(g); target.position = east + new Vector3(6f, 0f, 0f); target.position = new Vector3(target.position.x, GroundAt(target.position), 0f);
            Transform Way(string n, Vector3 p) { var t = new GameObject(n).transform; t.SetParent(g); p.y = GroundAt(p); t.position = p; return t; }
            // onto the Ranville road well short of the river, then along it to the bridge
            float rx = horsaWestX - 60f;
            var road = Way("Orne route 1: Ranville road", new Vector3(rx, 0f, RoadZ(rx)));
            var approach = Way("Orne route 2: bridge (west end)", new Vector3(horsaWestX - 20f, 0f, RoadZ(horsaWestX - 20f)));
            var oa = new GameObject("Orne assault (Euston II)").AddComponent<OrneAssault>();
            oa.transform.SetParent(actors);
            oa.gliders = new[] { fox.ga, sweeney.ga };
            oa.exits = new[] { fox.exit, sweeney.exit };
            oa.landedMessages = new[]
            {
                "Glider 96 down by the Orne: Lt Fox's platoon goes for the river bridge.",
                "Glider 95 down well short of the river bridge: Lt Sweeney's platoon comes on at the double.",
            };
            oa.rifle = rifle; oa.bren = bren; oa.sten = sten;
            oa.route = new[] { road, approach, target }; oa.guard = guard.ToArray();
        }

        static (GliderApproach ga, Transform exit) OrneGlider(Transform parent, string name, int chalk, Vector3 rest, float yaw, float slew, float delay)
        {
            var marker = new GameObject($"{name} rest point").transform;
            marker.SetParent(parent);
            marker.SetPositionAndRotation(rest, Quaternion.Euler(0f, yaw, 0f));
            var go = BuildHorsa(name, parent, chalk);
            go.transform.SetPositionAndRotation(marker.position, marker.rotation * Quaternion.Euler(0f, slew, 0f));
            var ga = go.AddComponent<GliderApproach>();
            ga.landingPoint = marker; ga.delay = delay; ga.slew = slew; ga.damage = GliderApproach.Damage.WheelsTorn;
            ga.settleDrop = 0.25f; ga.settlePitch = 2f; ga.settleRoll = 3f;
            var exit = new GameObject($"Glider {chalk} exit").transform;
            exit.SetParent(go.transform); exit.localPosition = new Vector3(-3.4f, 0.9f, 3.5f); exit.localRotation = Quaternion.identity;
            AddNet(go);
            return (ga, exit);
        }

        /// <summary>The nearest point to 'want' on firm ground of landing zone Y: off the river, the canal and the roads, above the marsh, with room to skid.</summary>
        static Vector3 DrySpot(Vector3 want, float riverX)
        {
            var roads = RoadSegments();
            bool Ok(float x, float z)
            {
                if (OrneDistance(x, z) < 30f || x < CanalHalfWidth + 45f || x > riverX - 30f) return false;
                foreach (var s in roads) if (SegDist(new Vector2(x, z), s) < s.halfWidth + 12f) return false;
                for (float back = 0f; back <= 60f; back += 15f)   // the skid: firm ground behind the rest point (it came in from the north)
                    if (TerrainY(x, z + back) < MarshWaterY + 0.4f) return false;
                return Mathf.Abs(z) < 790f;
            }
            if (Ok(want.x, want.z)) return new Vector3(want.x, GroundAt(want), want.z);
            for (float r = 10f; r <= 200f; r += 10f)
                for (int a = 0; a < 16; a++)
                {
                    float x = want.x + Mathf.Cos(a * Mathf.PI / 8f) * r, z = want.z + Mathf.Sin(a * Mathf.PI / 8f) * r;
                    if (Ok(x, z)) { var p = new Vector3(x, 0f, z); p.y = GroundAt(p); return p; }
                }
            Debug.LogWarning($"Stan's Game: no dry spot near {want} for an Orne glider - using it as it is");
            return new Vector3(want.x, GroundAt(want), want.z);
        }
    }
}
