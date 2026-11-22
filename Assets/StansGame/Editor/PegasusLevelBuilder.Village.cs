using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Proving Ground: a little Normandy hamlet between the Allied and the German camps, south of the training station
    /// (x -36..36, z -60..-22), to try out Joan's wall and street textures of 22 Nov before they go into the levels:
    ///   Village_Limestone  Caen-stone ashlar (stone-castle-wall-material-01) - most houses, as in Bénouville
    ///   Village_Rubble     rubble stone in lime mortar (handmade-stone-wall-material) - farm houses, garden walls
    ///   Village_Brick      weathered red brick (brick-wall-material) - the café, like the brick houses by the canal
    ///   Village_Planks     old boards (old-wooden-wall-tile-texture) - the barn
    ///   Village_Cobbles    cobbles (cobblestone-wall-tile-texture) - the street and the square
    ///   Village_Flagstones stone flags (stone-patterned-tiles-tile-texture) - the café terrace
    ///   Village_Slate      slate roofs, drawn by script (no slate texture among the downloads yet)
    /// The walls are boxes whose texture is laid on in metres (not stretched per face), so a brick is a brick on a
    /// small house and on a long one. Textures in Textures/Village (1024 px, from the 2048-4096 px downloads).
    /// Not used: under-ground-wall (an iron grid, not 1944 Normandy), the stone-wall-3d-scan (a sheet for its own
    /// scanned mesh, not a tiling texture), the two rock sheets (cliffs) - kept for later.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string VillageTexDir = Root + "/Textures/Village";

        static Material VillageMat(string name, float metres, float smooth)
        {
            var m = TexMat(name, $"{VillageTexDir}/{name}.jpg", Color.white, smooth, false, $"{VillageTexDir}/{name}_N.jpg");
            var scale = Vector2.one / metres;   // the box meshes carry their UVs in metres
            m.SetTextureScale("_BaseMap", scale); m.mainTextureScale = scale;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A box mesh with its texture laid on in metres (UVs from the face's own plane), saved as an asset.</summary>
        static GameObject VBox(string name, Transform parent, Vector3 localPos, Vector3 localEuler, Vector3 size, Material m, bool collider = true)
        {
            string key = $"VBox_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}".Replace(',', '.');
            Vector3 h = size / 2f;
            var faces = new List<Vector3[]>();
            void Quad(Vector3 c, Vector3 u, Vector3 v) => faces.Add(new[] { c - u - v, c + u - v, c + u + v, c - u + v });   // u x v points out
            Quad(new Vector3(h.x, 0f, 0f), new Vector3(0f, h.y, 0f), new Vector3(0f, 0f, h.z));
            Quad(new Vector3(-h.x, 0f, 0f), new Vector3(0f, 0f, h.z), new Vector3(0f, h.y, 0f));
            Quad(new Vector3(0f, h.y, 0f), new Vector3(0f, 0f, h.z), new Vector3(h.x, 0f, 0f));
            Quad(new Vector3(0f, -h.y, 0f), new Vector3(h.x, 0f, 0f), new Vector3(0f, 0f, h.z));
            Quad(new Vector3(0f, 0f, h.z), new Vector3(h.x, 0f, 0f), new Vector3(0f, h.y, 0f));
            Quad(new Vector3(0f, 0f, -h.z), new Vector3(0f, h.y, 0f), new Vector3(h.x, 0f, 0f));
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos; go.transform.localRotation = Quaternion.Euler(localEuler);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(key, faces);
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            if (collider) go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>
        /// A village house: one or two storeys, a 45-degree slate roof with its ridge along the street, gables with
        /// chimneys, shuttered windows, a door, and a shop front with its sign board if a name is given. Front = local +Z.
        /// </summary>
        static GameObject VillageHouse(string name, Transform parent, Vector3 pos, float yaw, float w, float d, int storeys, Material wall, Material roof, System.Random rng, string shop = null)
        {
            var h = new GameObject(name).transform; h.SetParent(parent);
            h.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            float H = storeys * 3.0f + 0.3f;
            var glass = Mat("Window_Dark", new Color(0.05f, 0.05f, 0.06f), 0.8f);
            Color[] shutterCols = { new Color(0.42f, 0.45f, 0.40f), new Color(0.75f, 0.74f, 0.70f), new Color(0.30f, 0.22f, 0.16f), new Color(0.32f, 0.38f, 0.42f) };
            int sc = rng.Next(shutterCols.Length);
            var shutter = Mat($"SME_Shutter_{sc}", shutterCols[sc]);
            var door = Mat("SME_Door_Oak", new Color(0.22f, 0.15f, 0.09f));
            var plinth = VillageMat("Village_Rubble", 2f, 0.1f);
            void P(string n, Vector3 lp, Vector3 size, Material m) => Prim(PrimitiveType.Cube, n, h, lp, Vector3.zero, size, m);

            VBox("Walls", h, new Vector3(0f, H / 2f - 0.5f, 0f), Vector3.zero, new Vector3(w, H + 1f, d), wall);
            VBox("Plinth", h, new Vector3(0f, 0.3f, 0f), Vector3.zero, new Vector3(w + 0.12f, 0.6f, d + 0.12f), plinth, false);
            float half = d / 2f + 0.35f, rise = half, slope = half * 1.4142f;
            VBox("Roof front", h, new Vector3(0f, H + rise / 2f, d / 4f + 0.1f), new Vector3(45f, 0f, 0f), new Vector3(w + 0.4f, 0.2f, slope), roof);
            VBox("Roof back", h, new Vector3(0f, H + rise / 2f, -d / 4f - 0.1f), new Vector3(-45f, 0f, 0f), new Vector3(w + 0.4f, 0.2f, slope), roof);
            foreach (float sx in new[] { -1f, 1f })
            {
                // the gable: a square turned 45 degrees, its top half the roof triangle
                VBox("Gable", h, new Vector3(sx * (w / 2f - 0.15f), H, 0f), new Vector3(45f, 0f, 0f), new Vector3(0.3f, (half - 0.3f) * 1.4142f, (half - 0.3f) * 1.4142f), wall, false);
                if (rng.NextDouble() < 0.8)
                    VBox("Chimney", h, new Vector3(sx * (w / 2f - 0.5f), H + rise + 0.5f, 0f), Vector3.zero, new Vector3(0.7f, 1.8f, 1.0f), plinth);
            }
            int nWin = Mathf.Max(2, Mathf.RoundToInt(w / 2.6f));
            for (int f = 0; f < storeys; f++)
                for (int k = 0; k < nWin; k++)
                {
                    float x = -w / 2f + (k + 0.5f) * w / nWin, y = 1.6f + f * 3.0f;
                    foreach (float side in new[] { 1f, -1f })
                    {
                        if (f == 0 && side > 0f && (shop != null || k == nWin / 2)) continue;
                        P("Window", new Vector3(x, y, side * (d / 2f + 0.02f)), new Vector3(0.95f, 1.6f, 0.05f), glass);
                        if (side > 0f)
                            foreach (float sx in new[] { -1f, 1f })
                                P("Shutter", new Vector3(x + sx * 0.74f, y, d / 2f + 0.06f), new Vector3(0.5f, 1.65f, 0.05f), shutter);
                    }
                }
            if (shop != null)
            {
                var sign = Mat("Village_ShopSign", new Color(0.25f, 0.30f, 0.22f));
                P("Shop window", new Vector3(-w * 0.12f, 1.5f, d / 2f + 0.03f), new Vector3(w * 0.55f, 1.9f, 0.06f), glass);
                P("Shop front frame", new Vector3(0f, 1.55f, d / 2f + 0.02f), new Vector3(w * 0.92f, 2.5f, 0.04f), sign);
                P("Shop sign", new Vector3(0f, 3.0f, d / 2f + 0.08f), new Vector3(w * 0.8f, 0.5f, 0.08f), sign);
                FitLabel(h, shop, h.TransformPoint(new Vector3(0f, 3.0f, d / 2f + 0.125f)), w * 0.74f, 0.4f, new Color(0.93f, 0.9f, 0.8f), h.rotation * Quaternion.Euler(0f, 180f, 0f));   // read from the street
                P("Door", new Vector3(w * 0.3f, 1.1f, d / 2f + 0.04f), new Vector3(1.0f, 2.2f, 0.06f), door);
            }
            else
                P("Door", new Vector3(-w / 2f + (nWin / 2 + 0.5f) * w / nWin, 1.1f, d / 2f + 0.03f), new Vector3(1.0f, 2.2f, 0.06f), door);
            GameObjectUtility.SetStaticEditorFlags(h.gameObject, StaticEditorFlags.BatchingStatic);
            return h.gameObject;
        }

        static void BuildVillage(Transform root, List<Bounds> keepOut)
        {
            var g = Group("Hamlet (texture test)", root);
            var rng = new System.Random(1944);
            var lime = VillageMat("Village_Limestone", 2.5f, 0.12f);
            var rubble = VillageMat("Village_Rubble", 2f, 0.1f);
            var brick = VillageMat("Village_Brick", 2f, 0.12f);
            var planks = VillageMat("Village_Planks", 2f, 0.15f);
            var cobbles = VillageMat("Village_Cobbles", 1.6f, 0.2f);
            var flags = VillageMat("Village_Flagstones", 2f, 0.2f);
            var slate = VillageMat("Village_Slate", 2f, 0.35f);
            const float StreetZ = -38f;

            // the cobbled street east-west, the square in the middle, the lane north to the training station
            VBox("Street (cobbles)", g, new Vector3(0f, 0.03f, StreetZ), Vector3.zero, new Vector3(74f, 0.06f, 6f), cobbles, false);
            VBox("Square (cobbles)", g, new Vector3(0f, 0.035f, StreetZ), Vector3.zero, new Vector3(16f, 0.07f, 14f), cobbles, false);
            VBox("Café terrace (flagstones)", g, new Vector3(0f, 0.045f, StreetZ - 4.0f), Vector3.zero, new Vector3(10f, 0.09f, 2.4f), flags, false);

            // north side (fronts to the south) and south side (fronts to the north)
            var north = new (float x, float w, int st, Material m, string shop)[]
            {
                (-30.5f, 9f, 2, lime, null), (-20.5f, 9f, 2, lime, "BOULANGERIE"), (-11f, 8f, 1, rubble, null),
                (11f, 8f, 2, lime, null), (20.5f, 9f, 2, lime, "ÉPICERIE"), (30.5f, 9f, 1, rubble, null),
            };
            foreach (var hs in north)
                VillageHouse($"House (north side, x {hs.x:0})", g, new Vector3(hs.x, 0f, StreetZ + 7.5f), 180f, hs.w, 7f, hs.st, hs.m, slate, rng, hs.shop);
            var south = new (float x, float w, int st, Material m, string shop)[]
            {
                (-30.5f, 9f, 2, lime, null), (-20.5f, 9f, 1, rubble, null), (-10.5f, 9f, 2, lime, null),
                (0f, 11f, 2, brick, "CAFÉ DU MARCHÉ"), (11f, 8f, 2, lime, null), (21f, 9f, 1, rubble, null),
            };
            foreach (var hs in south)
                VillageHouse($"House (south side, x {hs.x:0})", g, new Vector3(hs.x, 0f, StreetZ - 7.5f - (hs.shop != null ? 1.2f : 0f)), 0f, hs.w, 7f, hs.st, hs.m, slate, rng, hs.shop);
            // the barn at the east end of the south side: boards on a rubble base, big double doors to the street
            var barn = new GameObject("Barn (boards)").transform; barn.SetParent(g); barn.position = new Vector3(31f, 0f, StreetZ - 8f);
            VBox("Barn base", barn, new Vector3(0f, 0.6f, 0f), Vector3.zero, new Vector3(9f, 1.2f, 9f), rubble);
            VBox("Barn walls", barn, new Vector3(0f, 2.9f, 0f), Vector3.zero, new Vector3(8.8f, 3.4f, 8.8f), planks);
            // ridge along the street, like the houses (the slates then run the right way)
            VBox("Barn roof front", barn, new Vector3(0f, 6.93f, 2.33f), new Vector3(45f, 0f, 0f), new Vector3(9.4f, 0.2f, 6.6f), slate);
            VBox("Barn roof back", barn, new Vector3(0f, 6.93f, -2.33f), new Vector3(-45f, 0f, 0f), new Vector3(9.4f, 0.2f, 6.6f), slate);
            foreach (float sx in new[] { -1f, 1f })
                VBox("Barn gable", barn, new Vector3(sx * 4.3f, 4.75f, 0f), new Vector3(45f, 0f, 0f), new Vector3(0.2f, 6.1f, 6.1f), planks, false);
            VBox("Barn doors", barn, new Vector3(0f, 1.8f, 4.45f), Vector3.zero, new Vector3(3.4f, 3.4f, 0.12f), Mat("Village_BarnDoor", new Color(0.28f, 0.2f, 0.12f)), false);

            // garden walls of rubble behind the houses, and the village pump on the square
            foreach (float side in new[] { -1f, 1f })
                foreach (float x0 in new[] { -35f, 6.5f })
                    VBox("Garden wall", g, new Vector3(x0 + 14.25f, 0.6f, StreetZ + side * 18.5f), Vector3.zero, new Vector3(28.5f, 1.2f, 0.5f), rubble);
            var pump = new GameObject("Village pump").transform; pump.SetParent(g); pump.position = new Vector3(5.5f, 0f, StreetZ + 3.5f);
            VBox("Pump trough", pump, new Vector3(0f, 0.35f, 0f), Vector3.zero, new Vector3(1.8f, 0.7f, 0.8f), lime);
            Prim(PrimitiveType.Cylinder, "Pump", pump, new Vector3(0f, 1.0f, -0.25f), Vector3.zero, new Vector3(0.25f, 0.65f, 0.25f), Mat("SME_Iron", new Color(0.1f, 0.1f, 0.1f), 0.4f, 0.8f), true);
            // a few things about the street (the props Joan downloaded for Sainte-Mère-Église)
            foreach (var (f, p, y) in new[] { ("Barrel", new Vector3(-2.8f, 0f, StreetZ - 4.2f), 0f), ("Barrel", new Vector3(-3.5f, 0f, StreetZ - 4.6f), 40f),
                                              ("CrateSmall", new Vector3(18.6f, 0f, StreetZ + 3.6f), 10f), ("Bucket", new Vector3(6.6f, 0f, StreetZ + 3.0f), 0f),
                                              ("Wheelbarrow", new Vector3(27.5f, 0f, StreetZ - 3.6f), 200f), ("TelegraphPole", new Vector3(-14f, 0f, StreetZ - 3.4f), 0f),
                                              ("TelegraphPole", new Vector3(16f, 0f, StreetZ - 3.4f), 0f) })
                SmeProp(f, g, p, y);
            CampSign(g, new Vector3(-37f, 0f, StreetZ - 4f), 90f, "LE HAMEAU");

            // nothing else is put in the village (trees, strollers' spawn points)
            keepOut.Add(new Bounds(new Vector3(0f, 2f, StreetZ), new Vector3(76f, 10f, 40f)));
        }
    }
}
