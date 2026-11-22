using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Build Sainte-Mère-Église Level.
    /// Chapter 2: the night drop of the 82nd Airborne on Sainte-Mère-Église, 6 June 1944 (Mission Boston).
    /// Lives in the PegasusLevelBuilder partial class so it shares the helpers (materials, houses, trees, soldiers, weapons).
    ///
    /// Real-world scale (1 unit = 1 m), flat ground at y = 0 (the Plain of the Cotentin is flat; real LiDAR can come later).
    /// World axes: +Z = north, +X = east. The church of Notre-Dame-de-l'Assomption stands at the origin, nave east-west,
    /// the tower over the crossing (Models/Town/ChurchSME.fbx; the old blockout had it at the west end). The N13 (Carentan - Cherbourg) runs north-south along the east side of the square; the
    /// town hall stands on it. Distances are compressed: the 3/505 assembly area and drop zone O are really 1-2 km out.
    /// Everything is a primitive blockout to swap for real models later.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const float SmeSize = 1024f;
        // The real street plan, from the IGN LiDAR (Terrain/SainteMereHeights.json): the game frame is turned 20 degrees
        // from north so the church lies along X (origin = the church, +X = bearing 70, +Z = bearing 340, up the
        // rue General de Gaulle towards Cherbourg). The old N13 runs along the west side of the place; the church stands
        // in its churchyard in the place; the roads to La Fiere (west), Sainte-Marie-du-Mont (east) and Beuzeville
        // (north) leave from it.
        const float SmeN13X = -92f;                                          // rue General de Gaulle (the N13)
        static readonly Vector3 SmeTownHall = new Vector3(-80f, 0f, -95f);   // on the N13, east side, south of the place
        static readonly Vector3 SmeFireHouse = new Vector3(-30f, 0f, -71f);  // Madame Pommier's house (south side of the place, approximate)
        static readonly Vector3 SmeRally = new Vector3(-235f, 0f, -15f);     // orchard west of the town (3/505 gathers)
        static readonly Vector3 SmePump = new Vector3(-50f, 0f, -35f);       // the pump the bucket chain used
        static readonly Rect SmeSquare = new Rect(-88f, -60f, 121f, 80f);    // x, z, width, depth of the open place round the church
        static readonly Vector3 SmeTower = new Vector3(-19f, 0f, 0f);
        const float SmeTowerHeight = 30f, SmeTowerSide = 8.5f;

        static readonly (Vector3 a, Vector3 b, float w)[] SmeRoads =
        {
            (new Vector3(SmeN13X, 0f, -512f), new Vector3(SmeN13X, 0f, 512f), 9f),           // rue General de Gaulle (N13): Carentan - Cherbourg
            (new Vector3(SmeN13X - 4f, 0f, 46f), new Vector3(-512f, 0f, 46f), 6f),           // road to La Fiere and Cauquigny
            (new Vector3(-88f, 0f, 26f), new Vector3(33f, 0f, 26f), 6f),                     // street along the north of the churchyard
            (new Vector3(33f, 0f, 24f), new Vector3(110f, 0f, 40f), 6f),                     // road to Sainte-Marie-du-Mont
            (new Vector3(110f, 0f, 40f), new Vector3(330f, 0f, 95f), 6f),
            (new Vector3(330f, 0f, 95f), new Vector3(512f, 0f, 140f), 6f),
            (new Vector3(33f, 0f, 24f), new Vector3(78f, 0f, 70f), 5.5f),                    // road north (Beuzeville-au-Plain)
            (new Vector3(78f, 0f, 70f), new Vector3(86f, 0f, 150f), 5.5f),
            (new Vector3(86f, 0f, 150f), new Vector3(100f, 0f, 300f), 5.5f),
            (new Vector3(100f, 0f, 300f), new Vector3(128f, 0f, 512f), 5.5f),
            (new Vector3(SmeRally.x, 0f, 46f), new Vector3(SmeRally.x, 0f, SmeRally.z + 10f), 4f),   // lane to the orchard
        };

        [MenuItem("Stan's Game/Build Sainte-Mère-Église Level", priority = 2)]
        public static void BuildSainteMere()
        {
            if (!EditorUtility.DisplayDialog("Build Sainte-Mère-Église level",
                "Creates a new scene Assets/StansGame/Scenes/SainteMereEglise.unity (overwrites it if it exists). Continue?", "Build", "Cancel"))
                return;

            EnsureFolders();
            PropTextureFixer.Fix();
            mats.Clear();
            pureGroups.Clear();
            real = null; bridgeRef = null; extraRoads = null;
            Random.InitState(1944);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("SainteMereEglise_1944").transform;
            var env = Group("Environment", root);
            var town = Group("Town", env);
            var garrison = Group("German garrison (night, in the square)", root);
            var defenders04 = Group("German defenders (04:00)", root);
            var krause = Group("3rd Battalion 505th PIR (Krause)", root);
            var waveSouth = Group("Counter-attack: Carentan road", root);
            var waveNorth = Group("Counter-attack: from Neuville (N13)", root);
            var air = Group("Aircraft", root);
            var actors = Group("Actors", root);

            BuildLighting(env, "SME", 181.7f + 20f, 22.6f);   // (azimuth in the game frame, turned 20 deg from north)   // 01:30 BDST: the moon due south, 23 deg up
            // a bright moon behind thin cloud that night; enough to read the town from the air
            if (RenderSettings.sun) { RenderSettings.sun.intensity = 0.6f; }
            RenderSettings.ambientLight = new Color(0.10f, 0.11f, 0.16f);
            RenderSettings.fogDensity = 0.004f;
            var cam = Camera.main;
            if (cam) { cam.transform.position = new Vector3(70f, 18f, -95f); cam.transform.LookAt(new Vector3(-10f, 12f, 0f)); }
            SmeTerrain(env);
            SmeChurch(town);
            SmeSquareAndHouses(town);
            var townHall = SmeTownHallAndFlag(town, out var flag);
            SmeFire(town);
            SmeDownloadProps(town, env);
            SmeCottages(town);
            // (shop signs - SmeShopSigns - left out for now: Joan has the real locations of those buildings)
            SmeHedgerows(env);
            SmeOrchard(env);
            SmeFlak(env);

            // kit
            millsBomb = AssetDatabase.LoadAssetAtPath<Shell>($"{PrefabDir}/Grenade_Mills.prefab");
            stickGrenade = AssetDatabase.LoadAssetAtPath<Shell>($"{PrefabDir}/Grenade_Stielhandgranate.prefab");
            if (!millsBomb || !stickGrenade) BuildGrenadePrefabs();
            smokeNo77 = AssetDatabase.LoadAssetAtPath<Shell>($"{PrefabDir}/Smoke_No77.prefab");
            smokeNb39 = AssetDatabase.LoadAssetAtPath<Shell>($"{PrefabDir}/Smoke_Nb39.prefab");
            smokeM8 = AssetDatabase.LoadAssetAtPath<Shell>($"{PrefabDir}/Smoke_ANM8.prefab");
            if (!smokeNo77 || !smokeNb39 || !smokeM8) BuildSmokePrefabs();
            var mk2 = BuildMk2Grenade();
            var usRifle = BuildUsPrefab("US_Paratrooper_Rifleman", "M1 Garand rifle", 70f, 120f, false, 8);
            var usSmg = BuildUsPrefab("US_Paratrooper_Thompson", "Thompson M1A1", 34f, 700f, true, 30);
            var usCarbine = BuildUsPrefab("US_Paratrooper_Carbine", "M1A1 carbine", 45f, 150f, false, 15);

            var plan = SmeBattlePlan();   // who attacks and who defends, and where (Battles/SainteMereEglise.asset)
            var player = BuildUsPlayer(actors, mk2);
            var playerChute = player.gameObject.AddComponent<ParachuteDrop>();
            var canopy = SmeCanopyPrefab();
            playerChute.canopyPrefab = canopy;

            // the aircraft: the player's C-47 with his stick, and the rest of the serial flying over
            var plane = SmeC47("C-47 (F Company, 2/505 - player's stick)", air);
            var flight = plane.GetComponent<C47Flight>();
            flight.start = new Vector3(-720f, 150f, 14f); flight.end = new Vector3(720f, 150f, -30f);
            flight.greenLightAt = 0.36f;
            var stick = plan.Find("stick");
            flight.playerSlot = Mathf.Max(0, stick.units.FindIndex(u => u.kind == "Player"));
            for (int i = 0; i < stick.units.Count; i++)
            {
                var u = stick.units[i];
                if (u.kind == "Player") { flight.stick.Add(playerChute); continue; }
                var man = (GameObject)PrefabUtility.InstantiatePrefab(UsPrefabFor(u.kind, usRifle, usSmg, usCarbine), actors);
                man.name = u.name;
                man.transform.position = flight.start;
                var chute = man.AddComponent<ParachuteDrop>();
                chute.aiLandingPoint = u.position;
                chute.canopyPrefab = canopy;
                var ai = man.GetComponent<SoldierAI>(); if (ai) { ai.startAlert = stick.awake; ai.skill = stick.skill; }
                TagUnit(man, plan, stick);
                flight.stick.Add(chute);
            }
            var serial = new List<C47Flight>();
            for (int k = 0; k < 5; k++)
            {
                var other = SmeC47($"C-47 (serial, {k + 1})", air).GetComponent<C47Flight>();
                float off = (k - 2) * 90f + Random.Range(-20f, 20f);
                other.start = new Vector3(-760f - k * 40f, 170f + Random.Range(0f, 60f), off + 20f);
                other.end = new Vector3(760f, other.start.y, off - 30f);
                serial.Add(other);
            }

            // Steele and Russell's stick-mate on the church tower: Steele hung up on the north side, away from the square
            SmeSteele(town);

            // the Germans and their civilians in the square during the drop
            SmeNightGarrison(garrison, plan);
            SmeDefenders04(defenders04, plan);
            SpawnAmericans(plan, "krause", krause, usRifle, usSmg, usCarbine);
            SpawnGermans(plan, "counter-south", waveSouth);
            SpawnGermans(plan, "counter-north", waveNorth);
            SmeStugWreck(waveNorth);   // the StuG III knocked out on the Neuville road (photos 14, 17)
            SmeAmbientLife(root, town, garrison, actors, air);   // colour grade, patrols and a dog, lorries, C-47s, windows (SmeAmbient.cs)

            // markers
            Transform Mark(string n, Vector3 p) { var t = new GameObject(n).transform; t.SetParent(root); p.y = GroundAt(p); t.position = p; return t; }
            var square = Mark("Objective: church square", new Vector3(-30f, 0f, -30f));
            var rally = Mark("Objective: 3/505 assembly (orchard)", SmeRally);
            var hallMark = Mark("Objective: town hall (flag)", SmeTownHall + new Vector3(-9f, 0f, 0f));
            hallMark.rotation = Quaternion.Euler(0f, -90f, 0f);
            var southRoad = Mark("Hold: Carentan road", new Vector3(SmeN13X, 0f, -150f));

            var mission = new GameObject("SainteMereMission").AddComponent<SainteMereMission>();
            mission.transform.SetParent(root);
            mission.player = player; mission.playerChute = playerChute; mission.playerPlane = flight;
            mission.serial = serial.ToArray();
            mission.squareCentre = square; mission.rallyPoint = rally; mission.townHall = hallMark; mission.flag = flag;
            mission.nightGarrison = garrison.gameObject; mission.townDefenders = defenders04.gameObject; mission.krauseBattalion = krause.gameObject;
            mission.krauseTargets = new[] { square, hallMark, southRoad };
            mission.counterAttacks = new[] { waveSouth.gameObject, waveNorth.gameObject };
            Transform Objective(string id) { var g = plan.Find(id); var t = g != null && !string.IsNullOrEmpty(g.objective) ? root.Find(g.objective) : null; return t ? t : square; }
            foreach (var ai in waveSouth.GetComponentsInChildren<SoldierAI>(true)) ai.advanceTarget = Objective("counter-south");
            foreach (var ai in waveNorth.GetComponentsInChildren<SoldierAI>(true)) ai.advanceTarget = Objective("counter-north");
            // the two commanders, above the men's own AI (BattleCommander; how they fight is on the battle sheet)
            foreach (bool att in new[] { false, true })
            {
                var bc = new GameObject(att ? "Commander (attacker)" : "Commander (defender)").AddComponent<BattleCommander>();
                bc.transform.SetParent(root); bc.plan = plan; bc.attacker = att;
            }
            SmeNetworking(mission, mk2, canopy, garrison, defenders04, krause, waveSouth, waveNorth);   // co-op (STANS_NET)

            if (smeRaw != null) SmeGroundEverything(root);
            string path = SceneDir + "/SainteMereEglise.unity";
            EditorSceneManager.SaveScene(scene, path);
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == path)) list.Add(new EditorBuildSettingsScene(path, true));   // after Pegasus Bridge
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("Stan's Game: Sainte-Mère-Église level built at " + path);
            BattleValidator.Validate(false);   // anyone inside a wall, without a weapon, a patrol through a house...
        }

        // ---------------------------------------------------------------- ground

        static float SmeRoadDistance(float x, float z, out float width)
        {
            float best = float.MaxValue; width = 0f;
            foreach (var (a, b, w) in SmeRoads)
            {
                var p = new Vector2(x, z); var A = new Vector2(a.x, a.z); var B = new Vector2(b.x, b.z);
                var ab = B - A; float t = Mathf.Clamp01(Vector2.Dot(p - A, ab) / ab.sqrMagnitude);
                float d = Vector2.Distance(p, A + ab * t);
                if (d - w / 2f < best) { best = d - w / 2f; width = w; }
            }
            return best;   // < 0: on the road
        }

        static bool SmeInTown(float x, float z) => x > -160f && x < 140f && z > -285f && z < 285f;

        /// <summary>0 inside the town, rising to 1 forty metres outside it (how much the fields may roll).</summary>
        static float SmeOutOfTown(float x, float z)
        {
            float dx = Mathf.Max(-150f - x, 0f, x - 170f), dz = Mathf.Max(-180f - z, 0f, z - 160f);
            return Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / 40f);
        }

        static void SmeTerrain(Transform env)
        {
            const int amRes = 512;
            int res = 513; float baseY = -1f, height = 10f;
            float[,] h;
            if (LoadSmeHeights())
            {
                // the real ground (IGN LiDAR HD, post-war earthworks taken out): see Terrain/SainteMereHeights.json
                res = smeMeta.resolution; baseY = smeMeta.baseY; height = smeMeta.height;
                h = new float[res, res];
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++) h[z, x] = smeRaw[z * res + x] / 65535f;
            }
            else
            {
                h = new float[res, res];
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                    {
                        float wx = x / (float)(res - 1) * SmeSize - SmeSize / 2f, wz = z / (float)(res - 1) * SmeSize - SmeSize / 2f;
                        float roll = (Mathf.PerlinNoise(wx * 0.006f + 3f, wz * 0.006f + 7f) - 0.5f) * 1.2f;
                        float town = SmeOutOfTown(wx, wz);
                        float road = Mathf.Clamp01(SmeRoadDistance(wx, wz, out _) / 12f);
                        h[z, x] = (1f + roll * town * road) / 10f;   // terrain base is y = -1, so 0.1 = y 0
                    }
            }
            var data = new TerrainData { heightmapResolution = res, size = new Vector3(SmeSize, height, SmeSize) };
            AssetDatabase.DeleteAsset(SceneDir + "/SainteMereTerrain.asset");
            AssetDatabase.CreateAsset(data, SceneDir + "/SainteMereTerrain.asset");
            data.SetHeights(0, 0, h);

            var layers = GroundLayers();
            if (layers == null)
            {
                var grass = new TerrainLayer { diffuseTexture = MakeNoiseTexture("SME_Grass", new Color(0.20f, 0.27f, 0.13f), new Color(0.26f, 0.33f, 0.16f)), tileSize = new Vector2(6f, 6f) };
                var road = new TerrainLayer { diffuseTexture = MakeNoiseTexture("SME_Road", new Color(0.36f, 0.34f, 0.30f), new Color(0.30f, 0.28f, 0.25f)), tileSize = new Vector2(4f, 4f) };
                AssetDatabase.DeleteAsset($"{MatDir}/SME_Grass.terrainlayer"); AssetDatabase.CreateAsset(grass, $"{MatDir}/SME_Grass.terrainlayer");
                AssetDatabase.DeleteAsset($"{MatDir}/SME_Road.terrainlayer"); AssetDatabase.CreateAsset(road, $"{MatDir}/SME_Road.terrainlayer");
                layers = new[] { grass, grass, road, road, road, road };
            }
            data.terrainLayers = layers;
            data.alphamapResolution = amRes;
            int L = layers.Length;
            var am = new float[amRes, amRes, L];
            for (int z = 0; z < amRes; z++)
                for (int x = 0; x < amRes; x++)
                {
                    float wx = x / (float)(amRes - 1) * SmeSize - SmeSize / 2f, wz = z / (float)(amRes - 1) * SmeSize - SmeSize / 2f;
                    float rd = SmeRoadDistance(wx, wz, out _);
                    bool square = SmeSquare.Contains(new Vector2(wx, wz));
                    int idx;
                    if (rd < 0f) idx = 3;                                                  // gravel / tarmac
                    else if (square) idx = Mathf.Abs(wx) < 26f && Mathf.Abs(wz) < 9f ? 1 : 5;   // grass round the church, trampled square
                    else if (rd < 1.5f) idx = 2;                                           // verges
                    else idx = Mathf.PerlinNoise(wx * 0.03f, wz * 0.03f) > 0.62f ? 1 : 0;   // pasture with greener patches
                    am[z, x, Mathf.Min(idx, L - 1)] = 1f;
                }
            data.SetAlphamaps(0, 0, am);
            // creating the layer textures makes Unity reload the TerrainData from disk, where it was saved before the
            // heights were set (this is what left the old ground flat at y = -1): write the heights again and save
            data.SetHeights(0, 0, h);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            var tgo = Terrain.CreateTerrainGameObject(data);
            tgo.name = "Terrain (Plain of the Cotentin)";
            tgo.transform.SetParent(env);
            tgo.transform.position = new Vector3(-SmeSize / 2f, baseY, -SmeSize / 2f);
            terrainRef = tgo.GetComponent<Terrain>();
        }

        /// <summary>
        /// Set the church down on the real ground (its foot on the lowest ground round it, so it never stands on air),
        /// and wall its churchyard: a low granite wall all round, a gate to the place on the south and one by the tower
        /// door, sections that would run into the church left out (photos 07, 13).
        /// </summary>
        static void SmeSettleChurch(Transform town)
        {
            var church = town.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Church Notre-Dame"));
            if (!church || smeRaw == null) return;
            var b = UsKitImporter.WorldBounds(church.gameObject);
            float low = float.MaxValue;
            for (float x = b.min.x - 2f; x <= b.max.x + 2f; x += 2f)
                for (float z = b.min.z - 2f; z <= b.max.z + 2f; z += 2f) low = Mathf.Min(low, GroundAt(new Vector3(x, 0f, z)));
            float dy = low - 0.05f - church.position.y;
            church.position += Vector3.up * dy;
            smeTowerTop.y += dy; smeSteelePinnacle.y += dy;
            Physics.SyncTransforms();

            var wallMat = Mat("SME_Church_Stone", new Color(0.55f, 0.52f, 0.46f));
            var g = Group("Churchyard wall", town);
            // on the line of the low wall the church model already has on its west, north and part of its south side
            // (x -30.9, z -11 / +10.9): only the missing stretches are added
            float x0 = -30.9f, x1 = b.max.x + 2.5f, z0 = -11f, z1 = 10.9f;
            var tower = church.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Church tower");
            float towerZ = tower ? tower.position.z : 0f;
            void Side(Vector3 a, Vector3 c, float gateAt)
            {
                var d = c - a; float len = d.magnitude; d /= len;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 90f;
                for (float s = 0f; s < len; s += 3f)
                {
                    float l = Mathf.Min(3f, len - s); if (l < 0.5f) break;
                    var p = a + d * (s + l / 2f);
                    if (gateAt >= 0f && Mathf.Abs(s + l / 2f - gateAt) < 2.2f)
                    {
                        // the gate: two stone piers either side
                        if (Mathf.Abs(s + l / 2f - gateAt) < 1.6f) continue;
                    }
                    p.y = GroundAt(p);
                    bool intoChurch = false;
                    foreach (var hit in Physics.OverlapBox(p + Vector3.up * 0.6f, new Vector3(l / 2f, 0.5f, 0.7f), Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore))
                        if (hit.transform.IsChildOf(church)) intoChurch = true;
                    if (intoChurch) continue;
                    Box("Churchyard wall", g, p + Vector3.up * 0.25f, new Vector3(l + 0.05f, 1.6f, 0.55f), wallMat, yaw);   // 1.05 m above the ground, the rest below it
                }
                if (gateAt >= 0f)
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        var p = a + d * (gateAt + sgn * 1.9f); p.y = GroundAt(p);
                        Box("Gate pier", g, p + Vector3.up * 0.55f, new Vector3(0.7f, 2.2f, 0.7f), wallMat, yaw);
                    }
            }
            Side(new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), 38.9f);                                 // south, to the place
            Side(new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), -1f);
            Side(new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), -1f);
            Side(new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0), -1f);                                   // west (the model's own wall and gate)
        }

        // ---- the real ground (Terrain/SainteMereHeights.r16 + .json, made by Models/Source~/sme_terrain.py)
        static HeightMeta smeMeta;
        static ushort[] smeRaw;

        static bool LoadSmeHeights()
        {
            smeMeta = null; smeRaw = null;
            string dir = Path.GetDirectoryName(Application.dataPath);
            string raw = Path.Combine(dir, Root + "/Terrain/SainteMereHeights.r16"), meta = Path.Combine(dir, Root + "/Terrain/SainteMereHeights.json");
            if (!File.Exists(raw) || !File.Exists(meta)) return false;
            var m = JsonUtility.FromJson<HeightMeta>(File.ReadAllText(meta));
            var bytes = File.ReadAllBytes(raw);
            if (bytes.Length != m.resolution * m.resolution * 2) { Debug.LogError("Stan's Game: SainteMereHeights.r16 has the wrong size"); return false; }
            smeRaw = new ushort[m.resolution * m.resolution];
            System.Buffer.BlockCopy(bytes, 0, smeRaw, 0, bytes.Length);
            smeMeta = m;
            Debug.Log($"Stan's Game: Sainte-Mere-Eglise on the LiDAR ground ({m.resolution}^2 over {m.size} m). {m.source}");
            return true;
        }

        /// <summary>The ground (world y) at a point of the Sainte-Mere-Eglise map, bilinear from the height file.</summary>
        static float SmeY(float wx, float wz)
        {
            int n = smeMeta.resolution; float half = smeMeta.size * 0.5f;
            float fx = Mathf.Clamp((wx + half) / smeMeta.size * (n - 1), 0f, n - 1.001f);
            float fz = Mathf.Clamp((wz + half) / smeMeta.size * (n - 1), 0f, n - 1.001f);
            int ix = (int)fx, iz = (int)fz; float tx = fx - ix, tz = fz - iz;
            float a = smeRaw[iz * n + ix], b = smeRaw[iz * n + ix + 1], c = smeRaw[(iz + 1) * n + ix], d = smeRaw[(iz + 1) * n + ix + 1];
            return smeMeta.baseY + Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz) / 65535f * smeMeta.height;
        }

        /// <summary>
        /// Stand everything that was placed at y = 0 on the real ground: every direct child of a group (houses, trees,
        /// soldiers, townspeople, markers...). Things placed on the ground already (props, flak) are left alone.
        /// </summary>
        static void SmeGroundEverything(Transform root)
        {
            int moved = 0;
            var groups = new List<Transform> { root };
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (pureGroups.Contains(t)) groups.Add(t);
            foreach (var g in groups)
                for (int i = 0; i < g.childCount; i++)
                {
                    var c = g.GetChild(i);
                    if (pureGroups.Contains(c) || c.GetComponent<Terrain>() || c.GetComponent<Light>() || c.GetComponent<Camera>()) continue;
                    if (Mathf.Abs(c.position.y) > 0.02f) continue;
                    c.position += Vector3.up * GroundAt(c.position); moved++;
                }
            Debug.Log($"Stan's Game: {moved} things stood on the Sainte-Mere-Eglise ground");
        }

        // ---------------------------------------------------------------- the church of Notre-Dame-de-l'Assomption

        /// <summary>Top of the church tower (from the scan's TowerMarker when the Huberville church is used).</summary>
        static Vector3 smeTowerTop;
        static bool smeScannedChurch;

        /// <summary>
        /// The church: the photogrammetry scan of the 12th-century church of Huberville (a Cotentin village church near
        /// Valognes, paullemaignen, CC BY) as a stand-in for Notre-Dame-de-l'Assomption until that one is modelled;
        /// the primitive blockout when the scan is not in the project.
        /// </summary>
        static void SmeChurch(Transform town)
        {
            smeScannedChurch = false;
            if (SmeChurchModel(town)) { SmeSettleChurch(town); return; }   // the church modelled from the 1944 photos (PegasusLevelBuilder.Church.cs)
            var scan = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Town/ChurchHuberville.fbx");
            smeScannedChurch = scan;
            if (scan)
            {
                var sg = (GameObject)PrefabUtility.InstantiatePrefab(scan, town);
                sg.name = "Church (scan of Huberville church, stand-in)";
                PrefabUtility.UnpackPrefabInstance(sg, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                sg.transform.position = Vector3.zero; sg.transform.rotation = Quaternion.identity;
                var top = UsKitImporter.TakeMarker(sg, "TowerMarker");
                var bb = UsKitImporter.WorldBounds(sg);
                // long side east-west, as at Sainte-Mère-Église
                if (bb.size.z > bb.size.x) { sg.transform.rotation = Quaternion.Euler(0f, 90f, 0f); }
                bb = UsKitImporter.WorldBounds(sg);
                sg.transform.position += new Vector3(-bb.center.x, 0f, -bb.center.z);   // ground already at y = 0 in the model
                var mk = sg.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("TowerMarker"));
                smeTowerTop = mk ? mk.position : new Vector3(0f, bb.size.y, 0f);
                foreach (var mf in sg.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh && mf.sharedMesh.vertexCount > 100) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                var towerMark = new GameObject("Church tower").transform; towerMark.SetParent(sg.transform); towerMark.position = new Vector3(smeTowerTop.x, 0f, smeTowerTop.z);
                GameObjectUtility.SetStaticEditorFlags(sg, StaticEditorFlags.BatchingStatic);
                return;
            }
            smeTowerTop = SmeTower + Vector3.up * (SmeTowerHeight + 4.8f);
            var church = new GameObject("Church Notre-Dame-de-l'Assomption").transform; church.SetParent(town);
            var stone = Mat("SME_Church_Stone", new Color(0.55f, 0.52f, 0.46f), 0.12f);
            var slate = Mat("SME_Slate", new Color(0.18f, 0.19f, 0.21f), 0.3f);
            var glass = Mat("SME_Church_Glass", new Color(0.05f, 0.06f, 0.09f), 0.85f);
            const float naveW = 11f, naveH = 12f, naveX0 = -14.5f, naveX1 = 24f;
            float naveL = naveX1 - naveX0, naveCx = (naveX0 + naveX1) / 2f;
            Box("Nave", church, new Vector3(naveCx, naveH / 2f, 0f), new Vector3(naveL, naveH, naveW), stone);
            // steep slate roof
            var r1 = Box("Nave roof N", church, new Vector3(naveCx, naveH + 2.6f, 2.75f), new Vector3(naveL + 0.6f, 0.35f, 7.6f), slate); r1.transform.rotation = Quaternion.Euler(-45f, 0f, 0f);
            var r2 = Box("Nave roof S", church, new Vector3(naveCx, naveH + 2.6f, -2.75f), new Vector3(naveL + 0.6f, 0.35f, 7.6f), slate); r2.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            Box("Apse", church, new Vector3(naveX1 + 2.5f, 5.5f, 0f), new Vector3(5f, 11f, 8.5f), stone);
            Box("Transept", church, new Vector3(12f, naveH / 2f, 0f), new Vector3(8f, naveH, naveW + 9f), stone);
            for (int i = 0; i < 6; i++)
            {
                float x = naveX0 + 3f + i * (naveL - 6f) / 5f;
                if (Mathf.Abs(x - 12f) < 5f) continue;
                foreach (float s in new[] { -1f, 1f })
                {
                    Box("Lancet window", church, new Vector3(x, 6.5f, s * (naveW / 2f + 0.03f)), new Vector3(1.4f, 5f, 0.1f), glass, 0f, false);
                    Box("Buttress", church, new Vector3(x + 2.2f, 3.5f, s * (naveW / 2f + 0.6f)), new Vector3(0.9f, 7f, 1.2f), stone);
                }
            }
            // the west tower: square, a cornice with corner pinnacles (Steele's canopy caught on one) and a short stone spire
            var tower = new GameObject("Church tower").transform; tower.SetParent(church); tower.position = SmeTower;
            Box("Tower", tower, SmeTower + Vector3.up * SmeTowerHeight / 2f, new Vector3(SmeTowerSide, SmeTowerHeight, SmeTowerSide), stone);
            Box("Cornice", tower, SmeTower + Vector3.up * (SmeTowerHeight + 0.3f), new Vector3(SmeTowerSide + 0.8f, 0.6f, SmeTowerSide + 0.8f), stone);
            Box("Belfry openings W", tower, SmeTower + new Vector3(-SmeTowerSide / 2f - 0.03f, SmeTowerHeight - 5f, 0f), new Vector3(0.1f, 4f, 3f), glass, 0f, false);
            Box("Belfry openings N", tower, SmeTower + new Vector3(0f, SmeTowerHeight - 5f, SmeTowerSide / 2f + 0.03f), new Vector3(3f, 4f, 0.1f), glass, 0f, false);
            Box("Belfry openings S", tower, SmeTower + new Vector3(0f, SmeTowerHeight - 5f, -SmeTowerSide / 2f - 0.03f), new Vector3(3f, 4f, 0.1f), glass, 0f, false);
            Box("West door", tower, SmeTower + new Vector3(-SmeTowerSide / 2f - 0.05f, 2.2f, 0f), new Vector3(0.12f, 4.4f, 2.4f), Mat("SME_Door_Oak", new Color(0.22f, 0.15f, 0.09f)), 0f, false);
            float c = SmeTowerSide / 2f + 0.1f;
            foreach (var (sx, sz) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                Box("Pinnacle", tower, SmeTower + new Vector3(sx * c, SmeTowerHeight + 2.6f, sz * c), new Vector3(0.9f, 4.4f, 0.9f), stone);
            // spire: stacked shrinking blocks
            for (int k = 0; k < 6; k++)
            {
                float s = Mathf.Lerp(5.6f, 0.6f, k / 5f);
                Box("Spire", tower, SmeTower + Vector3.up * (SmeTowerHeight + 1.2f + k * 1.6f), new Vector3(s, 1.6f, s), slate, 45f);
            }
            Box("Cross", tower, SmeTower + Vector3.up * (SmeTowerHeight + 12f), new Vector3(0.15f, 2f, 0.15f), Mat("SME_Iron", new Color(0.1f, 0.1f, 0.1f), 0.4f, 0.8f), 0f, false);
            // the bell that rang the alarm for the fire
            var bell = Prim(PrimitiveType.Sphere, "Bell", tower, new Vector3(0f, SmeTowerHeight - 5.5f, 0f), Vector3.zero, new Vector3(1.4f, 1.6f, 1.4f), Mat("SME_Bronze", new Color(0.35f, 0.26f, 0.14f), 0.5f, 0.8f));
            bell.transform.position = SmeTower + Vector3.up * (SmeTowerHeight - 5.5f);
            GameObjectUtility.SetStaticEditorFlags(church.gameObject, StaticEditorFlags.BatchingStatic);
        }

        static void SmeSteele(Transform town)
        {
            // Pvt. John M. Steele, F Co. 505th: hung from a pinnacle by his canopy for hours, playing dead
            // the north side of the tower, away from the square, just under the top
            var pin = smeModelledChurch ? smeSteelePinnacle + new Vector3(-0.2f, -1.3f, 0.3f)
                    : smeScannedChurch ? smeTowerTop + new Vector3(1.4f, -1.2f, 2.6f)
                                       : SmeTower + new Vector3(SmeTowerSide / 2f + 0.1f, SmeTowerHeight + 4.8f, SmeTowerSide / 2f + 0.1f);
            var steele = new GameObject("Pvt. John Steele (hung up on the tower)");
            steele.transform.SetParent(town);
            steele.transform.position = pin;
            var cloth = Mat("SME_Canopy", new Color(0.55f, 0.53f, 0.42f), 0.1f);
            Prim(PrimitiveType.Sphere, "Canopy (draped)", steele.transform, new Vector3(0.8f, -1.2f, 1.2f), new Vector3(70f, 30f, 0f), new Vector3(6f, 1.2f, 4f), cloth);
            var body = new GameObject("Body").transform; body.SetParent(steele.transform, false);
            body.localPosition = new Vector3(0.6f, -9.2f, 1.3f);
            for (int i = 0; i < 4; i++)
            {
                var top = new Vector3(Random.Range(0f, 1.5f), -1.5f, Random.Range(0.5f, 2f));
                var bot = body.localPosition + new Vector3(0f, 1.9f, 0f);
                var l = Prim(PrimitiveType.Cylinder, "Rigging line", steele.transform, (top + bot) / 2f, Vector3.zero, new Vector3(0.02f, (top - bot).magnitude / 2f, 0.02f), Mat("SME_Line", new Color(0.75f, 0.73f, 0.62f)));
                l.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (top - bot).normalized);
            }
            UsBody(body, false);
            steele.AddComponent<HangingParatrooper>();
        }

        // ---------------------------------------------------------------- square, houses, town hall, the fire

        static void SmeSquareAndHouses(Transform town)
        {
            var houses = Group("Houses", town);
            var walls = new[]
            {
                Mat("SME_Wall_Limestone", new Color(0.62f, 0.58f, 0.50f)), Mat("SME_Wall_Grey", new Color(0.48f, 0.47f, 0.44f)),
                Mat("SME_Wall_Render", new Color(0.66f, 0.63f, 0.57f)),
            };
            var roofs = new[] { Mat("SME_Slate", new Color(0.18f, 0.19f, 0.21f), 0.3f), Mat("SME_Roof_Tile", new Color(0.34f, 0.22f, 0.17f)) };
            int n = 0;
            var rng = new System.Random(1944);
            // A terrace: houses wall to wall along a street, front (shop fronts, doors, shutters) towards the street.
            void Row(Vector3 from, Vector3 to, float depth, float yaw, params float[] gaps)
            {
                float len = Vector3.Distance(from, to); var dir = (to - from).normalized; float pos = 0f;
                while (pos < len - 5f)
                {
                    float w = Mathf.Min(6.5f + (float)rng.NextDouble() * 6f, len - pos);
                    float mid = pos + w / 2f;
                    bool gap = false; foreach (var g in gaps) if (Mathf.Abs(mid - g) < 7f) gap = true;
                    if (!gap) SmeTownHouse($"House {++n}", houses, from + dir * mid, w, depth, yaw, rng, walls, roofs);
                    pos += w;
                }
            }
            // A terrace along a street centre line a -> b, on its left (+1) or right (-1) side, fronts to the street,
            // starting 'skip' metres in (clear of the corner).
            void Street(Vector3 a0, Vector3 b0, float off, float side, float skip, params float[] gaps)
            {
                var dir = (b0 - a0).normalized; var nrm = new Vector3(-dir.z, 0f, dir.x) * side;
                var f = -nrm; float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                Row(a0 + dir * skip + nrm * off, b0 + nrm * off, 9f, yaw, gaps);
            }
            // rue General de Gaulle (the N13), both sides; the place opens on its east side
            Row(new Vector3(SmeN13X - 10.5f, 0f, -270f), new Vector3(SmeN13X - 10.5f, 0f, 270f), 9f, 90f, 316f);          // west side; gap: road to La Fiere
            Row(new Vector3(SmeN13X + 10.5f, 0f, -270f), new Vector3(SmeN13X + 10.5f, 0f, -66f), 9f, -90f, 168f, 182f);   // east side south; gap: the town hall
            Row(new Vector3(SmeN13X + 10.5f, 0f, 32f), new Vector3(SmeN13X + 10.5f, 0f, 270f), 9f, -90f);                 // east side north
            // round the place
            Row(new Vector3(-70f, 0f, 34.5f), new Vector3(30f, 0f, 34.5f), 9f, 180f);                                      // north side, across the street
            Row(new Vector3(38f, 0f, -56f), new Vector3(38f, 0f, 16f), 9f, -90f);                                          // east side
            Row(new Vector3(-76f, 0f, -65.5f), new Vector3(30f, 0f, -65.5f), 9f, 0f, 46f);                                 // south side; gap: the Pommier house
            // out along the other roads
            Street(new Vector3(33f, 0f, 24f), new Vector3(110f, 0f, 40f), 8.5f, 1f, 14f);                                // to Sainte-Marie-du-Mont
            Street(new Vector3(33f, 0f, 24f), new Vector3(110f, 0f, 40f), 8.5f, -1f, 14f);
            Street(new Vector3(110f, 0f, 40f), new Vector3(170f, 0f, 55f), 8.5f, 1f, 6f);
            Street(new Vector3(33f, 0f, 24f), new Vector3(78f, 0f, 70f), 8f, 1f, 16f);                                   // north
            Street(new Vector3(78f, 0f, 70f), new Vector3(86f, 0f, 150f), 8f, 1f, 6f);
            Street(new Vector3(78f, 0f, 70f), new Vector3(86f, 0f, 150f), 8f, -1f, 10f);
            Street(new Vector3(SmeN13X - 14f, 0f, 46f), new Vector3(-190f, 0f, 46f), 8.5f, 1f, 6f);                       // to La Fiere
            Street(new Vector3(SmeN13X - 14f, 0f, 46f), new Vector3(-170f, 0f, 46f), 8.5f, -1f, 6f);

            // the square itself: a few big lime trees and the pump the bucket chain used
            var bark = Mat("Bark", new Color(0.23f, 0.20f, 0.17f));
            var leaves = Mat("SME_Lime_Leaves", new Color(0.13f, 0.20f, 0.10f));
            var trees = Group("Square trees", town);
            foreach (var p in new[] { new Vector3(-78f, 0f, 12f), new Vector3(-78f, 0f, -20f), new Vector3(-78f, 0f, -50f), new Vector3(-55f, 0f, 14f),
                                      new Vector3(22f, 0f, 14f), new Vector3(26f, 0f, -48f), new Vector3(-2f, 0f, -52f), new Vector3(28f, 0f, -18f) })
                Broadleaf(trees, p, bark, leaves);
            var pump = new GameObject("Pump").transform; pump.SetParent(town); pump.position = SmePump;
            Box("Pump", pump, pump.position + Vector3.up * 0.8f, new Vector3(0.4f, 1.6f, 0.4f), Mat("SME_Iron", new Color(0.1f, 0.1f, 0.1f), 0.4f, 0.8f));
            Box("Trough", pump, pump.position + new Vector3(0f, 0.35f, 0.8f), new Vector3(1.6f, 0.7f, 0.8f), Mat("SME_Church_Stone", new Color(0.55f, 0.52f, 0.46f)));
            // low churchyard wall
            var wallMat = Mat("SME_Church_Stone", new Color(0.55f, 0.52f, 0.46f));
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Town/ChurchHuberville.fbx"))
            {
                Box("Churchyard wall N", town, new Vector3(4f, 0.45f, 11f), new Vector3(52f, 0.9f, 0.5f), wallMat);
                Box("Churchyard wall S", town, new Vector3(4f, 0.45f, -11f), new Vector3(52f, 0.9f, 0.5f), wallMat);
            }
        }

        /// <summary>
        /// A Sainte-Mère-Église town house as in the 1944 photos: two or three storeys of granite rubble or rendered
        /// stone, a steep slate roof with the ridge along the street, big chimney stacks on the gable ends, a dormer or
        /// two, tall windows with wooden shutters, and often a shop front with a painted sign on the ground floor.
        /// Front = local +Z. The walls go 2 m into the ground so the house never floats where the ground dips.
        /// </summary>
        static void SmeTownHouse(string name, Transform parent, Vector3 pos, float w, float d, float yaw, System.Random rng, Material[] walls, Material[] roofs)
        {
            var h = new GameObject(name).transform; h.SetParent(parent);
            h.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            int storeys = rng.NextDouble() < 0.35 ? 3 : 2;
            float H = storeys * 3.1f + 0.4f;
            var wall = walls[rng.Next(walls.Length)]; var roof = roofs[0];
            var stone = Mat("SME_Wall_Granite", new Color(0.50f, 0.48f, 0.44f));
            var glass = Mat("Window_Dark", new Color(0.05f, 0.05f, 0.06f), 0.8f);
            Color[] shutterCols = { new Color(0.42f, 0.45f, 0.40f), new Color(0.75f, 0.74f, 0.70f), new Color(0.30f, 0.22f, 0.16f), new Color(0.32f, 0.38f, 0.42f) };
            var shutter = Mat($"SME_Shutter_{rng.Next(shutterCols.Length)}", shutterCols[rng.Next(shutterCols.Length)]);
            void P(string n, Vector3 lp, Vector3 size, Material m, float rx = 0f, bool col = false)
                => Prim(PrimitiveType.Cube, n, h, lp, new Vector3(rx, 0f, 0f), size, m, col);
            P("Walls", new Vector3(0f, H / 2f - 1f, 0f), new Vector3(w, H + 2f, d), wall, 0f, true);
            P("Plinth", new Vector3(0f, 0.3f, d / 2f + 0.03f), new Vector3(w, 0.6f, 0.08f), stone);
            // roof: 45 degree slate, ridge along the street
            float half = d / 2f + 0.35f, pitch = 45f, slope = half / Mathf.Cos(pitch * Mathf.Deg2Rad);
            float rise = half * Mathf.Tan(pitch * Mathf.Deg2Rad);
            P("Roof front", new Vector3(0f, H + rise / 2f, d / 4f + 0.1f), new Vector3(w + 0.3f, 0.22f, slope), roof, pitch, true);
            P("Roof back", new Vector3(0f, H + rise / 2f, -d / 4f - 0.1f), new Vector3(w + 0.3f, 0.22f, slope), roof, -pitch, true);
            // gable ends (closing the roof triangle) and the chimney stacks on them
            foreach (float sx in new[] { -1f, 1f })
            {
                // a square turned 45 degrees: its top half is exactly the roof triangle, the bottom half is inside the walls
                Prim(PrimitiveType.Cube, "Gable", h, new Vector3(sx * (w / 2f - 0.15f), H, 0f), new Vector3(45f, 0f, 0f), new Vector3(0.3f, (half - 0.3f) * 1.4142f, (half - 0.3f) * 1.4142f), wall);
                if (rng.NextDouble() < 0.8)
                    P("Chimney", new Vector3(sx * (w / 2f - 0.5f), H + rise + 0.6f, 0f), new Vector3(0.7f, 1.6f + (float)rng.NextDouble(), 1.1f), stone, 0f, true);
            }
            // windows with shutters, storey by storey, front and back
            int nWin = Mathf.Max(2, Mathf.RoundToInt(w / 2.7f));
            bool shop = rng.NextDouble() < 0.45;
            for (int f = 0; f < storeys; f++)
                for (int k = 0; k < nWin; k++)
                {
                    float x = -w / 2f + (k + 0.5f) * w / nWin, y = 1.6f + f * 3.1f;
                    foreach (float side in new[] { 1f, -1f })
                    {
                        if (f == 0 && side > 0f && (shop || k == nWin / 2)) continue;   // shop front or door here
                        P("Window", new Vector3(x, y, side * (d / 2f + 0.02f)), new Vector3(0.95f, 1.7f, 0.05f), glass);
                        if (side > 0f)
                            foreach (float sx in new[] { -1f, 1f })
                                P("Shutter", new Vector3(x + sx * 0.75f, y, d / 2f + 0.06f), new Vector3(0.5f, 1.75f, 0.05f), shutter);
                    }
                }
            if (shop)
            {
                // shop front: a wide window, the door and a painted sign board, as on the boulangerie and the cafés
                Color[] signCols = { new Color(0.80f, 0.77f, 0.66f), new Color(0.25f, 0.30f, 0.22f), new Color(0.45f, 0.18f, 0.14f), new Color(0.20f, 0.22f, 0.30f) };
                var sign = Mat($"SME_ShopSign_{rng.Next(signCols.Length)}", signCols[rng.Next(signCols.Length)]);
                P("Shop window", new Vector3(-w * 0.12f, 1.5f, d / 2f + 0.03f), new Vector3(w * 0.55f, 2.0f, 0.06f), glass);
                P("Shop front frame", new Vector3(0f, 1.6f, d / 2f + 0.02f), new Vector3(w * 0.92f, 2.6f, 0.04f), sign);
                P("Shop sign", new Vector3(0f, 3.15f, d / 2f + 0.08f), new Vector3(w * 0.8f, 0.55f, 0.08f), sign);
                P("Door", new Vector3(w * 0.3f, 1.1f, d / 2f + 0.04f), new Vector3(1.0f, 2.2f, 0.06f), Mat("SME_Door_Oak", new Color(0.22f, 0.15f, 0.09f)));
            }
            else
                P("Door", new Vector3(-w / 2f + (nWin / 2 + 0.5f) * w / nWin, 1.1f, d / 2f + 0.03f), new Vector3(1.0f, 2.2f, 0.06f), Mat("SME_Door_Oak", new Color(0.22f, 0.15f, 0.09f)));
            // dormers in the roof
            int dormers = rng.Next(0, 3);
            for (int k = 0; k < dormers; k++)
            {
                float x = -w / 2f + (k + 1f) * w / (dormers + 1f);
                P("Dormer", new Vector3(x, H + 0.9f, d / 2f - 0.6f), new Vector3(1.2f, 1.5f, 1.2f), wall);
                P("Dormer window", new Vector3(x, H + 0.85f, d / 2f + 0.02f), new Vector3(0.7f, 0.9f, 0.05f), glass);
                P("Dormer roof", new Vector3(x, H + 1.75f, d / 2f - 0.6f), new Vector3(1.45f, 0.15f, 1.5f), roof, 0f);
            }
            GameObjectUtility.SetStaticEditorFlags(h.gameObject, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>Walls carried 2 m into the ground under a House() so it never floats where the ground dips.</summary>
        static void SmeFoundation(GameObject house, Vector3 size, Material m)
            => Prim(PrimitiveType.Cube, "Foundation", house.transform, new Vector3(0f, -0.95f, 0f), Vector3.zero, new Vector3(size.x, 2f, size.z), m, true);

        static Transform SmeTownHallAndFlag(Transform town, out GameObject flag)
        {
            var hall = House("Town hall (Mairie)", town, SmeTownHall, new Vector3(16f, 9f, 11f), Mat("SME_Wall_Limestone", new Color(0.62f, 0.58f, 0.50f)), Mat("SME_Slate", new Color(0.18f, 0.19f, 0.21f), 0.3f), 90f);
            SmeFoundation(hall, new Vector3(16f, 9f, 11f), Mat("SME_Wall_Limestone", new Color(0.62f, 0.58f, 0.50f)));
            var steps = Box("Steps", hall.transform, SmeTownHall + new Vector3(-6.2f, 0.25f, 0f), new Vector3(1.4f, 0.5f, 4f), Mat("SME_Church_Stone", new Color(0.55f, 0.52f, 0.46f)));
            // the flagpole over the door; the Stars and Stripes goes up at 04:30
            var poleBase = SmeTownHall + new Vector3(-5.6f, 7.5f, 0f);
            var pole = Prim(PrimitiveType.Cylinder, "Flagpole", hall.transform, Vector3.zero, Vector3.zero, new Vector3(0.08f, 2.2f, 0.08f), Mat("SME_Iron", new Color(0.1f, 0.1f, 0.1f), 0.4f, 0.8f));
            pole.transform.position = poleBase + new Vector3(-1.2f, 1.2f, 0f);
            pole.transform.rotation = Quaternion.Euler(0f, 0f, 40f);
            flag = new GameObject("Stars and Stripes");
            flag.transform.SetParent(hall.transform);
            flag.transform.position = poleBase + new Vector3(-2.6f, 2.0f, 0f);
            flag.transform.rotation = Quaternion.identity;
            var red = Mat("SME_Flag_Red", new Color(0.6f, 0.08f, 0.1f)); var white = Mat("SME_Flag_White", new Color(0.85f, 0.85f, 0.82f)); var blue = Mat("SME_Flag_Blue", new Color(0.08f, 0.12f, 0.32f));
            for (int s = 0; s < 13; s++)
                Prim(PrimitiveType.Cube, "Stripe", flag.transform, new Vector3(0f, -s * 0.1f, 0.9f), Vector3.zero, new Vector3(0.02f, 0.1f, 1.8f), s % 2 == 0 ? red : white);
            Prim(PrimitiveType.Cube, "Canton", flag.transform, new Vector3(-0.01f, -0.25f, 0.37f), Vector3.zero, new Vector3(0.02f, 0.55f, 0.75f), blue);
            return hall.transform;
        }

        /// <summary>A particle material for the fire, saved as an asset so that its shader goes into the build
        /// (made at run time it was missing there: Joan's printscreen 11, red squares instead of flames).</summary>
        static Material FireMat(string tex, bool additive)
        {
            string path = $"{Root}/Textures/Generated/{tex}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = tex }; AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/Generated/{tex}.png"));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", additive ? 2f : 0f); m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD"); else m.DisableKeyword("_BLENDMODE_ADD");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SmeFire(Transform town)
        {
            var burnt = Mat("SME_Wall_Scorched", new Color(0.20f, 0.17f, 0.14f));
            var h = House("Pommier house (on fire)", town, SmeFireHouse, new Vector3(10f, 7f, 9f), burnt, Mat("SME_Roof_Burning", new Color(0.12f, 0.08f, 0.06f)), 0f);
            SmeFoundation(h, new Vector3(10f, 7f, 9f), burnt);
            var fire = new GameObject("Fire").AddComponent<HouseFire>();
            fire.intensity = 22f; fire.range = 90f;
            fire.size = new Vector3(10f, 7f, 9f);
            fire.flameMat = FireMat("FireFlame", true); fire.emberMat = FireMat("FireEmber", true); fire.smokeMat = FireMat("FireSmoke", false);
            fire.transform.SetParent(h.transform); fire.transform.position = SmeFireHouse + Vector3.up * 3f;
            // the barn behind it, where the fire started
            var barn = House("Pommier barn", town, SmeFireHouse + new Vector3(0f, 0f, -13f), new Vector3(12f, 5f, 7f), burnt, Mat("SME_Roof_Burning", new Color(0.12f, 0.08f, 0.06f)), 0f);
            SmeFoundation(barn, new Vector3(12f, 5f, 7f), burnt);
        }

        // ---------------------------------------------------------------- the bocage

        static void SmeHedgerows(Transform env)
        {
            var g = Group("Hedgerows (bocage)", env);
            var bank = Mat("SME_Hedge_Bank_Grass", new Color(0.17f, 0.19f, 0.10f), 0.05f);
            var bushes = new[] { Mat("SME_Hedge", new Color(0.10f, 0.17f, 0.08f), 0.05f), Mat("SME_Hedge_2", new Color(0.08f, 0.14f, 0.07f), 0.05f), Mat("SME_Hedge_3", new Color(0.13f, 0.19f, 0.09f), 0.05f) };
            // one 8 m stretch of hedgerow: a rounded earth bank (half in the ground, grassed over) under two or three
            // overlapping bushes of hawthorn and hazel - not one block
            void Hedge(Vector3 p, float dirYaw)
            {
                var q = Quaternion.Euler(0f, dirYaw, 0f); var along = q * Vector3.forward;
                var b = Prim(PrimitiveType.Sphere, "Hedge bank", g, Vector3.zero, Vector3.zero, new Vector3(2.8f, 1.5f, 9.2f), bank, true);
                b.transform.SetPositionAndRotation(p + Vector3.up * 0.1f, q);
                int n = Random.value < 0.5f ? 2 : 3;
                for (int k = 0; k < n; k++)
                {
                    float t = (k - (n - 1) / 2f) * (8f / n) + Random.Range(-0.6f, 0.6f);
                    float h = Random.Range(1.9f, 3.1f), w = Random.Range(2.2f, 3.2f), l = 8f / n + Random.Range(1.2f, 2.6f);
                    var c = p + along * t; c.y = GroundAt(c) + 0.5f + h * 0.42f;
                    var s = Prim(PrimitiveType.Sphere, "Hedge", g, Vector3.zero, Vector3.zero, new Vector3(w, h, l), bushes[Random.Range(0, bushes.Length)]);
                    s.transform.SetPositionAndRotation(c, q * Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(-12f, 12f), Random.Range(-6f, 6f)));
                }
            }
            var bark = Mat("Bark", new Color(0.23f, 0.20f, 0.17f));
            var leaves = Mat("SME_Lime_Leaves", new Color(0.13f, 0.20f, 0.10f));
            // the real field boundaries, where the LiDAR still shows their banks (Terrain/SainteMereHedges.json, from
            // Models/Source~/sme_hedges.py); the made-up bocage fills the rest (many hedges went in the 1960s land
            // consolidation, and the fields north and west of the town are housing now) but keeps clear of them
            var realHedges = new List<Vector2>();
            string hf = Path.Combine(Path.GetDirectoryName(Application.dataPath), Root + "/Terrain/SainteMereHedges.json");
            if (File.Exists(hf))
            {
                var file = JsonUtility.FromJson<SmeHedgeFile>(File.ReadAllText(hf));
                foreach (var pc in file.pieces)
                {
                    var p = new Vector3(pc.x, 0f, pc.z);
                    if (SmeRoadDistance(p.x, p.z, out _) < 3f || SmeInTown(p.x, p.z) || Vector3.Distance(p, SmeRally) < 45f || NearCottage(p)) continue;
                    p.y = GroundAt(p);
                    Hedge(p, pc.yaw);
                    if (Random.value < 0.12f) Broadleaf(g, p, bark, leaves);
                    realHedges.Add(new Vector2(p.x, p.z));
                }
            }
            bool NearReal(Vector3 q) { foreach (var r in realHedges) if ((r - new Vector2(q.x, q.z)).sqrMagnitude < 22f * 22f) return true; return NearCottage(q); }
            const float cell = 95f;
            for (float x = -480f; x < 480f; x += cell)
                for (float z = -480f; z < 480f; z += cell)
                {
                    float jx = x + Random.Range(-15f, 15f), jz = z + Random.Range(-15f, 15f);
                    foreach (var (a, b) in new[] { (new Vector3(jx, 0f, jz), new Vector3(jx + cell, 0f, jz)), (new Vector3(jx, 0f, jz), new Vector3(jx, 0f, jz + cell)) })
                    {
                        Vector3 mid = (a + b) / 2f;
                        if (SmeInTown(mid.x, mid.z) || Vector3.Distance(mid, SmeRally) < 60f) continue;
                        // split the hedge where a road crosses it, and leave a gate in every hedge
                        float len = Vector3.Distance(a, b); var dir = (b - a) / len;
                        float gate = Random.Range(0.2f, 0.8f) * len;
                        for (float s = 0f; s < len; s += 8f)
                        {
                            Vector3 p = a + dir * (s + 4f); p.y = GroundAt(p);
                            if (Mathf.Abs(s + 4f - gate) < 5f || SmeRoadDistance(p.x, p.z, out _) < 3f || SmeInTown(p.x, p.z) || NearReal(p)) continue;
                            Hedge(p, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                            if (Random.value < 0.12f) Broadleaf(g, p, bark, leaves);
                        }
                    }
                }
            GameObjectUtility.SetStaticEditorFlags(g.gameObject, StaticEditorFlags.BatchingStatic);
        }

        // ---------------------------------------------------------------- the edge of the town

        static readonly List<Vector3> smeCottages = new List<Vector3>();
        static bool NearCottage(Vector3 q) { foreach (var c in smeCottages) if (new Vector2(c.x - q.x, c.z - q.z).sqrMagnitude < 16f * 16f) return true; return false; }

        /// <summary>
        /// Detached stone houses on the roads out of the town, set back behind a low garden wall (photos 15, 16, 19):
        /// Joan's Benouville house model (Models/Town/NormandyCottage.fbx, its embedded textures taken out; front = +Z).
        /// </summary>
        static void SmeCottages(Transform town)
        {
            smeCottages.Clear();
            string path = Root + "/Models/Town/NormandyCottage.fbx";
            if (AssetImporter.GetAtPath(path) is ModelImporter mi && mi.materialImportMode != ModelImporterMaterialImportMode.None)
            { mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.SaveAndReimport(); }
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset) return;
            var g = Group("Cottages (edge of the town)", town);
            var mat = TexMat("SME_Cottage", Root + "/Models/Town/NormandyCottage_BaseColor.jpg", 0.1f);
            var wallMat = Mat("SME_Wall_Granite", new Color(0.50f, 0.48f, 0.44f));
            var spots = new (Vector3 p, float yaw)[]
            {
                (new Vector3(SmeN13X - 14f, 0f, 300f), 90f), (new Vector3(SmeN13X + 14f, 0f, 340f), -90f), (new Vector3(SmeN13X - 14f, 0f, 395f), 90f), (new Vector3(SmeN13X + 14f, 0f, 440f), -90f),   // N13 north
                (new Vector3(SmeN13X - 14f, 0f, -305f), 90f), (new Vector3(SmeN13X + 14f, 0f, -350f), -90f), (new Vector3(SmeN13X - 14f, 0f, -420f), 90f), (new Vector3(SmeN13X + 14f, 0f, -460f), -90f), // N13 south
                (new Vector3(-270f, 0f, 59f), 180f), (new Vector3(-350f, 0f, 59f), 180f), (new Vector3(-430f, 0f, 59f), 180f), (new Vector3(-300f, 0f, 33f), 0f), (new Vector3(-390f, 0f, 33f), 0f),      // to La Fiere
                (new Vector3(194f, 0f, 74.4f), 166f), (new Vector3(249f, 0f, 61.4f), -14f), (new Vector3(300.8f, 0f, 102.6f), 166f),                                                                     // to Sainte-Marie-du-Mont
                (new Vector3(76.8f, 0f, 191f), 95f), (new Vector3(107.3f, 0f, 238.4f), -85f),                                                                                                             // to Beuzeville
            };
            int n = 0;
            foreach (var (p0, yaw) in spots)
            {
                var p = p0; p.y = GroundAt(p0);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, g);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = $"Cottage {++n}";
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f) * asset.transform.rotation);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = mat; r.sharedMaterials = ms;
                    var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) r.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }
                var root = new GameObject($"Cottage {n} (plot)").transform; root.SetParent(g); root.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                go.transform.SetParent(root, true);
                // carried down into the ground where it slopes, and a low stone wall along the front garden
                Prim(PrimitiveType.Cube, "Foundation", root, new Vector3(0f, -0.95f, 0f), Vector3.zero, new Vector3(11.4f, 2f, 7.4f), wallMat, true);
                var f = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; var sideV = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                foreach (float sx in new[] { -1f, 1f })
                {
                    Vector3 wp = p + f * 8.2f + sideV * sx * 4.1f; wp.y = GroundAt(wp) + 0.45f;
                    Box("Garden wall", root, wp, new Vector3(5.6f, 1.6f, 0.5f), wallMat, yaw);   // a gate in the middle
                }
                smeCottages.Add(p);
            }
        }

        /// <summary>
        /// The shop signs on the rue General de Gaulle in the June 1944 photos: Charles Lefrancois (28), the Boulangerie
        /// P. Deschamps (27), the cafe sign ending "...ndes Marques" (03) and the coiffeur (02, 12). Painted boards over
        /// the shop fronts; the exact houses they were on are not known, so they are spread along the street.
        /// </summary>
        static void SmeShopSigns(Transform town)
        {
            var g = Group("Shop signs (rue General de Gaulle)", town);
            var board = Mat("SME_Sign_Board", new Color(0.13f, 0.12f, 0.10f));
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // (z along the street, which side: -1 west row facing east, +1 east row facing west, text)
            var signs = new (float z, float side, string text)[]
            {
                (12f, -1f, "CHARLES LEFRANÇOIS"), (-36f, -1f, "BOULANGERIE   P. DESCHAMPS"), (78f, 1f, "GRANDES MARQUES"), (-128f, 1f, "COIFFEUR"),
            };
            foreach (var (z, side, text) in signs)
            {
                float face = SmeN13X + side * 6f;                 // the house fronts are 6 m from the middle of the street
                var p = new Vector3(face - side * 0.12f, 0f, z); p.y = GroundAt(p) + 3.25f;
                float yaw = side < 0f ? 90f : -90f;               // facing the street
                var b = Box($"Sign: {text}", g, p, new Vector3(Mathf.Max(3.2f, text.Length * 0.33f), 0.62f, 0.08f), board, yaw);
                var tgo = new GameObject("Lettering"); tgo.transform.SetParent(b.transform, false);
                tgo.transform.localPosition = new Vector3(0f, 0f, 0.55f);   // in front of the board (local z is the board's thin axis)
                tgo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var tm = tgo.AddComponent<TextMesh>();
                tm.text = text; tm.font = font; tm.fontSize = 64; tm.characterSize = 0.032f;
                tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = new Color(0.86f, 0.80f, 0.62f);
                var mr = tgo.GetComponent<MeshRenderer>(); mr.sharedMaterial = font.material;
                // undo the board's stretch so the letters keep their shape
                var ls = b.transform.lossyScale; tgo.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
            }
        }

        [System.Serializable] class SmeHedgePiece { public float x, z, yaw; }
        [System.Serializable] class SmeHedgeFile { public SmeHedgePiece[] pieces; }

        static void SmeOrchard(Transform env)
        {
            var g = Group("Orchard (3/505 assembly)", env);
            var bark = Mat("Bark", new Color(0.23f, 0.20f, 0.17f));
            var leaves = Mat("SME_Apple_Leaves", new Color(0.15f, 0.22f, 0.10f));
            for (int i = -3; i <= 3; i++)
                for (int k = -3; k <= 3; k++)
                    AppleTree(g, SmeRally + new Vector3(i * 9f + Random.Range(-1f, 1f), 0f, k * 9f + Random.Range(-1f, 1f) + 12f), bark, leaves);
        }

        static void SmeFlak(Transform env)
        {
            var g = Group("Flak (2 cm Flak 38)", env);
            foreach (var p0 in new[] { new Vector3(130f, 0f, 115f), new Vector3(95f, 0f, -170f), new Vector3(-150f, 0f, 160f) })
            {
                var p = p0; p.y = GroundAt(p0);   // standing on the field, not at height 0
                var gun = new GameObject("2 cm Flak 38").transform; gun.SetParent(g); gun.position = p;
                if (SmeFlakvierling(gun, p)) continue;
                var flakModel = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Vehicles/Flakvierling38.fbx");
                if (flakModel)
                {
                    // the Flakvierling 38 (DFS_studio, CC BY): four 2 cm guns on one mount
                    gun.name = "2 cm Flakvierling 38";
                    var fm = (GameObject)PrefabUtility.InstantiatePrefab(flakModel, gun);
                    PrefabUtility.UnpackPrefabInstance(fm, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    fm.transform.localPosition = Vector3.zero; fm.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    var mz = fm.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("MuzzleMarker"));
                    Vector3 mzPos = mz ? mz.position : p + Vector3.up * 2f;
                    UsKitImporter.TakeMarker(fm, "MuzzleMarker");
                    var muzzleT = new GameObject("Muzzle").transform; muzzleT.SetParent(gun); muzzleT.position = mzPos;
                    var fb = UsKitImporter.WorldBounds(fm);
                    var box = gun.gameObject.AddComponent<BoxCollider>(); box.center = fb.center - p; box.size = fb.size * 0.8f;
                    SandbagRing(gun, p, 3.6f, 1.1f, Vector3.back, 1);
                    gun.gameObject.AddComponent<FlakGun>().barrel = muzzleT;
                    continue;
                }
                var grey = Mat("Feldgrau", new Color(0.36f, 0.38f, 0.35f));
                Box("Platform", gun, p + Vector3.up * 0.3f, new Vector3(2.6f, 0.4f, 2.6f), grey);
                var barrel = Prim(PrimitiveType.Cylinder, "Barrel", gun, new Vector3(0f, 1.8f, 0.6f), new Vector3(-40f, 0f, 0f), new Vector3(0.1f, 1.1f, 0.1f), grey);
                var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(barrel.transform, false); muzzle.localPosition = new Vector3(0f, 1f, 0f);
                SandbagRing(gun, p, 3.6f, 1.1f, Vector3.back, 1);
                var flak = gun.gameObject.AddComponent<FlakGun>(); flak.barrel = muzzle;
            }
        }

        /// <summary>
        /// The Flakvierling 38 from its moving parts (Models/Vehicles/Flakvierling38_Parts.fbx, made by
        /// Models/Source~/flakvierling_parts.py from the DFS_studio rig): fixed base, traversing carriage, elevating cradle
        /// with the four guns and a muzzle marker on each, seats and handwheels. Manned as the real detachment of eight:
        /// the gunner and the two loaders seated on the gun, the gun commander and an ammunition number in the pit
        /// (the range-taker and the rest of the ammunition numbers are left out).
        /// </summary>
        static bool SmeFlakvierling(Transform gun, Vector3 p)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Vehicles/Flakvierling38_Parts.fbx");
            if (!model) return false;
            gun.name = "2 cm Flakvierling 38";
            var fm = (GameObject)PrefabUtility.InstantiatePrefab(model, gun);
            PrefabUtility.UnpackPrefabInstance(fm, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            float yaw = Random.Range(0f, 360f);
            fm.transform.localPosition = Vector3.zero; fm.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * fm.transform.localRotation;
            // the markers (seats, handwheels, muzzles) are 1 mm triangles in the FBX: keep the transforms, drop the meshes
            foreach (var t in fm.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Muzzle") || t.name.StartsWith("Seat_") || t.name.StartsWith("Wheel_"))
                {
                    var mr = t.GetComponent<MeshRenderer>(); if (mr) Object.DestroyImmediate(mr);
                    var mf = t.GetComponent<MeshFilter>(); if (mf) Object.DestroyImmediate(mf);
                }
            var paint = Mat("Flak_Dunkelgelb", new Color(0.42f, 0.38f, 0.25f), 0.35f, 0.3f);
            foreach (var r in fm.GetComponentsInChildren<Renderer>()) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = paint; r.sharedMaterials = ms; }
            Transform Find(string n) => fm.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
            // the FBX parts all sit at the top level (the exporter misplaces child objects): build the hierarchy here,
            // cradle and muzzles riding the carriage, seats and handwheels on the carriage
            var carT = Find("Carriage"); var craT = Find("Cradle");
            if (carT && craT)
            {
                craT.SetParent(carT, true);
                foreach (var t in fm.GetComponentsInChildren<Transform>(true).ToArray())
                    if (t.name.StartsWith("Muzzle")) t.SetParent(craT, true);
                    else if (t.name.StartsWith("Seat_") || t.name.StartsWith("Wheel_")) t.SetParent(carT, true);
            }
            var flak = gun.gameObject.AddComponent<FlakGun>();
            flak.carriage = Find("Carriage"); flak.cradle = Find("Cradle");
            flak.muzzles = new[] { Find("Muzzle1"), Find("Muzzle2"), Find("Muzzle3"), Find("Muzzle4") }.Where(t => t).ToArray();
            flak.wheelTraverse = Find("Wheel_Traverse"); flak.wheelElevation = Find("Wheel_Elevation");
            flak.seats = new[] { Find("Seat_Gunner"), Find("Seat_LoaderL"), Find("Seat_LoaderR") };
            flak.barrel = flak.muzzles.Length > 0 ? flak.muzzles[0] : null;
            if (!flak.carriage || !flak.cradle || flak.muzzles.Length == 0) Debug.LogWarning("Stan's Game: Flakvierling38_Parts is missing Carriage/Cradle/Muzzle markers");
            // a low collider over the base only, so the crew on the gun can be shot
            var b = Find("Base") ? UsKitImporter.WorldBounds(Find("Base").gameObject) : UsKitImporter.WorldBounds(fm);
            var box = gun.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(b.center.x - p.x, 0.3f, b.center.z - p.z); box.size = new Vector3(b.size.x * 0.6f, 0.6f, b.size.z * 0.6f);
            SandbagRing(gun, p, 3.6f, 1.1f, Vector3.back, 1);

            // the gun numbers on their seats
            var crew = new Health[3];
            string[] names = { "Flak gunner (seated)", "Flak loader left (seated)", "Flak loader right (seated)" };
            for (int i = 0; i < 3; i++)
                if (flak.seats[i] && flak.carriage) crew[i] = FlakCrewman(names[i], flak.seats[i], flak.carriage);
            flak.crew = crew;
            // the gun commander and an ammunition number in the pit: ordinary soldiers who fight back
            var q = Quaternion.Euler(0f, yaw, 0f);
            foreach (var (n, off, face) in new[] { ("Flak commander, loader post", new Vector3(-2.2f, 0f, -1.6f), 30f), ("Flak ammunition loader", new Vector3(2.3f, 0f, -1.2f), -40f) })
            {
                var sp = p + q * off; sp.y = GroundAt(sp);
                var h = Soldier(n, gun, sp, yaw + face);
                var ai = h ? h.GetComponent<SoldierAI>() : null;
                if (ai) { ai.patrolRadius = 0f; ai.combatRoam = 0f; ai.sleepsUntilAlarm = false; ai.startAlert = true; }
            }
            return true;
        }

        static Health FlakCrewman(string name, Transform seat, Transform carriage)
        {
            var h = Soldier(name, carriage, seat.position, carriage.eulerAngles.y);
            if (!h) return null;
            var go = h.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var ai = go.GetComponent<SoldierAI>(); if (ai) Object.DestroyImmediate(ai);   // he works the gun (FlakCrewSeat), he doesn't walk about
            var holder = go.GetComponentInChildren<WeaponHolder>(true); if (holder) holder.gameObject.SetActive(false);   // rifle stowed
#if STANS_NET
            var nt = go.GetComponent<Unity.Netcode.Components.NetworkTransform>(); if (nt) Object.DestroyImmediate(nt);   // he rides the gun, which each machine turns itself
#endif
            return h;
        }

        // ---------------------------------------------------------------- people

        static void SmeNightGarrison(Transform g, BattlePlan plan)
        {
            // guards over the bucket chain and around the square (from the battle sheet)
            SpawnGermans(plan, "night-garrison", g);
            Sandbags(g, SmeTownHall + new Vector3(-14f, 0f, -4f), 270f);
            // the townspeople of the bucket chain (no weapons, not targets)
            var coat = Mat("SME_Civilian_Coat", new Color(0.25f, 0.23f, 0.22f));
            for (int i = 0; i < 9; i++)
            {
                var p = Vector3.Lerp(SmePump + new Vector3(1f, 0f, -1f), SmeFireHouse + new Vector3(4f, 0f, 6f), i / 8f) + new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f));
                var c = new GameObject($"Townsperson (bucket chain) {i + 1}").transform; c.SetParent(g); c.position = p;
                c.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                if (SmeTownsperson(c, i)) continue;   // Joan's French civilian, rigged (Models/Civilian)
                Prim(PrimitiveType.Capsule, "Body", c, new Vector3(0f, 0.85f, 0f), Vector3.zero, new Vector3(0.5f, 0.85f, 0.4f), coat);
                Prim(PrimitiveType.Sphere, "Head", c, new Vector3(0f, 1.55f, 0f), Vector3.zero, new Vector3(0.23f, 0.25f, 0.23f), Mat("Skin", new Color(0.72f, 0.56f, 0.45f)));
                Prim(PrimitiveType.Cylinder, "Bucket", c, new Vector3(0.3f, 0.8f, 0.2f), Vector3.zero, new Vector3(0.25f, 0.15f, 0.25f), Mat("SME_Iron", new Color(0.1f, 0.1f, 0.1f), 0.4f, 0.8f));
            }
        }

        /// <summary>A townsperson of the bucket chain: the rigged French civilian, a bucket in his right hand.</summary>
        static bool SmeTownsperson(Transform c, int i)
        {
            var model = CivilianModel();
            if (!model) return false;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, c);
            inst.name = "Model";
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
            var mat = TexMat("French_Civilian", $"{CivilianDir}/French_Civilian_BaseColor.jpg", 0.2f);
            var rs = inst.GetComponentsInChildren<Renderer>();
            foreach (var r in rs) { var ms = r.sharedMaterials; for (int k = 0; k < ms.Length; k++) ms[k] = mat; r.sharedMaterials = ms; }
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float tall = 1.66f + (i % 3) * 0.05f;
            if (b.size.y > 0.1f) inst.transform.localScale *= tall / b.size.y;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.position += new Vector3(0f, c.position.y - b.min.y, 0f);
            var anim = inst.GetComponent<Animator>(); if (!anim) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = CivilianController();
            anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var bucket = Prim(PrimitiveType.Cylinder, "Bucket", c, new Vector3(0.3f, 0.5f, 0.2f), Vector3.zero, new Vector3(0.28f, 0.16f, 0.28f), Mat("SME_Iron", new Color(0.1f, 0.1f, 0.1f), 0.4f, 0.8f));
            var col = bucket.GetComponent<Collider>(); if (col) Object.DestroyImmediate(col);
            var held = inst.AddComponent<HeldProp>(); held.prop = bucket.transform;
            return true;
        }

        static void SmeDefenders04(Transform g, BattlePlan plan)
        {
            // at 04:00 most of the garrison had pulled out; a few posts were still held (from the battle sheet)
            SpawnGermans(plan, "defenders-04", g);
            Sandbags(g, SmeTownHall + new Vector3(-14f, 0f, -4f), 270f);
        }

        static void SmeGerman(Transform parent, string name, Vector3 pos, float yaw, SoldierAI.Mode mode, bool awake)
        {
            var h = Soldier(name, parent, pos, yaw);
            if (!h.GetComponent<SoldierAI>()) SetupDefender(h, name.Contains("MG42"));
            var ai = h.GetComponent<SoldierAI>();
            if (!ai) return;
            ai.mode = mode; ai.sleepsUntilAlarm = !awake; ai.startAlert = awake;
            ai.reactionTime = 2f; ai.skill = name.Contains("MG42") ? 0.35f : 0.32f;
            if (!name.Contains("MG42") && Random.value < 0.35f) { ai.smokeGrenade = smokeNb39; ai.smokeGrenades = 1; }
            if (mode == SoldierAI.Mode.Advance) { ai.patrolRadius = 0f; ai.combatRoam = 8f; }
        }

        /// <summary>A German of a battle-sheet group: the group's orders, skill and smoke.</summary>
        static Health SmeGerman(Transform parent, string name, Vector3 pos, float yaw, BattlePlan.Group g)
        {
            var h = Soldier(name, parent, pos, yaw);
            if (!h) return null;
            if (!h.GetComponent<SoldierAI>()) SetupDefender(h, name.Contains("MG42"));
            var ai = h.GetComponent<SoldierAI>();
            if (!ai) return h;
            ai.mode = g.mode; ai.sleepsUntilAlarm = !g.awake; ai.startAlert = g.awake;
            ai.reactionTime = 2f; ai.skill = name.Contains("MG42") ? g.mgSkill : g.skill;
            if (!name.Contains("MG42") && Random.value < g.smokeChance) { ai.smokeGrenade = smokeNb39; ai.smokeGrenades = 1; }
            if (g.mode == SoldierAI.Mode.Advance) { ai.patrolRadius = 0f; ai.combatRoam = g.combatRoam >= 0f ? g.combatRoam : 8f; }
            return h;
        }

        // ---------------------------------------------------------------- Americans

        /// <summary>M1942 jump suit, M1 helmet with its liner and net, reserve 'chute on the chest, musette bag.</summary>
        static void UsBody(Transform t, bool collider = true)
        {
            var suit = Mat("US_M42_JumpSuit", new Color(0.46f, 0.44f, 0.33f));
            var drab = Mat("US_OliveDrab", new Color(0.26f, 0.27f, 0.18f));
            Prim(PrimitiveType.Capsule, "Body", t, new Vector3(0f, 0.9f, 0f), Vector3.zero, new Vector3(0.55f, 0.9f, 0.4f), suit);
            var head = Prim(PrimitiveType.Sphere, "Head", t, new Vector3(0f, 1.62f, 0f), Vector3.zero, new Vector3(0.24f, 0.26f, 0.24f), Mat("Skin", new Color(0.72f, 0.56f, 0.45f)), collider);
            if (collider) head.GetComponent<Collider>().isTrigger = false;
            Prim(PrimitiveType.Sphere, "Helmet M1", t, new Vector3(0f, 1.70f, 0f), Vector3.zero, new Vector3(0.31f, 0.21f, 0.33f), drab);
            Prim(PrimitiveType.Cylinder, "Helmet brim", t, new Vector3(0f, 1.66f, 0f), Vector3.zero, new Vector3(0.33f, 0.01f, 0.35f), drab);
            Prim(PrimitiveType.Cube, "Reserve parachute", t, new Vector3(0f, 0.95f, 0.24f), Vector3.zero, new Vector3(0.36f, 0.24f, 0.12f), drab);
            Prim(PrimitiveType.Cube, "Musette bag", t, new Vector3(0f, 1.05f, -0.26f), Vector3.zero, new Vector3(0.32f, 0.3f, 0.14f), drab);
        }

        const string UsDir = Root + "/Models/US";

        /// <summary>
        /// The rigged soldier (Joan's Mixamo-rigged para, the same skeleton and animations as the British) dressed as
        /// an American paratrooper of the 82nd: the M1942 jump-suit texture (repainted by Claude from the British one),
        /// the M1 helmet with its net and loose chin strap, and the reserve parachute on the chest.
        /// Returns false when the model is not in the project (the blockout is used instead).
        /// </summary>
        static bool AddUsBody(GameObject root)
        {
            if (!AddBritishBody(root)) return false;
            var model = root.transform.Find("Model");
            var anim = model ? model.GetComponent<Animator>() : null;
            // the jump suit
            var m = Mat("US_Para", Color.white, 0.2f);
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{UsDir}/US_Para_BaseColor.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{BritishDir}/British_Para_Normal.png");
            if (albedo) { m.SetTexture("_BaseMap", albedo); m.mainTexture = albedo; }
            if (normal) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            m.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(m);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = m; r.sharedMaterials = ms; }
            if (!anim || !anim.isHuman) return true;
            // M1 helmet. The British Mk II helmet is part of the body mesh: pull it in to the skull (a copy of the mesh
            // for the Americans) so a true-size M1 sits on the head with its rim at the eyebrows and the face shows
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var top = head ? head.Cast<Transform>().FirstOrDefault(t => t.name.Contains("HeadTop")) : null;
            if (head && top) UsHeadMesh(model, head, top);
            var helmetFbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{UsDir}/M1Helmet.fbx");
            if (head && helmetFbx)
            {
                var h = (GameObject)PrefabUtility.InstantiatePrefab(helmetFbx);
                h.name = "M1 helmet";
                foreach (var c in h.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                h.transform.rotation = root.transform.rotation;
                h.transform.localScale *= 0.93f;   // the model with its net is about 7 % over a real M1 (28 x 31 cm with the net)
                float topY = (top ? top.position.y : head.position.y + 0.3f) + 0.04f;
                Vector3 centre = top ? new Vector3((head.position.x + top.position.x) / 2f, 0f, (head.position.z + top.position.z) / 2f) : head.position;
                var hb = UsKitImporter.WorldBounds(h);
                h.transform.position += new Vector3(centre.x - hb.center.x, topY + 0.015f - hb.max.y, centre.z - hb.center.z);
                h.transform.SetParent(head, true);
            }
            // T-5 reserve on the chest, hooked to the harness
            var chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Spine);
            var reserveFbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{UsDir}/ReserveChute.fbx");
            if (chest && reserveFbx)
            {
                var rc = (GameObject)PrefabUtility.InstantiatePrefab(reserveFbx);
                rc.name = "Reserve parachute";
                foreach (var c in rc.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                rc.transform.rotation = root.transform.rotation;
                var b = UsKitImporter.WorldBounds(rc);
                if (b.size.y < b.size.z) rc.transform.rotation = root.transform.rotation * Quaternion.Euler(90f, 0f, 0f);   // thin side towards the front
                b = UsKitImporter.WorldBounds(rc);
                Vector3 front = chest.position + root.transform.forward * 0.17f - Vector3.up * 0.12f;
                rc.transform.position += front - b.center;
                rc.transform.SetParent(chest, true);
            }
            return true;
        }

        /// <summary>
        /// The para body with the British helmet squeezed inside the M1's shell: every vertex more than 9 cm above the
        /// head bone is pulled in towards the head's axis (to 9.5 cm, a skull, below the M1's rim; inside the M1's dome
        /// above it) and kept under its crown. Saved once as Models/US/US_Para_Body.asset.
        /// </summary>
        static void UsHeadMesh(Transform model, Transform head, Transform top)
        {
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (!smr || !smr.sharedMesh) return;
            string path = UsDir + "/US_Para_Body.asset";
            var done = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (done && done.vertexCount == smr.sharedMesh.vertexCount) { smr.sharedMesh = done; return; }
            var src = smr.sharedMesh;
            int ih = System.Array.IndexOf(smr.bones, head), it = System.Array.IndexOf(smr.bones, top);
            if (ih < 0 || it < 0) return;
            var bp = src.bindposes;
            Vector3 H = bp[ih].inverse.MultiplyPoint3x4(Vector3.zero), T = bp[it].inverse.MultiplyPoint3x4(Vector3.zero);
            Vector3 up = (T - H).normalized;
            float k = Vector3.Distance(head.position, top.position) / Vector3.Distance(H, T);   // mesh units -> metres in the game
            var m = Object.Instantiate(src); m.name = "US_Para_Body";
            var v = m.vertices;
            const float rimH = 0.178f, crownH = 0.345f;
            for (int i = 0; i < v.Length; i++)
            {
                var d = v[i] - H; float h = Vector3.Dot(d, up) * k;
                if (h < 0.09f) continue;
                var radial = d - up * Vector3.Dot(d, up); float r = radial.magnitude * k;
                float rmax = h < rimH ? 0.095f : 0.125f * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((h - rimH) / 0.17f, 2f)));
                rmax = Mathf.Max(rmax, 0.02f);
                if (r > rmax) radial *= rmax / r;
                h = Mathf.Min(h, crownH);
                v[i] = H + up * (h / k) + radial;
            }
            m.vertices = v; m.RecalculateBounds();
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
            smr.sharedMesh = m;
        }

        static GameObject BuildUsPrefab(string name, string gun, float dmg, float rpm, bool auto, int mag)
        {
            var go = new GameObject(name);
            var cc = go.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            var h = go.AddComponent<Health>(); h.faction = Faction.British;   // = the Allies in the shared combat code
            var w = AddWeapon(go.transform, gun, dmg, rpm, auto, mag);
            var ai = go.AddComponent<SoldierAI>(); ai.weapon = w; ai.skill = 0.55f;
            ai.smokeGrenade = smokeM8; ai.smokeGrenades = 1;
            go.AddComponent<Ragdoll>();
            if (AddUsBody(go)) MountWeapons(go, w);
            else UsBody(go.transform);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static SoldierController BuildUsPlayer(Transform parent, Shell grenade)
        {
            var go = new GameObject("Player - F Company, 2/505 PIR");
            go.transform.SetParent(parent);
            go.transform.position = SmeRally + Vector3.up * 0.2f;
            var cc = go.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            var h = go.AddComponent<Health>(); h.faction = Faction.British; h.maxHealth = 200f; h.regenPerSecond = 5f;
            // M1 rifle, the .45, two Mk II grenades, an AN-M8 smoke grenade and the first-aid packet
            var rifle = AddWeapon(go.transform, "M1 Garand rifle", 70f, 120f, false, 8);
            var colt = Pistol(go.transform, "M1911", 40f);
            var pc = go.AddComponent<SoldierController>();
            pc.weapons = new[] { rifle, colt };
            pc.cam = Camera.main;
            var kit = go.AddComponent<Loadout>();
            kit.primary = rifle; kit.sidearm = colt;
            kit.grenades = new List<Shell> { grenade, grenade };
            kit.smokes = new List<Shell> { smokeM8 };
            kit.dressings = 1;
            go.AddComponent<VehicleEntry>();   // lorries and the Kübelwagen: E to drive or climb aboard
            go.AddComponent<Ragdoll>();
            if (AddUsBody(go)) { pc.animator = go.GetComponentInChildren<Animator>(); kit.weaponMount = MountWeapons(go, rifle, colt); }
            else UsBody(go.transform);
            return pc;
        }

        static GameObject SmeC47(string name, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent);
            var od = Mat("C47_OliveDrab", new Color(0.24f, 0.25f, 0.18f), 0.25f, 0.2f);
            var grey = Mat("C47_NeutralGrey", new Color(0.42f, 0.43f, 0.42f), 0.25f, 0.2f);
            var white = Mat("C47_Stripe_White", new Color(0.85f, 0.85f, 0.82f)); var black = Mat("C47_Stripe_Black", new Color(0.04f, 0.04f, 0.04f));
            var t = go.transform;
            var c47 = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/StansGame/Models/Vehicles/C47.fbx");
            if (c47)
            {
                // Joan's C-47 (with invasion stripes), prepared in Blender: 29 m span, NoseMarker at the nose
                var m = (GameObject)PrefabUtility.InstantiatePrefab(c47, t);
                m.name = "C-47 model"; m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.identity;
                foreach (var c in m.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                PrefabUtility.UnpackPrefabInstance(m, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var nose = UsKitImporter.TakeMarker(m, "NoseMarker");
                if (nose.HasValue) UsKitImporter.FaceMarkerForward(m, nose.Value);
                var bb = UsKitImporter.WorldBounds(m);
                m.transform.position -= bb.center - t.position;
                // the propellers: the model has each blade and the spinner as parts of their own; group them round the
                // spinner (the shaft) so PropellerSpin can turn them (port: Cylinder03-06, starboard: Cylinder46-49)
                C47Prop(m.transform, "Prop_Port", "Cylinder03-FACES", "Cylinder04-FACES", "Cylinder05-FACES", "Cylinder06-FACES");
                C47Prop(m.transform, "Prop_Stbd", "Cylinder49-FACES", "Cylinder46-FACES", "Cylinder47-FACES", "Cylinder48-FACES");
                go.AddComponent<PropellerSpin>();
                SmeC47Door(t, go);
                return go;
            }
            Prim(PrimitiveType.Capsule, "Fuselage", t, Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(2.6f, 9.8f, 2.6f), od);
            Prim(PrimitiveType.Cube, "Belly", t, new Vector3(0f, -0.7f, 0f), Vector3.zero, new Vector3(2.2f, 0.6f, 15f), grey);
            Prim(PrimitiveType.Cube, "Wing", t, new Vector3(0f, -0.7f, 1.8f), Vector3.zero, new Vector3(29f, 0.45f, 4.2f), od);
            Prim(PrimitiveType.Cube, "Tailplane", t, new Vector3(0f, 0.3f, -8.6f), Vector3.zero, new Vector3(10.5f, 0.25f, 2.4f), od);
            Prim(PrimitiveType.Cube, "Fin", t, new Vector3(0f, 2.1f, -8.9f), Vector3.zero, new Vector3(0.3f, 3.6f, 2.8f), od);
            foreach (float s in new[] { -1f, 1f })
            {
                Prim(PrimitiveType.Cylinder, "Engine nacelle", t, new Vector3(s * 5.4f, -0.5f, 3.4f), new Vector3(90f, 0f, 0f), new Vector3(1.4f, 2.2f, 1.4f), od);
                Prim(PrimitiveType.Cylinder, "Propeller disc", t, new Vector3(s * 5.4f, -0.5f, 5.7f), new Vector3(90f, 0f, 0f), new Vector3(3.6f, 0.02f, 3.6f), Mat("C47_Prop", new Color(0.05f, 0.05f, 0.05f)));
                // invasion stripes: three white, two black, on each wing and round the rear fuselage
                for (int k = 0; k < 5; k++)
                    Prim(PrimitiveType.Cube, "Invasion stripe", t, new Vector3(s * (8.2f + k * 0.6f), -0.46f, 1.8f), Vector3.zero, new Vector3(0.6f, 0.05f, 4.25f), k % 2 == 0 ? white : black);
            }
            for (int k = 0; k < 5; k++)
                Prim(PrimitiveType.Cylinder, "Invasion stripe (fuselage)", t, new Vector3(0f, 0f, -5.4f - k * 0.6f), new Vector3(90f, 0f, 0f), new Vector3(2.62f, 0.3f, 2.62f), k % 2 == 0 ? white : black);
            Prim(PrimitiveType.Cube, "Cockpit glazing", t, new Vector3(0f, 0.85f, 8.2f), new Vector3(20f, 0f, 0f), new Vector3(1.4f, 0.5f, 1.2f), Mat("Window_Dark", new Color(0.05f, 0.05f, 0.06f), 0.8f));
            SmeC47Door(t, go);
            return go;
        }

        /// <summary>One propeller of the C-47: the spinner (first name) and its three blades under a pivot on the shaft.</summary>
        static void C47Prop(Transform model, string name, string spinner, params string[] blades)
        {
            Transform Find(string n) { foreach (var x in model.GetComponentsInChildren<Transform>(true)) if (x.name.StartsWith(n)) return x; return null; }
            var hub = Find(spinner);
            if (!hub || !hub.GetComponent<Renderer>()) { Debug.LogWarning($"Stan's Game: C-47 {name}: no {spinner} in the model - the propeller won't turn"); return; }
            var pivot = new GameObject(name).transform;
            pivot.SetParent(hub.parent, false);
            pivot.position = hub.GetComponent<Renderer>().bounds.center;
            pivot.rotation = hub.parent.rotation;
            hub.SetParent(pivot, true);
            foreach (var b in blades) { var x = Find(b); if (x) x.SetParent(pivot, true); }
        }

        /// <summary>The cargo door on the port side aft, the red/green jump light by it, and the flight script.</summary>
        static void SmeC47Door(Transform t, GameObject go)
        {
            var door = new GameObject("Cargo door (port, aft)").transform; door.SetParent(t, false); door.localPosition = new Vector3(-1.35f, 0.1f, -4.6f);
            var lamp = new GameObject("Jump light").AddComponent<Light>();
            lamp.transform.SetParent(t, false); lamp.transform.localPosition = new Vector3(-0.9f, 0.9f, -4.2f);
            lamp.type = LightType.Point; lamp.range = 3f; lamp.intensity = 1.2f; lamp.color = Color.red;
            var f = go.AddComponent<C47Flight>(); f.door = door; f.jumpLight = lamp;
        }

        /// <summary>The T-5 canopy (amaraklov's US Army parachute, CC BY) as a prefab for ParachuteDrop; null without the model.</summary>
        static GameObject SmeCanopyPrefab()
        {
            string path = Root + "/Models/US/T5Canopy.fbx";
            // CanopyCloth moves the canopy's vertices at run time (breathing, the silk collapse): the mesh must stay readable in builds
            if (AssetImporter.GetAtPath(path) is ModelImporter imp && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!model) return null;
            var root = new GameObject("T5_Canopy");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // the canopy is a single sheet: seen from underneath (the player's view) it must render both sides
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    if (!ms[i]) continue;
                    var two = Mat("T5_Canopy_TwoSided", Color.white, 0.1f);
                    two.CopyPropertiesFromMaterial(ms[i]);
                    two.SetFloat("_Cull", 0f); two.doubleSidedGI = true;
                    EditorUtility.SetDirty(two);
                    ms[i] = two;
                }
                r.sharedMaterials = ms;
            }
            // keep the importer's axis conversion (Blender Z-up -> Unity Y-up); resetting it to identity laid the canopy on its side
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = model.transform.localRotation;
            var b = UsKitImporter.WorldBounds(inst);
            inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);   // the risers at the origin, canopy above
            root.AddComponent<CanopyCloth>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/T5_Canopy.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>The Mk II fragmentation grenade ("pineapple"), with Joan's model when it has been imported.</summary>
        static Shell BuildMk2Grenade()
        {
            var shell = BuildGrenade("Grenade_MkII", "Mk II grenade", 4.5f, 160f, 9f, false);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/StansGame/Models/Weapons/MkIIGrenade.fbx");
            if (!model) return shell;
            string path = $"{PrefabDir}/Grenade_MkII.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            foreach (Transform c in root.transform.Cast<Transform>().ToList()) Object.DestroyImmediate(c.gameObject);   // the Mills bomb stand-in
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var b = UsKitImporter.WorldBounds(inst);
            inst.transform.position -= b.center - root.transform.position;
            foreach (var c in root.GetComponents<CapsuleCollider>()) { c.radius = 0.03f; c.height = 0.114f; c.direction = 1; c.center = Vector3.zero; }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            // the pick-up visual
            var vis = new GameObject("Grenade_MkII_Pickup");
            var v = (GameObject)PrefabUtility.InstantiatePrefab(model, vis.transform);
            v.transform.localPosition = -UsKitImporter.WorldBounds(v).center;
            var pick = PrefabUtility.SaveAsPrefabAsset(vis, $"{PrefabDir}/Grenade_MkII_Pickup.prefab");
            Object.DestroyImmediate(vis);
            shell = AssetDatabase.LoadAssetAtPath<Shell>(path);
            if (shell) { shell.pickupVisual = pick; EditorUtility.SetDirty(shell); AssetDatabase.SaveAssets(); }
            return shell;
        }
    }
}
