using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The Proving Ground's camps (Joan, Nov 2026): west of the range an Allied base camp - British lines and American
    /// lines either side of a gravel road, with sentry posts at both ends - and east of it a German field camp round
    /// the motor pool. Tents are made here (no models yet): British bell tents and a marquee, the American M1934
    /// pyramidal tents, the German four-man Zeltbahn tents in splinter pattern. The German camp has no flag on purpose:
    /// the Wehrmacht's flags carried the swastika, which stays out of Stan's game.
    /// Also the late-afternoon light and the colour grade, and the painted ground (roads, worn earth, grass).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        // ---------------------------------------------------------------- layout (metres; the range runs north, +z)
        const float AlliedRoadX = -80f, CampTrackZ = -14f, GermanParkX = 100f;
        static readonly Rect BritishLines = new Rect(-74f, -45f, 34f, 150f);    // x -74..-40
        static readonly Rect AmericanLines = new Rect(-126f, -45f, 40f, 150f);  // x -126..-86
        static readonly Rect GermanLines = new Rect(38f, -45f, 92f, 150f);      // x 38..130

        /// <summary>Roads and paths, for the painted ground: (from, to, half width, kind) - kind 3 gravel, 2 packed path.</summary>
        static readonly (Vector2 a, Vector2 b, float hw, int kind)[] PgRoads =
        {
            (new Vector2(AlliedRoadX, -58f), new Vector2(AlliedRoadX, 124f), 3.2f, 3),      // the Allied camp road
            (new Vector2(AlliedRoadX, CampTrackZ), new Vector2(-12f, CampTrackZ), 2.2f, 3),  // track to the station (west)
            (new Vector2(12f, CampTrackZ), new Vector2(GermanParkX - 6f, CampTrackZ), 2.2f, 3), // track to the German camp (east)
            (new Vector2(-12f, -1f), new Vector2(12f, -1f), 5.5f, 3),                       // the training station apron
            (new Vector2(GermanParkX, -46f), new Vector2(GermanParkX, 70f), 7f, 3),         // the German motor pool
            (new Vector2(-57f, CampTrackZ), new Vector2(-57f, 100f), 1.4f, 2),              // British lines: the path between the tents
            (new Vector2(-105f, CampTrackZ), new Vector2(-105f, 100f), 1.6f, 2),            // American lines
            (new Vector2(56f, CampTrackZ), new Vector2(56f, 98f), 1.6f, 2),                 // German camp
            (new Vector2(-22f, 6f), new Vector2(-22f, 112f), 1.0f, 2),                      // footpath down the range, inside the wire
            (new Vector2(22f, 6f), new Vector2(22f, 112f), 1.0f, 2),
            (new Vector2(-37f, -38f), new Vector2(-42f, -53f), 2.4f, 3),                     // the hamlet: lane west, round the south end
            (new Vector2(-42f, -53f), new Vector2(AlliedRoadX, -53f), 2.4f, 3),               // of the British lines to the Allied road
            (new Vector2(37f, -38f), new Vector2(46f, -20f), 2.0f, 3),                        // lane north-east to the German track
            (new Vector2(0f, -31f), new Vector2(0f, -7f), 1.4f, 2),                           // path up to the training station
        };

        static float PgRoadDist(float x, float z, int kind)
        {
            var p = new Vector2(x, z); float d = float.MaxValue;
            foreach (var r in PgRoads)
            {
                if (r.kind != kind) continue;
                Vector2 ab = r.b - r.a; float t = Mathf.Clamp01(Vector2.Dot(p - r.a, ab) / ab.sqrMagnitude);
                d = Mathf.Min(d, Vector2.Distance(p, r.a + ab * t) - r.hw);
            }
            return d;
        }

        /// <summary>Worn, bare ground: (x, z, radius) - gun pits, the firing point, round the tents and the gates.</summary>
        static readonly List<Vector3> pgWorn = new List<Vector3>();

        /// <summary>
        /// The ground layers at a point (the six Poly Haven scans: 0 pasture, 1 turf, 2 packed path, 3 gravel, 4 mud,
        /// 5 trampled earth): short turf over the working area, rough pasture beyond, gravel roads, worn earth in the
        /// camps and pits, mud where the lorries turn.
        /// </summary>
        static void PgGroundWeights(float x, float z, float[] w)
        {
            for (int i = 0; i < 6; i++) w[i] = 0f;
            bool inside = PgFlat.Contains(new Vector2(x, z));
            float n = Mathf.PerlinNoise(x * 0.045f + 5f, z * 0.045f + 9f), n2 = Mathf.PerlinNoise(x * 0.11f + 40f, z * 0.11f + 3f);
            // grass: turf (mown) in the camps and on the range, pasture further out and in patches
            float turf = inside ? Mathf.Clamp01(0.75f + (n - 0.5f) * 1.2f) : Mathf.Clamp01((n - 0.62f) * 3f);
            w[1] = turf; w[0] = 1f - turf;
            void Over(int layer, float a) { a = Mathf.Clamp01(a); if (a <= 0f) return; for (int i = 0; i < 6; i++) w[i] *= 1f - a; w[layer] += a; }
            // worn earth round the pits, the firing point and the tents
            foreach (var p in pgWorn)
            {
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(p.x, p.z));
                if (d < p.y * 1.6f) Over(5, (1f - Smooth01(p.y * 0.6f, p.y * 1.6f, d)) * (0.65f + n2 * 0.5f));
            }
            if (Mathf.Abs(x) < 23f && z > 0.5f && z < 6.5f) Over(5, 0.55f + n2 * 0.4f);   // the firing point, trodden bare
            // the paths and roads, with ragged worn verges
            float path = PgRoadDist(x, z, 2);
            if (path < 1.2f) Over(2, 1f - Mathf.Clamp01((path + 0.2f) / 1.4f) + (n2 - 0.5f) * 0.4f);
            float road = PgRoadDist(x, z, 3);
            if (road < 2.5f) Over(5, (1f - Mathf.Clamp01((road - 0.2f) / 2.3f)) * 0.7f);   // verges
            if (road < 0.6f) Over(3, 1f - Mathf.Clamp01((road + 0.4f) / 1f) + (n2 - 0.5f) * 0.3f);
            // mud where the German lorries turn and by the Allied road's south end
            if ((x > GermanParkX - 12f && x < GermanParkX + 12f && z < -30f) || (Mathf.Abs(x - AlliedRoadX) < 7f && z < -44f))
                Over(4, Mathf.Clamp01((n2 - 0.45f) * 2.5f));
            float sum = 0f; for (int i = 0; i < 6; i++) sum += w[i];
            if (sum > 0f) for (int i = 0; i < 6; i++) w[i] /= sum;
        }

        /// <summary>
        /// Paints the Proving Ground like Pegasus Bridge: the six scanned ground layers (turf, pasture, gravel roads,
        /// packed paths, worn earth, mud) and the swaying short and long grass. Runs after everything is placed, so the
        /// worn ground round the pits and tents is known.
        /// </summary>
        static void PaintProvingGround()
        {
            if (!terrainRef) return;
            var data = terrainRef.terrainData;
            // making the layer and grass assets can make Unity reload the terrain data from disk: keep the heights to put back
            int hr = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, hr, hr);
            var layers = GroundLayers();
            if (layers == null) { Debug.LogWarning("Stan's Game: ground scans not in the project - the Proving Ground keeps plain turf"); return; }
            // the range's pits and the firing point's guns
            foreach (var p in new[] { new Vector3(-11f, 3f, 3.6f), new Vector3(11f, 3f, 3.6f), new Vector3(-17.5f, 4f, 4f), new Vector3(17.5f, 4f, 4f),
                                      new Vector3(-30.5f, 3f, 40f), new Vector3(30.5f, 3f, 40f), new Vector3(-30.5f, 3f, 95f), new Vector3(30.5f, 3f, 95f),
                                      new Vector3(-21f, 4f, -5f), new Vector3(14f, 4f, 78f) })
                pgWorn.Add(p);
            data.terrainLayers = layers;
            const int amRes = 512;
            data.alphamapResolution = amRes;
            float size = data.size.x; Vector3 tp = terrainRef.transform.position;
            var am = new float[amRes, amRes, 6]; var w = new float[6];
            for (int z = 0; z < amRes; z++)
                for (int x = 0; x < amRes; x++)
                {
                    float wx = tp.x + x / (amRes - 1f) * size, wz = tp.z + z / (amRes - 1f) * size;
                    PgGroundWeights(wx, wz, w);
                    for (int i = 0; i < 6; i++) am[z, x, i] = w[i];
                }
            data.SetAlphamaps(0, 0, am);

            var shortGrass = MakeGrassTexture("GrassBlades", new Color(0.30f, 0.38f, 0.18f), 0.75f);
            var longGrass = MakeGrassTexture("GrassLong", new Color(0.38f, 0.40f, 0.22f), 1.0f);
            data.detailPrototypes = new[]
            {
                new DetailPrototype { prototypeTexture = shortGrass, renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false,
                    minWidth = 0.3f, maxWidth = 0.55f, minHeight = 0.12f, maxHeight = 0.3f, noiseSpread = 0.3f,
                    healthyColor = new Color(0.58f, 0.68f, 0.40f), dryColor = new Color(0.66f, 0.62f, 0.40f) },
                new DetailPrototype { prototypeTexture = longGrass, renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false,
                    minWidth = 0.35f, maxWidth = 0.7f, minHeight = 0.35f, maxHeight = 0.75f, noiseSpread = 0.5f,
                    healthyColor = new Color(0.56f, 0.62f, 0.38f), dryColor = new Color(0.74f, 0.66f, 0.42f) },
            };
            const int detRes = 640;
            data.SetDetailResolution(detRes, 32);
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            var d0 = new int[detRes, detRes]; var d1 = new int[detRes, detRes];
            for (int z = 0; z < detRes; z++)
                for (int x = 0; x < detRes; x++)
                {
                    float wx = tp.x + x / (detRes - 1f) * size, wz = tp.z + z / (detRes - 1f) * size;
                    PgGroundWeights(wx, wz, w);
                    float bare = w[2] + w[3] + w[4] + w[5];
                    if (bare > 0.5f || Random.value < bare) continue;
                    float n = Mathf.PerlinNoise(wx * 0.08f + 3f, wz * 0.08f + 11f);
                    bool inside = PgFlat.Contains(new Vector2(wx, wz));
                    d0[z, x] = n > 0.25f ? 3 + Mathf.RoundToInt(n * 5f) : 2;
                    if (!inside || n > 0.72f) d1[z, x] = inside ? 1 : 2 + Mathf.RoundToInt(n * 3f);   // rough long grass out in the fields
                }
            data.SetDetailLayer(0, 0, 0, d0);
            data.SetDetailLayer(0, 0, 1, d1);
            data.wavingGrassAmount = 0.35f; data.wavingGrassSpeed = 0.45f; data.wavingGrassStrength = 0.4f;
            data.wavingGrassTint = new Color(0.62f, 0.66f, 0.5f);
            terrainRef.detailObjectDistance = 120f; terrainRef.detailObjectDensity = 1f;
            data.SetHeights(0, 0, heights);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- light and colour

        /// <summary>
        /// A warm late afternoon in June: the sun low in the west-south-west, long shadows across the range, and the
        /// same punchy grade as Pegasus Bridge (ACES, contrast, saturation, bloom), tuned for daylight.
        /// </summary>
        static void PgLight(Transform root, Light sun, Material sky)
        {
            sun.transform.rotation = Quaternion.Euler(42f, 62f, 0f);    // from the south-west, behind the left shoulder looking down the range
            sun.color = new Color(1f, 0.92f, 0.80f); sun.intensity = 2.0f; sun.shadowStrength = 0.8f;
            if (sky)
            {
                sky.SetFloat("_SunSize", 0.04f); sky.SetFloat("_SunSizeConvergence", 6f);
                sky.SetFloat("_AtmosphereThickness", 0.8f); sky.SetFloat("_Exposure", 1.35f);
                sky.SetColor("_SkyTint", new Color(0.42f, 0.56f, 0.82f)); sky.SetColor("_GroundColor", new Color(0.42f, 0.40f, 0.34f));
                EditorUtility.SetDirty(sky);
            }
            // the sky's own ambient is never worked out for a scene built in code: give it the colours of a clear day
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.84f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.54f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.28f, 0.23f);
            RenderSettings.reflectionIntensity = 0.6f;
            RenderSettings.fogColor = new Color(0.80f, 0.80f, 0.78f);
            RenderSettings.fogStartDistance = 180f; RenderSettings.fogEndDistance = 1100f;

            string dir = Root + "/Settings";
            System.IO.Directory.CreateDirectory(dir);
            string path = $"{dir}/ProvingGroundGrade.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.3f); ca.contrast.Override(20f); ca.saturation.Override(18f);
            var wb = profile.Add<WhiteBalance>(true); wb.temperature.Override(8f); wb.tint.Override(2f);
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.96f, 1f, 1.08f, -0.02f));      // cool, deep shadows
            smh.highlights.Override(new Vector4(1.08f, 1.02f, 0.9f, 0.02f));  // warm sunlight
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f); bloom.intensity.Override(0.6f); bloom.scatter.Override(0.7f); bloom.tint.Override(new Color(1f, 0.9f, 0.78f));
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.2f); vig.smoothness.Override(0.45f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            var go = new GameObject("Colour grade (GTA look, afternoon)");
            go.transform.SetParent(root);
            var vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 1f; vol.weight = 1f; vol.sharedProfile = profile;
        }

        static void PgPostProcessingOn()
        {
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (!data) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
            }
        }

        // ---------------------------------------------------------------- the camps

        /// <summary>Builds the camps; returns where each side's soldiers stroll (British, American, German).</summary>
        static (Bounds british, Bounds american, Bounds german) BuildCamps(Transform root, List<Bounds> keepOut, Material wood)
        {
            var camps = Group("Camps", root);
            madeMeshes.Clear();
            BuildAlliedCamp(Group("Allied camp (west)", camps), keepOut, wood);
            BuildGermanCamp(Group("German camp (east)", camps), keepOut, wood);
            Bounds B(Rect r) => new Bounds(new Vector3(r.center.x, 0f, r.center.y), new Vector3(r.width - 4f, 20f, r.height - 4f));
            return (B(BritishLines), B(AmericanLines), B(GermanLines));
        }

        static void BuildAlliedCamp(Transform g, List<Bounds> keepOut, Material wood)
        {
            var brit = Group("British lines (6th Airborne)", g);
            var us = Group("American lines (82nd Airborne)", g);
            var khaki = CanvasMat("PG_Canvas_BellTent", new Color(0.66f, 0.62f, 0.50f), new Color(0.58f, 0.55f, 0.43f));
            var drab = CanvasMat("PG_Canvas_OliveDrab", new Color(0.33f, 0.33f, 0.22f), new Color(0.28f, 0.29f, 0.19f));
            var dark = Mat("PG_Tent_Door", new Color(0.12f, 0.11f, 0.09f), 0.05f);
            var pole = Mat("PG_Tent_Pole", new Color(0.36f, 0.27f, 0.17f), 0.2f);

            // British lines: two rows of bell tents along the path, the HQ marquee, the cookhouse, the flag
            float bx = -57f;
            for (float z = 6f; z <= 96f; z += 11f)
                foreach (float side in new[] { -1f, 1f })
                {
                    var p = new Vector3(bx + side * 6.5f, 0f, z + (side > 0 ? 3f : 0f));
                    var t = Tent("Bell tent", brit, BellTentMesh(), khaki, p, side > 0 ? 270f : 90f, pole, dark, 1.0f);
                    keepOut.Add(WorldBounds(t)); pgWorn.Add(new Vector3(p.x, 3f, p.z));
                }
            var hq = Tent("HQ marquee (company office)", brit, RidgeTentMesh("Marquee", 9f, 5f, 1.8f, 3.6f), khaki, new Vector3(-63f, 0f, -30f), 0f, pole, dark, 1f);
            keepOut.Add(WorldBounds(hq)); pgWorn.Add(new Vector3(-63f, 6f, -30f));
            Trestle(brit, new Vector3(-63f, 0f, -26.2f), 0f, wood);
            CookHouse(brit, new Vector3(-48f, 0f, -32f), wood, keepOut);
            var bj = Jeep(brit, "Willys jeep (6th Airborne)", new Vector3(-50f, 0f, -42f), 0f);
            keepOut.Add(WorldBounds(bj)); pgWorn.Add(new Vector3(-50f, 4f, -42f));
            Flag(brit, new Vector3(-73f, 0f, -20f), UnionFlag(), "Union Flag", wood);
            foreach (var (f, p, y) in new[] { ("CrateStackA", new Vector3(-70f, 0f, -38f), 10f), ("CrateStackB", new Vector3(-67f, 0f, -40f), -20f), ("SupplyStack", new Vector3(-46f, 0f, -42f), 0f) })
                { var o = SmeProp(f, brit, p, y); if (o) keepOut.Add(WorldBounds(o)); }
            CampSign(brit, new Vector3(-41f, 0f, CampTrackZ - 3.2f), 270f, "6TH AIRBORNE DIV.  BRITISH LINES");
            CampFire(brit, new Vector3(-52f, 0f, -24f));
            // the British transport by the south edge of the lines: Bedford OX lorries, empty, loaded, and the gun lorry
            foreach (var (file, x, label) in new[] { ("BedfordOX", -70f, "Bedford OX lorry"), ("BedfordOX_Loaded", -61f, "Bedford OX lorry (loaded)"), ("BedfordOX_Gun", -52f, "Bedford OX gun lorry") })
            {
                var bl = Bedford(brit, label, new Vector3(x, 0f, -47.5f), 90f, file);
                if (bl) { keepOut.Add(WorldBounds(bl)); pgWorn.Add(new Vector3(x, 6f, -47.5f)); }
            }
            // a Cromwell by the north end of the lines (the airborne division's armoured recce had them from July 1944)
            var crom = BuildArmour("Cromwell IV", brit, new Vector3(-66f, 0f, 108f), 90f, Armour.Cromwell);
            if (crom)
            {
                foreach (var hp in crom.GetComponentsInChildren<Health>(true)) hp.regenPerSecond = Mathf.Max(hp.regenPerSecond, 50f);
                keepOut.Add(WorldBounds(crom)); pgWorn.Add(new Vector3(-66f, 6f, 108f));
                CampSign(brit, new Vector3(-59f, 0f, 104f), 90f, "CROMWELL IV  (QF 75 MM)");
            }

            // American lines: two rows of pyramidal squad tents, the CP, the motor pool with two jeeps, the flag
            float ux = -105f;
            for (float z = 6f; z <= 96f; z += 12f)
                foreach (float side in new[] { -1f, 1f })
                {
                    var p = new Vector3(ux + side * 8f, 0f, z);
                    var t = Tent("Pyramidal tent M1934", us, PyramidTentMesh(), drab, p, side > 0 ? 270f : 90f, pole, dark, 1f);
                    keepOut.Add(WorldBounds(t)); pgWorn.Add(new Vector3(p.x, 3.8f, p.z));
                }
            var cp = Tent("Command post (CP)", us, RidgeTentMesh("CP tent", 8f, 5f, 1.7f, 3.4f), drab, new Vector3(-114f, 0f, -30f), 0f, pole, dark, 1f);
            keepOut.Add(WorldBounds(cp)); pgWorn.Add(new Vector3(-114f, 6f, -30f));
            Trestle(us, new Vector3(-114f, 0f, -26.2f), 0f, wood);
            Flag(us, new Vector3(-87f, 0f, -20f), StarsAndStripes(), "Stars and Stripes (48 stars)", wood);
            for (int i = 0; i < 2; i++)
            {
                var j = Jeep(us, $"Willys jeep {i + 1} (82nd Airborne)", new Vector3(-95f - i * 4f, 0f, -38f), 0f);
                keepOut.Add(WorldBounds(j)); pgWorn.Add(new Vector3(-97f, 5f, -38f));
            }
            foreach (var (f, p, y) in new[] { ("CrateStackB", new Vector3(-122f, 0f, -38f), 5f), ("CrateWide", new Vector3(-120f, 0f, -42f), 30f), ("Barrel", new Vector3(-90f, 0f, -42f), 0f), ("Barrel", new Vector3(-90.8f, 0f, -43f), 0f) })
                { var o = SmeProp(f, us, p, y); if (o) keepOut.Add(WorldBounds(o)); }
            CampSign(us, new Vector3(-86f, 0f, CampTrackZ - 3.2f), 270f, "82ND AIRBORNE DIV.  505TH P.I.R.");
            CampFire(us, new Vector3(-99f, 0f, -24f));
            // a Sherman by the north gate, drivable like the German tanks (BuildArmour turns it a further 90 degrees)
            var sherman = BuildArmour("M4A3E8 Sherman", us, new Vector3(-100f, 0f, 108f), 90f, Armour.Sherman);
            if (sherman)
            {
                foreach (var hp in sherman.GetComponentsInChildren<Health>(true)) hp.regenPerSecond = Mathf.Max(hp.regenPerSecond, 50f);
                keepOut.Add(WorldBounds(sherman)); pgWorn.Add(new Vector3(-100f, 6f, 108f));
                CampSign(us, new Vector3(-93f, 0f, 104f), 0f, "M4A3E8 SHERMAN (LATE 1944)");
            }
            var stu = BuildArmour("M5A1 Stuart", us, new Vector3(-113f, 0f, 108f), 90f, Armour.Stuart);
            if (stu)
            {
                foreach (var hp in stu.GetComponentsInChildren<Health>(true)) hp.regenPerSecond = Mathf.Max(hp.regenPerSecond, 50f);
                keepOut.Add(WorldBounds(stu)); pgWorn.Add(new Vector3(-113f, 5f, 108f));
                CampSign(us, new Vector3(-119f, 0f, 104f), 0f, "M5A1 STUART  (37 MM)");
            }
            // the M16 multiple gun motor carriage: the battalions' anti-aircraft half-tracks (the M45 Quadmount, four .50s)
            var m16 = BuildArmour("M16 MGMC half-track", us, new Vector3(-88f, 0f, 108f), 90f, Armour.M16);   // nose south, to the camp (printscreens 35, 37; BuildArmour adds 90)   // by the road (at -127 it stood in the watchtower, printscreen 31)
            if (m16)
            {
                foreach (var hp in m16.GetComponentsInChildren<Health>(true)) hp.regenPerSecond = Mathf.Max(hp.regenPerSecond, 50f);
                keepOut.Add(WorldBounds(m16)); pgWorn.Add(new Vector3(-88f, 5f, 108f));
                CampSign(us, new Vector3(-84.5f, 0f, 103.2f), 0f, "M16 MGMC  (4 X .50 CAL)");   // read from the camp (printscreen 35)
            }

            // the road through the camp, sentry posts with a barrier at both ends, a watchtower at each far corner
            foreach (float z in new[] { -50f, 118f })
            {
                Barrier(g, new Vector3(AlliedRoadX, 0f, z), wood);
                SentryPost(brit, new Vector3(AlliedRoadX + 5f, 0f, z), z < 0f ? 180f : 0f, "British_Infantry", "Sentry (British)", keepOut);
                SentryPost(us, new Vector3(AlliedRoadX - 5f, 0f, z), z < 0f ? 180f : 0f, "US_Paratrooper_HD", "Sentry (American)", keepOut);
            }
            Watchtower(g, new Vector3(-128f, 0f, 108f), wood, keepOut);
            Watchtower(g, new Vector3(-44f, 0f, 108f), wood, keepOut);
        }

        static void BuildGermanCamp(Transform g, List<Bounds> keepOut, Material wood)
        {
            var splinter = SplinterMat();
            var dark = Mat("PG_Tent_Door", new Color(0.12f, 0.11f, 0.09f), 0.05f);
            var pole = Mat("PG_Tent_Pole", new Color(0.36f, 0.27f, 0.17f), 0.2f);
            // four-man tents of buttoned Zeltbahnen, in rows either side of the camp path
            for (float z = 8f; z <= 92f; z += 7f)
                foreach (float x in new[] { 46f, 51f, 61f, 66f })
                {
                    var p = new Vector3(x + Random.Range(-0.3f, 0.3f), 0f, z + Random.Range(-0.4f, 0.4f));
                    var t = Tent("Zeltbahn tent (4-man)", g, ZeltbahnMesh(), splinter, p, x < 56f ? 90f : 270f, pole, dark, 1f);
                    keepOut.Add(WorldBounds(t)); pgWorn.Add(new Vector3(p.x, 2f, p.z));
                }
            var cmd = Tent("Command tent (Gefechtsstand)", g, RidgeTentMesh("Command tent", 8f, 4.6f, 1.6f, 3.2f), splinter, new Vector3(66f, 0f, -32f), 0f, pole, dark, 1f);
            keepOut.Add(WorldBounds(cmd)); pgWorn.Add(new Vector3(66f, 6f, -32f));
            Trestle(g, new Vector3(66f, 0f, -28.4f), 0f, wood);
            // the gate: the checkpoint and the barrier on the track from the station
            foreach (var (f, p, y) in new[] { ("Checkpoint", new Vector3(42f, 0f, CampTrackZ - 5.5f), 90f), ("BorderPost", new Vector3(42f, 0f, CampTrackZ + 4.5f), 90f) })
                { var o = SmeProp(f, g, p, y, 0f, f == "Checkpoint" ? "Checkpoint (camp gate)" : "Border post"); if (o) keepOut.Add(WorldBounds(o)); }
            Barrier(g, new Vector3(42f, 0f, CampTrackZ), wood, 90f);
            SentryPost(g, new Vector3(40f, 0f, CampTrackZ + 7.5f), 270f, "German_Rifleman_Heer_B", "Sentry (Heer)", keepOut);
            // the 2 cm Flakvierling 38 in its sandbagged pit, guarding the camp (not firing: nobody fights here)
            var flakAt = new Vector3(80f, 0f, 70f);
            var flak = new GameObject("2 cm Flakvierling 38 (pit)").transform; flak.SetParent(g); flak.position = flakAt;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Vehicles/Flakvierling38.fbx");
            if (model)
            {
                var fm = (GameObject)PrefabUtility.InstantiatePrefab(model, flak);
                PrefabUtility.UnpackPrefabInstance(fm, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                fm.transform.localPosition = Vector3.zero; fm.transform.localRotation = Quaternion.Euler(0f, 200f, 0f) * fm.transform.localRotation;
                foreach (var t in fm.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("MuzzleMarker")).ToArray()) Object.DestroyImmediate(t.gameObject);
                var paint = Mat("Flak_Dunkelgelb", new Color(0.42f, 0.38f, 0.25f), 0.35f, 0.3f);
                foreach (var r in fm.GetComponentsInChildren<Renderer>()) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = paint; r.sharedMaterials = ms; }
                var fb = WorldBounds(fm);
                var box = flak.gameObject.AddComponent<BoxCollider>(); box.center = fb.center - flakAt; box.size = fb.size * 0.8f;
            }
            SandbagRing(flak, flakAt, 3.8f, 1.1f, Vector3.back, 1);
            keepOut.Add(new Bounds(flakAt, new Vector3(9f, 4f, 9f))); pgWorn.Add(new Vector3(flakAt.x, 5f, flakAt.z));
            foreach (var p in new[] { new Vector3(76.5f, 0f, 66.2f), new Vector3(83.4f, 0f, 66f) }) SmeProp("AmmoBox2", flak, p, Random.Range(0f, 90f));
            // the 8.8 cm Flak on its carriage (travelling order) and a 7.5 cm PaK 40 with its ammunition, by the command tent
            var f88 = PgModel($"{PgVehicles}/Flak88.fbx", g, new Vector3(84f, 0f, 22f), 0f, $"{PgVehicles}/Textures/Flak88", "8.8 cm Flak 36 (on its carriage)");
            if (f88) { var b = WorldBounds(f88); var c = f88.AddComponent<BoxCollider>(); c.center = f88.transform.InverseTransformPoint(b.center); c.size = new Vector3(2.6f, 2.4f, 7f); keepOut.Add(b); pgWorn.Add(new Vector3(84f, 6f, 22f)); }
            var pak40 = PgModel($"{PgVehicles}/Pak40.fbx", g, new Vector3(78f, 0f, -26f), 270f, $"{PgVehicles}/Textures/Pak40", "7.5 cm PaK 40");
            if (pak40)
            {
                var b = WorldBounds(pak40); var c = pak40.AddComponent<BoxCollider>(); c.center = pak40.transform.InverseTransformPoint(b.center); c.size = new Vector3(2f, 1.2f, 5.5f);
                keepOut.Add(b); pgWorn.Add(new Vector3(78f, 5f, -26f));
                PgModel($"{PgProps}/Pak40Ammo.fbx", g, new Vector3(80.5f, 0f, -22.5f), 20f, $"{PgProps}/Textures/Pak40Ammo", "PaK 40 ammunition");
            }
            CampSign(g, new Vector3(88.5f, 0f, 18f), 90f, "8,8 CM FLAK 36");
            // a camouflage net on poles over the near end of the motor pool
            CamoNet(g, new Vector3(GermanParkX, 0f, -30f), new Vector2(16f, 22f), wood);
            // the field kitchen on its Opel (the "Gulaschkanone") and a fire by the tents
            var fk = PgModel($"{PgProps}/FieldKitchenTruck.fbx", g, new Vector3(66f, 0f, -36f), 0f, $"{PgProps}/Textures/FieldKitchenTruck", "Field kitchen (Opel Blitz)");
            if (fk) { SolidBox(fk); keepOut.Add(WorldBounds(fk)); pgWorn.Add(new Vector3(66f, 6f, -36f)); }
            CampFire(g, new Vector3(50f, 0f, -32f));
            // the field bunker, the timber for the next one, fuel drums, stores
            foreach (var (f, p, y) in new[] { ("FieldBunker", new Vector3(122f, 0f, 70f), 270f), ("TimberPile", new Vector3(76f, 0f, 96f), 10f), ("TimberWale", new Vector3(84f, 0f, 98f), 80f),
                                              ("SupplyStack", new Vector3(118f, 0f, -38f), 0f), ("CrateStackA", new Vector3(80f, 0f, -36f), 25f), ("CrateTall", new Vector3(78f, 0f, -40f), 0f),
                                              ("Wheelbarrow", new Vector3(70f, 0f, 89f), 40f) })
                { var o = SmeProp(f, g, p, y); if (o) keepOut.Add(WorldBounds(o)); }
            // (the wheelbarrow and the spade stood on the timber pile's collider: moved clear of it, the spade laid down on the grass)
            SmeProp("TrenchSpade", g, new Vector3(71.6f, 0.03f, 88.2f), 115f, 90f, "Entrenching tool (laid down)", false);
            for (int i = 0; i < 9; i++)
            {
                var o = SmeProp("Barrel", g, new Vector3(112f + (i % 3) * 0.8f, 0f, -44f + (i / 3) * 0.8f), Random.Range(0f, 360f));
                if (o && i == 0) keepOut.Add(new Bounds(new Vector3(112.8f, 1f, -43.2f), new Vector3(3f, 2f, 3f)));
            }
            Watchtower(g, new Vector3(128f, 0f, 108f), wood, keepOut);
            Watchtower(g, new Vector3(42f, 0f, 108f), wood, keepOut);
            // no sign with a name (the units here change); a plain painted board at the gate
            CampSign(g, new Vector3(44.5f, 0f, CampTrackZ - 9.5f), 90f, "FELDLAGER  -  HALT!");
        }

        // ---------------------------------------------------------------- pieces

        /// <summary>
        /// One of Joan's models brought to real size in Blender (Models/Source~/willys_jeep.py, sherman_e8*.py, vickers_tripod.py, tables_scale.py), placed and painted:
        /// each material takes the texture of the same name from texDir (Glass stays glass).
        /// </summary>
        static GameObject PgModel(string fbx, Transform parent, Vector3 pos, float yaw, string texDir, string name = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (!asset) return null;
            var m = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            PrefabUtility.UnpackPrefabInstance(m, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            if (name != null) m.name = name;
            m.transform.position = pos; m.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * asset.transform.localRotation;
            string tag = System.IO.Path.GetFileName(texDir);
            var glass = Mat("PG_Glass", new Color(0.45f, 0.52f, 0.55f), 0.92f, 0.1f);
            foreach (var r in m.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string mn = ms[i] ? ms[i].name.Replace(" (Instance)", "") : "";
                    if (mn.ToLowerInvariant().Contains("glass")) { ms[i] = glass; continue; }
                    string tex = $"{texDir}/{mn}.jpg";
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(tex)) ms[i] = TexMat($"PG_{tag}_{mn}", tex, 0.2f);
                }
                r.sharedMaterials = ms;
            }
            return m;
        }

        const string PgVehicles = Root + "/Models/Vehicles", PgProps = Root + "/Models/Props";

        static Material CanvasMat(string name, Color a, Color b)
        {
            var m = Mat(name, Color.white, 0.05f);
            var tex = MakeNoiseTexture(name + "_Tex", a, b);
            m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f));
            return m;
        }

        /// <summary>The German splinter pattern (Splittertarnmuster 31): tan ground, angular green and brown patches, rain streaks.</summary>
        static Material SplinterMat()
        {
            string path = $"{MatDir}/PG_Splinter_Tex.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!tex)
            {
                const int S = 256;
                tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
                Color tan = new Color(0.56f, 0.51f, 0.38f), green = new Color(0.30f, 0.35f, 0.22f), brown = new Color(0.36f, 0.27f, 0.17f), rain = new Color(0.22f, 0.27f, 0.17f);
                float a = 28f * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        // splinters: noise on a coarse slanted grid, so the patches come out with straight edges
                        float u = x * ca + y * sa, v = -x * sa + y * ca;
                        float qu = Mathf.Floor(u / 22f), qv = Mathf.Floor(v / 9f);
                        float n = Mathf.PerlinNoise(qu * 0.37f + 3f, qv * 0.29f + 7f) + (Mathf.PerlinNoise(x * 0.05f, y * 0.05f) - 0.5f) * 0.25f;
                        Color c = n < 0.42f ? green : n < 0.6f ? tan : brown;
                        if (((int)(v) % 7 == 0) && Mathf.PerlinNoise(u * 0.2f, qv) > 0.45f) c = rain;   // the rain streaks
                        tex.SetPixel(x, y, c);
                    }
                tex.Apply(); tex.wrapMode = TextureWrapMode.Repeat;
                AssetDatabase.CreateAsset(tex, path);
            }
            var m = Mat("PG_Canvas_Splinter", Color.white, 0.05f);
            m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(0.6f, 0.6f));
            return m;
        }

        /// <summary>A tent from a made mesh: canvas, a door flap in front, a pole showing at the top.</summary>
        static GameObject Tent(string name, Transform parent, Mesh mesh, Material canvas, Vector3 pos, float yaw, Material pole, Material door, float scale)
        {
            var t = new GameObject(name);
            t.transform.SetParent(parent); t.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f)); t.transform.localScale = Vector3.one * scale;
            t.AddComponent<MeshFilter>().sharedMesh = mesh;
            t.AddComponent<MeshRenderer>().sharedMaterial = canvas;
            var b = mesh.bounds;
            var col = t.AddComponent<BoxCollider>(); col.center = new Vector3(0f, b.size.y * 0.35f, 0f); col.size = new Vector3(b.size.x * 0.8f, b.size.y * 0.7f, b.size.z * 0.8f);
            // the door: a dark opening on the front (+z)
            Prim(PrimitiveType.Cube, "Door", t.transform, new Vector3(0f, Mathf.Min(0.75f, b.size.y * 0.35f), b.max.z * 0.985f), Vector3.zero,
                 new Vector3(Mathf.Min(0.9f, b.size.x * 0.3f), Mathf.Min(1.5f, b.size.y * 0.7f), 0.02f), door);
            Prim(PrimitiveType.Cylinder, "Pole top", t.transform, new Vector3(0f, b.max.y + 0.12f, 0f), Vector3.zero, new Vector3(0.05f, 0.14f, 0.05f), pole);
            GameObjectUtility.SetStaticEditorFlags(t, StaticEditorFlags.BatchingStatic);
            return t;
        }

        static readonly Dictionary<string, Mesh> madeMeshes = new Dictionary<string, Mesh>();   // made once per build, shared by every tent

        static Mesh SaveMesh(string name, List<Vector3[]> faces)
        {
            if (madeMeshes.TryGetValue(name, out var made) && made) return made;
            string dir = Root + "/Models/Generated";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Root + "/Models", "Generated");
            string path = $"{dir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);   // made fresh on every build
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (var f in faces)
                for (int side = 0; side < 2; side++)   // both sides, so the inside shows through the door
                {
                    int s = v.Count;
                    Vector3 nrm = Vector3.Cross(f[1] - f[0], f[2] - f[0]).normalized;
                    Vector3 ax = Mathf.Abs(nrm.y) > 0.9f ? Vector3.right : Vector3.Cross(Vector3.up, nrm).normalized;
                    Vector3 ay = Vector3.Cross(nrm, ax);
                    foreach (var p in f) { v.Add(p - (side == 1 ? nrm * 0.01f : Vector3.zero)); uv.Add(new Vector2(Vector3.Dot(p, ax), Vector3.Dot(p, ay))); }
                    for (int i = 1; i < f.Length - 1; i++)
                        if (side == 0) { tri.Add(s); tri.Add(s + i); tri.Add(s + i + 1); }
                        else { tri.Add(s); tri.Add(s + i + 1); tri.Add(s + i); }
                }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            AssetDatabase.CreateAsset(m, path);
            madeMeshes[name] = m;
            return m;
        }

        // faces are wound so that Cross(b - a, c - a) points outwards
        static Mesh BellTentMesh()
        {
            const int n = 20; const float r = 2.3f, wall = 0.6f, apex = 3.1f;
            var faces = new List<Vector3[]>();
            Vector3 At(int i, float y) { float a = Mathf.PI * 2f * i / n; return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r); }
            for (int i = 0; i < n; i++)
            {
                faces.Add(new[] { At(i, 0f), At(i, wall), At(i + 1, wall), At(i + 1, 0f) });
                faces.Add(new[] { At(i, wall), new Vector3(0f, apex, 0f), At(i + 1, wall) });
            }
            FixWinding(faces, new Vector3(0f, 1f, 0f));
            return SaveMesh("Tent_Bell", faces);
        }

        static Mesh PyramidTentMesh() => PyramidMesh("Tent_Pyramidal_M1934", 4.9f, 0.9f, 3.4f);
        static Mesh ZeltbahnMesh() => PyramidMesh("Tent_Zeltbahn4", 2.5f, 0f, 1.7f);

        static Mesh PyramidMesh(string name, float size, float wall, float apex)
        {
            float h = size / 2f;
            var c = new[] { new Vector3(-h, 0f, -h), new Vector3(h, 0f, -h), new Vector3(h, 0f, h), new Vector3(-h, 0f, h) };
            var faces = new List<Vector3[]>();
            var top = new Vector3(0f, apex, 0f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = c[i], b = c[(i + 1) % 4];
                if (wall > 0f) faces.Add(new[] { a, a + Vector3.up * wall, b + Vector3.up * wall, b });
                faces.Add(new[] { a + Vector3.up * wall, top, b + Vector3.up * wall });
            }
            FixWinding(faces, new Vector3(0f, apex * 0.3f, 0f));
            return SaveMesh(name, faces);
        }

        /// <summary>A ridge tent or marquee: length along x, door at the front (+z side) is in the long wall.</summary>
        static Mesh RidgeTentMesh(string name, float length, float width, float wall, float ridge)
        {
            float l = length / 2f, w = width / 2f;
            var faces = new List<Vector3[]>
            {
                new[] { new Vector3(-l, 0f, w), new Vector3(l, 0f, w), new Vector3(l, wall, w), new Vector3(-l, wall, w) },     // front wall
                new[] { new Vector3(-l, 0f, -w), new Vector3(-l, wall, -w), new Vector3(l, wall, -w), new Vector3(l, 0f, -w) }, // back wall
                new[] { new Vector3(-l, wall, w), new Vector3(l, wall, w), new Vector3(l, ridge, 0f), new Vector3(-l, ridge, 0f) },
                new[] { new Vector3(-l, wall, -w), new Vector3(-l, ridge, 0f), new Vector3(l, ridge, 0f), new Vector3(l, wall, -w) },
                new[] { new Vector3(l, 0f, -w), new Vector3(l, wall, -w), new Vector3(l, ridge, 0f), new Vector3(l, wall, w), new Vector3(l, 0f, w) },
                new[] { new Vector3(-l, 0f, w), new Vector3(-l, wall, w), new Vector3(-l, ridge, 0f), new Vector3(-l, wall, -w), new Vector3(-l, 0f, -w) },
            };
            FixWinding(faces, new Vector3(0f, wall * 0.5f, 0f));
            return SaveMesh("Tent_" + name.Replace(" ", "_"), faces);
        }

        /// <summary>Turns every face so its normal points away from the inside point.</summary>
        static void FixWinding(List<Vector3[]> faces, Vector3 inside)
        {
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                Vector3 n = Vector3.Cross(f[1] - f[0], f[2] - f[0]);
                Vector3 mid = Vector3.zero; foreach (var p in f) mid += p; mid /= f.Length;
                if (Vector3.Dot(n, mid - inside) < 0f) faces[i] = f.Reverse().ToArray();
            }
        }

        static void Trestle(Transform parent, Vector3 pos, float yaw, Material wood, string model = "Table_Wood")
        {
            var t = new GameObject("Trestle table").transform; t.SetParent(parent); t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var tm = PgModel($"{PgProps}/Tables/{model}.fbx", t, pos, yaw, $"{PgProps}/Textures/Tables", model);
            if (tm) { var b = WorldBounds(tm); var c = t.gameObject.AddComponent<BoxCollider>(); c.center = t.InverseTransformPoint(b.center); c.size = new Vector3(1.5f, 0.76f, 0.7f); }
            else
            {
                Prim(PrimitiveType.Cube, "Top", t, new Vector3(0f, 0.76f, 0f), Vector3.zero, new Vector3(1.8f, 0.05f, 0.75f), wood, true);
                foreach (float x in new[] { -0.75f, 0.75f }) Prim(PrimitiveType.Cube, "Trestle", t, new Vector3(x, 0.37f, 0f), Vector3.zero, new Vector3(0.06f, 0.74f, 0.65f), wood);
            }
            foreach (float x in new[] { -0.5f, 0.5f }) Prim(PrimitiveType.Cube, "Bench", t, new Vector3(x * 0.4f, 0.42f, -0.75f), Vector3.zero, new Vector3(1.6f, 0.05f, 0.25f), wood);
        }

        static void CookHouse(Transform parent, Vector3 pos, Material wood, List<Bounds> keepOut)
        {
            var t = new GameObject("Cookhouse (field kitchen)").transform; t.SetParent(parent); t.position = pos;
            var iron = Mat("PG_Iron_Black", new Color(0.08f, 0.08f, 0.08f), 0.3f, 0.6f);
            var canvas = CanvasMat("PG_Canvas_BellTent", new Color(0.66f, 0.62f, 0.50f), new Color(0.58f, 0.55f, 0.43f));
            foreach (float x in new[] { -2.5f, 2.5f }) foreach (float z in new[] { -2f, 2f })
                Prim(PrimitiveType.Cylinder, "Post", t, new Vector3(x, 1.2f, z), Vector3.zero, new Vector3(0.1f, 1.2f, 0.1f), wood, true);
            Prim(PrimitiveType.Cube, "Fly sheet", t, new Vector3(0f, 2.45f, 0f), new Vector3(4f, 0f, 0f), new Vector3(5.4f, 0.04f, 4.4f), canvas);
            Prim(PrimitiveType.Cube, "Range (stove)", t, new Vector3(0f, 0.45f, -1f), Vector3.zero, new Vector3(2.2f, 0.9f, 0.9f), iron, true);
            Prim(PrimitiveType.Cylinder, "Flue", t, new Vector3(0.8f, 1.9f, -1.2f), Vector3.zero, new Vector3(0.18f, 1.1f, 0.18f), iron);
            foreach (float x in new[] { -0.6f, 0.2f }) Prim(PrimitiveType.Cylinder, "Dixie", t, new Vector3(x, 1.0f, -1f), Vector3.zero, new Vector3(0.45f, 0.12f, 0.45f), iron);
            Trestle(t, pos + new Vector3(0f, 0f, 1.2f), 0f, wood, "Table_Simple");
            SmeProp("Barrel", t, pos + new Vector3(2f, 0f, -1.6f), 0f);
            keepOut.Add(new Bounds(pos + Vector3.up, new Vector3(6f, 3f, 5f))); pgWorn.Add(new Vector3(pos.x, 4f, pos.z));
        }

        static void Flag(Transform parent, Vector3 pos, Texture2D tex, string name, Material wood)
        {
            var t = new GameObject("Flagpole: " + name).transform; t.SetParent(parent); t.position = pos;
            var white = Mat("PG_Paint_White", new Color(0.88f, 0.86f, 0.8f), 0.2f);
            Prim(PrimitiveType.Cylinder, "Pole", t, new Vector3(0f, 4f, 0f), Vector3.zero, new Vector3(0.1f, 4f, 0.1f), white, true);
            Prim(PrimitiveType.Sphere, "Finial", t, new Vector3(0f, 8.05f, 0f), Vector3.zero, Vector3.one * 0.14f, white);
            foreach (float x in new[] { -0.6f, 0.6f }) Prim(PrimitiveType.Cube, "Stone", t, new Vector3(x * 0.6f, 0.08f, 0f), new Vector3(0f, x * 40f, 0f), new Vector3(0.4f, 0.16f, 0.3f), Mat("PG_Stone", new Color(0.6f, 0.58f, 0.54f)));
            var m = Mat("PG_Flag_" + name.Split(' ')[0], Color.white, 0.05f);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Cull", 0f);   // both sides
            m.doubleSidedGI = true;
            m.SetFloat("_Smoothness", 0.04f);
            // the cloth: a grid that waves in the wind (WavingFlag), hoisted just off the pole
            var flag = new GameObject(name);
            flag.transform.SetParent(t, false); flag.transform.localPosition = new Vector3(0.07f, 7.3f, 0f);
            flag.AddComponent<MeshFilter>();
            var fr = flag.AddComponent<MeshRenderer>(); fr.sharedMaterial = m; fr.shadowCastingMode = ShadowCastingMode.TwoSided;
            var wf = flag.AddComponent<WavingFlag>(); wf.width = 1.9f; wf.height = 1.0f;
        }

        static Texture2D FlagTex(string name, int w, int h, System.Func<float, float, Color> paint)
        {
            string path = $"{MatDir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);   // made fresh, so a change to the painting shows
            // printed bunting: a plain weave (a darker thread every 3 pixels both ways), slubs in the yarn, the dye a
            // little uneven and faded by the sun
            w *= 2; h *= 2;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = paint((x + 0.5f) / w, (y + 0.5f) / h);
                    float weave = (x % 3 == 0 ? 0.9f : 1f) * (y % 3 == 0 ? 0.92f : 1f) * ((x / 3 + y / 3) % 2 == 0 ? 1f : 0.97f);
                    float slub = 0.97f + 0.05f * Mathf.PerlinNoise(x * 0.9f, y * 0.013f) - 0.03f * Mathf.PerlinNoise(x * 0.012f, y * 0.8f);
                    float dye = 0.94f + 0.06f * Mathf.PerlinNoise(x * 0.008f + 7f, y * 0.008f + 3f);
                    c *= weave * slub * dye;
                    float g = c.grayscale; c = Color.Lerp(c, new Color(g, g, g), 0.1f);   // faded
                    c.a = 1f;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 8;
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        /// <summary>The Union Flag (1:2), painted in code: blue field, white and red saltires, white-bordered red cross.</summary>
        static Texture2D UnionFlag() => FlagTex("Flag_Union", 512, 256, (u, v) =>
        {
            Color blue = new Color(0.0f, 0.14f, 0.42f), red = new Color(0.78f, 0.06f, 0.13f), white = Color.white;
            float x = (u - 0.5f) * 2f, y = (v - 0.5f) * 2f;          // -1..1 both ways (the flag is 2:1, so x spans twice y's width)
            float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
            if (ax < 1f / 10f || ay < 1f / 5f) return red;            // the red cross (1/5 of the height)
            if (ax < 1f / 6f || ay < 1f / 3f) return white;           // its white border
            // the saltires along the diagonals: broad white, a narrower red inside it
            float d = Mathf.Min(Mathf.Abs(x * 0.5f - y * 0.5f), Mathf.Abs(x * 0.5f + y * 0.5f)) * 0.894f;
            if (d < 0.03f) return red;
            if (d < 0.075f) return white;
            return blue;
        });

        /// <summary>The 48-star flag of 1944: thirteen stripes, a blue canton with six rows of eight stars.</summary>
        static Texture2D StarsAndStripes() => FlagTex("Flag_US48", 570, 300, (u, v) =>
        {
            Color red = new Color(0.70f, 0.10f, 0.17f), white = Color.white, blue = new Color(0.12f, 0.15f, 0.36f);
            float top = 1f - v;
            if (top < 7f / 13f && u < 0.76f / 1.9f)
            {
                // the canton: 8 stars across, 6 down
                float cu = u / (0.76f / 1.9f), cv = top / (7f / 13f);
                float sx = cu * 8f, sy = cv * 6f;
                float fx = sx - Mathf.Floor(sx) - 0.5f, fy = sy - Mathf.Floor(sy) - 0.5f;
                float r = Mathf.Sqrt(fx * fx + fy * fy * 1.6f), a = Mathf.Atan2(fy, fx);
                float star = 0.18f + 0.11f * Mathf.Cos(5f * (a + Mathf.PI / 2f));   // a five-pointed outline
                return r < star * 0.95f ? white : blue;
            }
            return ((int)(top * 13f)) % 2 == 0 ? red : white;
        });

        /// <summary>A sign board on the town-sign posts, painted both sides, the text on the side facing 'yaw' + 90.</summary>
        static void CampSign(Transform parent, Vector3 pos, float yaw, string text)
        {
            var paint = Mat("PG_SignPaint", new Color(0.86f, 0.84f, 0.78f), 0.1f);
            var edge = Mat("PG_SignEdge", new Color(0.12f, 0.11f, 0.1f), 0.1f);
            var sign = SmeProp("TownSign", parent, pos, yaw, 0f, "Sign: " + text);
            Transform st = sign ? sign.transform : parent;
            var rot = Quaternion.Euler(0f, yaw - 90f, 0f);
            foreach (float side in new[] { -1f, 1f })
            {
                var panel = Prim(PrimitiveType.Cube, "Painted face", parent, Vector3.zero, Vector3.zero, new Vector3(0.012f, PlaqueH, PlaqueW), edge);
                panel.transform.SetPositionAndRotation(pos + rot * new Vector3(side * 0.052f, PlaqueY, 0f), rot);
                var coat = Prim(PrimitiveType.Cube, "Paint", parent, Vector3.zero, Vector3.zero, new Vector3(0.004f, PlaqueH - 0.06f, PlaqueW - 0.06f), paint);
                coat.transform.SetPositionAndRotation(pos + rot * new Vector3(side * 0.059f, PlaqueY, 0f), rot);
                panel.transform.SetParent(st, true); coat.transform.SetParent(st, true);
            }
            FitLabel(parent, text, pos + rot * new Vector3(-0.064f, PlaqueY, 0f), PlaqueW - 0.16f, PlaqueH - 0.14f, new Color(0.08f, 0.08f, 0.08f), Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>
        /// A sandbag wall from a to b out of Joan's sandbag-barrier pieces (Models/Props/SandbagWall: 4.6 m long, 1 m
        /// high), stretched a little to fit exactly; the old sandbag line if the piece is missing.
        /// </summary>
        static void SandbagWall(Transform parent, Vector3 a, Vector3 b, float fallbackHeight)
        {
            const float piece = 4.59f;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{PgProps}/SandbagWall.fbx");
            Vector3 d = b - a; d.y = 0f; float len = d.magnitude;
            if (!asset || len < 0.5f) { SandbagLine(parent, a, b, fallbackHeight); return; }
            int n = Mathf.Max(1, Mathf.RoundToInt(len / piece));
            float stretch = len / (n * piece);
            var rot = Quaternion.FromToRotation(Vector3.right, d.normalized);
            var g = new GameObject("Sandbag wall").transform; g.SetParent(parent);
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.Lerp(a, b, (i + 0.5f) / n);
                var w = PgModel($"{PgProps}/SandbagWall.fbx", g, p, 0f, $"{PgProps}/Textures/SandbagWall", "Sandbags");
                w.transform.rotation = rot * w.transform.rotation;
                // stretch along the wall's own length (its long side, whichever local axis that came in as)
                var lb = w.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size;
                var sc = w.transform.localScale;
                if (lb.x >= lb.z) sc.x *= stretch; else sc.z *= stretch;
                w.transform.localScale = sc;
                var cg = new GameObject("Collider"); cg.transform.SetParent(g); cg.transform.SetPositionAndRotation(p + Vector3.up * 0.5f, rot);
                cg.AddComponent<BoxCollider>().size = new Vector3(piece * stretch, 1.0f, 0.75f);
            }
        }

        /// <summary>A striped barrier pole across the road on two trestles.</summary>
        static void Barrier(Transform parent, Vector3 pos, Material wood, float yaw = 0f)
        {
            var t = new GameObject("Barrier").transform; t.SetParent(parent); t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var white = Mat("PG_Paint_White", new Color(0.88f, 0.86f, 0.8f), 0.2f);
            var red = Mat("PG_Paint_Red", new Color(0.62f, 0.1f, 0.08f), 0.2f);
            foreach (float x in new[] { -3.6f, 3.6f }) Prim(PrimitiveType.Cube, "Post", t, new Vector3(x, 0.55f, 0f), Vector3.zero, new Vector3(0.18f, 1.1f, 0.18f), wood, true);
            for (int i = 0; i < 8; i++)
                Prim(PrimitiveType.Cylinder, "Pole", t, new Vector3(-3.15f + i * 0.9f, 1.05f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.11f, 0.45f, 0.11f), i % 2 == 0 ? red : white, i == 0);
            pgWorn.Add(new Vector3(pos.x, 4f, pos.z));
        }

        /// <summary>A sentry post: a ring of sandbags open at the back, a soldier standing his post inside.</summary>
        static void SentryPost(Transform parent, Vector3 pos, float facing, string prefabName, string label, List<Bounds> keepOut)
        {
            var t = new GameObject(label).transform; t.SetParent(parent); t.position = pos;
            var back = Quaternion.Euler(0f, facing, 0f) * Vector3.back;
            SandbagRing(t, pos, 1.5f, 1.0f, back, 1);
            keepOut.Add(new Bounds(pos + Vector3.up, new Vector3(4f, 3f, 4f))); pgWorn.Add(new Vector3(pos.x, 2.5f, pos.z));
            var guid = AssetDatabase.FindAssets($"{prefabName} t:Prefab", new[] { PrefabDir }).FirstOrDefault(gd => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(gd)) == prefabName);
            var pf = guid != null ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)) : null;
            if (!pf) { Debug.LogWarning($"Stan's Game: no prefab {prefabName} for the sentry"); return; }
            var man = (GameObject)PrefabUtility.InstantiatePrefab(pf, t);
            man.name = label;
            man.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, facing, 0f));
            foreach (var ai in man.GetComponentsInChildren<SoldierAI>(true)) ai.enabled = false;   // he stands his post; nobody fights here
            var hp = man.GetComponent<Health>();
            var anim = man.GetComponentInChildren<Animator>(true);
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root + (hp && hp.faction == Faction.German ? "/Animations/Soldier_German.overrideController" : "/Animations/Soldier_Allied.overrideController"));
            if (anim && ctrl) { anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; }
            Label(man.transform, label, pos + Vector3.up * 2.15f, 0.05f, new Color(1f, 0.92f, 0.7f), Quaternion.identity);
        }

        /// <summary>A timber watchtower: four legs, a platform with a breastwork at 4 m, a roof, a ladder.</summary>
        static void Watchtower(Transform parent, Vector3 pos, Material wood, List<Bounds> keepOut)
        {
            // Joan's watchtower with its guard hut (props_batch4.py), walkable inside; else the timber one below
            var wt = PgModel($"{PgProps}/WatchtowerHut.fbx", parent, pos, pos.x < 0f ? 90f : 270f, $"{PgProps}/Textures/WatchtowerHut", "Watchtower and guard hut");
            if (wt)
            {
                foreach (var mf in wt.GetComponentsInChildren<MeshFilter>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                keepOut.Add(WorldBounds(wt)); pgWorn.Add(new Vector3(pos.x, 5f, pos.z));
                return;
            }
            var t = new GameObject("Watchtower").transform; t.SetParent(parent); t.position = pos;
            var dark = Mat("PG_Wood_Dark", new Color(0.28f, 0.2f, 0.13f), 0.15f);
            const float s = 1.4f, deck = 4f;
            foreach (float x in new[] { -s, s }) foreach (float z in new[] { -s, s })
            {
                Prim(PrimitiveType.Cube, "Leg", t, new Vector3(x, (deck + 2.2f) / 2f, z), Vector3.zero, new Vector3(0.2f, deck + 2.2f, 0.2f), dark, true);
            }
            foreach (float y in new[] { 1.3f, 2.7f })   // cross braces
                foreach (float side in new[] { -s, s })
                {
                    Prim(PrimitiveType.Cube, "Brace", t, new Vector3(side, y, 0f), new Vector3(35f, 0f, 0f), new Vector3(0.08f, 0.12f, 3.3f), wood);
                    Prim(PrimitiveType.Cube, "Brace", t, new Vector3(0f, y, side), new Vector3(0f, 0f, 35f), new Vector3(3.3f, 0.12f, 0.08f), wood);
                }
            Prim(PrimitiveType.Cube, "Platform", t, new Vector3(0f, deck, 0f), Vector3.zero, new Vector3(3.4f, 0.12f, 3.4f), wood, true);
            foreach (var (p, sc) in new[] { (new Vector3(0f, deck + 0.55f, s + 0.1f), new Vector3(3.4f, 1f, 0.08f)), (new Vector3(0f, deck + 0.55f, -s - 0.1f), new Vector3(3.4f, 1f, 0.08f)),
                                            (new Vector3(s + 0.1f, deck + 0.55f, 0f), new Vector3(0.08f, 1f, 3.4f)), (new Vector3(-s - 0.1f, deck + 0.55f, 0.6f), new Vector3(0.08f, 1f, 2.2f)) })
                Prim(PrimitiveType.Cube, "Breastwork", t, p, Vector3.zero, sc, wood, true);
            Prim(PrimitiveType.Cube, "Roof", t, new Vector3(0f, deck + 2.3f, 0f), new Vector3(0f, 0f, 8f), new Vector3(3.9f, 0.1f, 3.9f), dark);
            SmeProp("Ladder", t, pos + new Vector3(-s - 0.9f, 0f, -0.9f), 90f, 14f, "Ladder", false);   // leaning in to the platform (printscreen 31)
            keepOut.Add(new Bounds(pos + Vector3.up * 3f, new Vector3(5f, 7f, 5f))); pgWorn.Add(new Vector3(pos.x, 3f, pos.z));
        }

        /// <summary>A camouflage net over the motor pool on poles.</summary>
        static void CamoNet(Transform parent, Vector3 centre, Vector2 size, Material wood)
        {
            var t = new GameObject("Camouflage net").transform; t.SetParent(parent); t.position = centre;
            var net = Mat("PG_CamoNet", Color.white, 0.02f);
            net.SetTexture("_BaseMap", MakeNoiseTexture("PG_CamoNet_Tex", new Color(0.20f, 0.24f, 0.13f), new Color(0.36f, 0.33f, 0.22f)));
            net.SetTextureScale("_BaseMap", new Vector2(3f, 3f));
            foreach (float x in new[] { -size.x / 2f, 0f, size.x / 2f }) foreach (float z in new[] { -size.y / 2f, size.y / 2f })
                Prim(PrimitiveType.Cylinder, "Net pole", t, new Vector3(x, 1.9f, z), Vector3.zero, new Vector3(0.1f, 1.9f, 0.1f), wood, true);
            Prim(PrimitiveType.Cube, "Net", t, new Vector3(0f, 3.85f, 0f), Vector3.zero, new Vector3(size.x + 1f, 0.04f, size.y + 1f), net);
            // the edges hang down towards the ground
            foreach (float side in new[] { -1f, 1f })
                Prim(PrimitiveType.Cube, "Net (hanging)", t, new Vector3(side * (size.x / 2f + 1.2f), 2.6f, 0f), new Vector3(0f, 0f, side * -62f), new Vector3(0.04f, 2.9f, size.y + 1f), net);
        }

        /// <summary>
        /// A Willys MB jeep (mock-up until there is a model), drivable like the Kübelwagen: walk up, E. Open tub with a
        /// floor, two front seats and a rear bench where TruckController seats four; flat bonnet with the white star;
        /// spare wheel and jerrycan on the back. The wheels are named and pivot on their hubs so they roll and steer.
        /// </summary>
        static GameObject Jeep(Transform parent, string name, Vector3 pos, float yaw)
        {
            var go = new GameObject(name); var t = go.transform;
            t.SetParent(parent); t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var model = PgModel($"{PgVehicles}/WillysJeep.fbx", t, pos, yaw, $"{PgVehicles}/Textures/WillysJeep", "Model");
            if (model)
            {
                // Joan's Willys MB (willy-jeep): wheels Wheel_FL/FR/RL/RR on their hubs; the bonnet must lead, down +Z
                var mw = model.GetComponentsInChildren<Transform>(true).Where(c => c.name.StartsWith("Wheel_")).ToList();
                float fz = 0f, rz = 0f; int nf = 0, nr = 0;
                foreach (var w in mw) { float z = t.InverseTransformPoint(w.position).z; if (w.name.StartsWith("Wheel_F")) { fz += z; nf++; } else { rz += z; nr++; } }
                if (nf > 0 && nr > 0 && fz / nf < rz / nr) model.transform.rotation = Quaternion.AngleAxis(180f, t.up) * model.transform.rotation;
                return MakeJeepDrivable(go, mw);
            }
            var od = Mat("PG_Jeep_OD", new Color(0.29f, 0.30f, 0.19f), 0.25f, 0.2f);
            var tyre = Mat("PG_Tyre", new Color(0.06f, 0.06f, 0.06f), 0.1f);
            var seat = Mat("PG_Jeep_Seat", new Color(0.2f, 0.19f, 0.13f), 0.1f);
            var white = Mat("PG_Paint_White", new Color(0.88f, 0.86f, 0.8f), 0.2f);
            var glass = Mat("PG_Jeep_Glass", new Color(0.55f, 0.62f, 0.65f), 0.9f);
            // the open tub: floor, sides, back, the dash (front seats at z 0.2, the rear bench at z -0.7, as TruckController seats a car)
            Prim(PrimitiveType.Cube, "Floor", t, new Vector3(0f, 0.42f, -0.4f), Vector3.zero, new Vector3(1.38f, 0.05f, 1.9f), od);
            foreach (float x in new[] { -0.69f, 0.69f })
            {
                Prim(PrimitiveType.Cube, "Side", t, new Vector3(x, 0.7f, -0.4f), Vector3.zero, new Vector3(0.04f, 0.56f, 1.9f), od);
                Prim(PrimitiveType.Cube, "Wing", t, new Vector3(x * 0.96f, 0.78f, 1.08f), Vector3.zero, new Vector3(0.3f, 0.05f, 0.9f), od);
            }
            Prim(PrimitiveType.Cube, "Back panel", t, new Vector3(0f, 0.7f, -1.35f), Vector3.zero, new Vector3(1.38f, 0.56f, 0.04f), od);
            Prim(PrimitiveType.Cube, "Dash", t, new Vector3(0f, 0.75f, 0.53f), Vector3.zero, new Vector3(1.38f, 0.62f, 0.06f), od);
            Prim(PrimitiveType.Cube, "Bonnet", t, new Vector3(0f, 0.85f, 1.05f), Vector3.zero, new Vector3(1.05f, 0.36f, 1.0f), od);
            Prim(PrimitiveType.Cube, "Grille", t, new Vector3(0f, 0.75f, 1.57f), Vector3.zero, new Vector3(1.0f, 0.56f, 0.05f), od);
            foreach (float x in new[] { -0.32f, 0.32f })
                Prim(PrimitiveType.Sphere, "Headlamp", t, new Vector3(x, 0.92f, 1.6f), Vector3.zero, new Vector3(0.16f, 0.16f, 0.06f), glass);
            Prim(PrimitiveType.Cube, "Star (white)", t, new Vector3(0f, 1.035f, 1.05f), new Vector3(0f, 45f, 0f), new Vector3(0.3f, 0.005f, 0.3f), white);
            Prim(PrimitiveType.Cube, "Star (white)", t, new Vector3(0f, 1.036f, 1.05f), Vector3.zero, new Vector3(0.3f, 0.005f, 0.3f), white);
            Prim(PrimitiveType.Cube, "Windscreen frame", t, new Vector3(0f, 1.27f, 0.56f), new Vector3(-6f, 0f, 0f), new Vector3(1.36f, 0.44f, 0.04f), od);
            Prim(PrimitiveType.Cube, "Windscreen", t, new Vector3(0f, 1.27f, 0.565f), new Vector3(-6f, 0f, 0f), new Vector3(1.24f, 0.34f, 0.03f), glass);
            foreach (float x in new[] { -0.33f, 0.33f })
            {
                Prim(PrimitiveType.Cube, "Front seat", t, new Vector3(x, 0.55f, 0.2f), Vector3.zero, new Vector3(0.46f, 0.12f, 0.44f), seat);
                Prim(PrimitiveType.Cube, "Front seat back", t, new Vector3(x, 0.82f, -0.04f), new Vector3(-10f, 0f, 0f), new Vector3(0.46f, 0.45f, 0.07f), seat);
            }
            Prim(PrimitiveType.Cube, "Rear bench", t, new Vector3(0f, 0.6f, -0.72f), Vector3.zero, new Vector3(1.2f, 0.12f, 0.44f), seat);
            Prim(PrimitiveType.Cube, "Rear bench back", t, new Vector3(0f, 0.88f, -0.98f), new Vector3(-10f, 0f, 0f), new Vector3(1.2f, 0.45f, 0.07f), seat);
            Prim(PrimitiveType.Cylinder, "Steering wheel", t, new Vector3(-0.33f, 1.05f, 0.38f), new Vector3(-60f, 0f, 0f), new Vector3(0.36f, 0.01f, 0.36f), tyre);
            Prim(PrimitiveType.Cylinder, "Spare wheel", t, new Vector3(0.3f, 0.82f, -1.45f), new Vector3(90f, 0f, 0f), new Vector3(0.68f, 0.1f, 0.68f), tyre);
            Prim(PrimitiveType.Cube, "Jerrycan", t, new Vector3(-0.35f, 0.82f, -1.47f), Vector3.zero, new Vector3(0.35f, 0.47f, 0.16f), od);
            // the wheels: an empty hub transform each (TruckController turns and spins these), the tyre inside it
            var wheels = new List<Transform>();
            foreach (var (wn, x, z) in new[] { ("Wheel_FL", -0.62f, 1.15f), ("Wheel_FR", 0.62f, 1.15f), ("Wheel_RL", -0.62f, -0.85f), ("Wheel_RR", 0.62f, -0.85f) })
            {
                var hub = new GameObject(wn).transform; hub.SetParent(t, false); hub.localPosition = new Vector3(x, 0.36f, z);
                Prim(PrimitiveType.Cylinder, "Tyre", hub, Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.72f, 0.11f, 0.72f), tyre);
                Prim(PrimitiveType.Cylinder, "Hub cap", hub, new Vector3(Mathf.Sign(x) * 0.115f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.34f, 0.01f, 0.34f), od);
                wheels.Add(hub);
            }
            return MakeJeepDrivable(go, wheels);
        }

        /// <summary>
        /// Joan's Bedford OX 30-cwt lorry (Models/Source~/bedford_oxd.py, from bedford_oxa_mesh.stl): BedfordOX (empty bed),
        /// BedfordOX_Loaded (ammunition boxes, sacks, jerrycans) or BedfordOX_Gun (the guns on the bed and the cab roof).
        /// Drivable like the Opel: driver on the right, benches in the back. 72 hp, 40 mph (64 km/h).
        /// </summary>
        static GameObject Bedford(Transform parent, string name, Vector3 pos, float yaw, string file)
        {
            var go = new GameObject(name); var t = go.transform;
            t.SetParent(parent); t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var model = PgModel($"{PgVehicles}/{file}.fbx", t, pos, yaw, $"{PgVehicles}/Textures/BedfordOX", "Model");
            if (!model) { Object.DestroyImmediate(go); return null; }
            var mw = model.GetComponentsInChildren<Transform>(true).Where(c => c.name.StartsWith("Wheel_")).ToList();
            float fz = 0f, rz = 0f; int nf = 0, nr = 0;
            foreach (var w in mw) { float z = t.InverseTransformPoint(w.position).z; if (w.name.StartsWith("Wheel_F")) { fz += z; nf++; } else { rz += z; nr++; } }
            if (nf > 0 && nr > 0 && fz / nf < rz / nr) model.transform.rotation = Quaternion.AngleAxis(180f, t.up) * model.transform.rotation;
            var b = WorldBounds(model);
            var col = go.AddComponent<BoxCollider>();
            col.center = t.InverseTransformPoint(b.center) + new Vector3(0f, 0.15f, 0f);
            var sz = t.InverseTransformVector(b.size); col.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y) - 0.3f, Mathf.Abs(sz.z));
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true;
            var tc = go.AddComponent<TruckController>();
            tc.displayName = "Bedford OX lorry";
            tc.wheels = mw.ToArray(); tc.wheelRadius = 0.51f;
            tc.maxForwardSpeed = 17.9f; tc.maxReverseSpeed = 3.5f; tc.acceleration = 2.0f;   // 72 hp, 64 km/h
            tc.layout = TruckController.SeatLayout.Lorry; tc.rightHandDrive = true; tc.benchHeight = 1.25f;   // the bed floor is at 0.81 m
            NetScenery(go, false);
#if STANS_NET
            if (!go.GetComponent<NetTruck>()) go.AddComponent<NetTruck>();
#endif
            return go;
        }

        static GameObject MakeJeepDrivable(GameObject go, List<Transform> wheels)
        {
            var col = go.AddComponent<BoxCollider>(); col.center = new Vector3(0f, 0.75f, 0.1f); col.size = new Vector3(1.55f, 1.2f, 3.3f);
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true;
            var tc = go.AddComponent<TruckController>();
            tc.displayName = "Willys jeep"; tc.engineSound = "car_engine";
            tc.wheels = wheels.ToArray(); tc.wheelRadius = 0.36f;
            tc.maxForwardSpeed = 25f; tc.maxReverseSpeed = 5f; tc.acceleration = 3.8f; tc.maxSteer = 36f; tc.stepHeight = 0.4f;   // Willys MB: 60 hp for 1.1 t, ~90 km/h
            tc.cameraDistance = 7f; tc.cameraHeight = 2f;
            tc.layout = TruckController.SeatLayout.Car;   // driver, front passenger, two behind
            tc.frontSeatBack = 1.45f; tc.rearSeatBack = 2.35f; tc.seatRaise = 0.1f;   // hips on the cushions (measured on the model), feet on the floor (printscreens 29, 32)   // the Willys' seats: just ahead of the middle, and over the rear axle
            NetScenery(go, false);
#if STANS_NET
            if (!go.GetComponent<NetTruck>()) go.AddComponent<NetTruck>();
#endif
            return go;
        }
    }
}
