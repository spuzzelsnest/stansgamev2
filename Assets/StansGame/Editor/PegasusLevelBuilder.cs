using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if STANS_NET
using Unity.Netcode;
using Unity.Netcode.Components;
#endif

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Build Pegasus Bridge Level.
    /// Generates the night of 5/6 June 1944 at the Bénouville bridge in a new scene.
    /// Real-world scale (1 unit = 1 m). World axes: +X = east bank (Ranville), -X = west bank (Bénouville),
    /// +Z = up the canal towards Ouistreham and the sea (true bearing 28 deg). The canal runs along z through x = 0.
    /// If Assets/StansGame/Terrain/PegasusHeights.r16/.json exist, the ground is the real IGN LiDAR HD terrain.
    /// Everything that is not a real model yet is a primitive blockout that can be swapped later.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string Root = "Assets/StansGame";
        const string MatDir = Root + "/Materials";
        const string PrefabDir = Root + "/Prefabs";
        const string SceneDir = Root + "/Scenes";
        const string BridgeModel = Root + "/Models/PegasusBridge1934.fbx";   // built by Models/Source~/bridge1934.py

        // layout (metres)
        const float CanalHalfWidth = 21f;       // reference half width of the open canal (towpaths, gliders, defences are laid out from it)
        const float WaterLevel = -1.8f;          // LiDAR: quays ~5.4 m, canal water 3.6 m (IGN69); photos show a low quay
        const float CanalDepth = -6f;
        // the 1934 bridge as it was in June 1944: ~31 m long, ~7 m wide (it was lengthened by 5 m in the 1960s and
        // replaced in 1994 by a larger copy). The lifting span crosses the canal from its seat on a concrete pier off the
        // west quay (a short fixed span joins the pier to the quay) to the rolling segment on the east quay; the counterweight tail reaches back over the east bank.
        const float BridgeWestEnd = 12.2f, BridgeEastEnd = 21.4f;   // east: the back of the counterweight pit
        const float RoadWidth = 6.5f;
        // Roads as on the RAF vertical of June 1944 ("aerial photo bridge June 1944", scaled by the canal at the bridge),
        // joined to the roads still in the IGN LiDAR further out. West: the Bénouville road runs straight west from the
        // bridge to the north-south road (the LiDAR's junction ~260 m out); east: the Ranville road leaves the bridge
        // heading about 10 degrees south of east, then runs on to Horsa Bridge on the Orne; along the east bank a road
        // runs south from the Ranville road close to the canal, with a track branching off to the landing field.
        static readonly Vector3 BenouvilleBend = new Vector3(-140f, 0f, 4f);       // (just a point on the straight road)
        static readonly Vector3 BenouvilleCrossroads = new Vector3(-260f, 0f, 12f);
        static readonly Vector2[] RanvilleRoad = { new Vector2(BridgeEastEnd - 1f, -1.3f), new Vector2(88f, -15.6f), new Vector2(200f, -18f), new Vector2(560f, 0f), new Vector2(720f, 0f) };
        static readonly Vector2[] EastBankRoadSouth = { new Vector2(26.5f, -3f), new Vector2(20f, -45f), new Vector2(18f, -85f), new Vector2(30f, -150f), new Vector2(30f, -560f) };
        static readonly Vector2[] EastBankRoadNorth = { new Vector2(28.5f, -3f), new Vector2(30f, 40f), new Vector2(30f, 560f) };
        static readonly Vector2[] GliderTrack = { new Vector2(28f, -5f), new Vector2(31f, -11f), new Vector2(37.4f, -33f) };   // to where glider 91 ended up
        const float WestBankRoadX = -22.5f;   // the west-bank road runs right along the canal, in front of the café
        const float TowpathRoadOffset = 9f;    // tree-lined roads run along both banks

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        // ---- real ground from IGN LiDAR HD (see Assets/StansGame/Terrain/PegasusHeights.json), converted by the
        // terrain pipeline into the game frame: origin = canal centre at the bridge, +Z up the canal (bearing 28 deg).
        static string heightsName = "PegasusHeights", terrainAssetName = "PegasusTerrain";   // the Bénouville level swaps these
        static string HeightsRaw => Root + $"/Terrain/{heightsName}.r16";
        static string HeightsMeta => Root + $"/Terrain/{heightsName}.json";
        [System.Serializable] class OrnePoint { public float x, z, halfWidth, bedY; }
        [System.Serializable] class HeightMeta { public int resolution; public float size, baseY, height, zref, coverage; public string source; public OrnePoint[] orne; }
        static HeightMeta real;            // null = no LiDAR data, use the procedural ground
        static ushort[] realRaw;
        static float TerrainSize = 1200f;
        static Terrain terrainRef;
        static Transform bridgeRef;
        static readonly HashSet<Transform> pureGroups = new HashSet<Transform>();
        const float MarshWaterY = -3.6f;  // tidal Orne, the LZ pond and ditches all lie below this (IGN69 ~1.8 m)

        static bool LoadRealHeights()
        {
            real = null; realRaw = null; TerrainSize = 1200f; smeRaw = null; smeMeta = null;
            string raw = Path.Combine(Path.GetDirectoryName(Application.dataPath), HeightsRaw);
            string meta = Path.Combine(Path.GetDirectoryName(Application.dataPath), HeightsMeta);
            if (!File.Exists(raw) || !File.Exists(meta)) return false;
            real = JsonUtility.FromJson<HeightMeta>(File.ReadAllText(meta));
            var bytes = File.ReadAllBytes(raw);
            if (bytes.Length != real.resolution * real.resolution * 2) { Debug.LogError("Stan's Game: PegasusHeights.r16 has the wrong size"); real = null; return false; }
            realRaw = new ushort[real.resolution * real.resolution];
            System.Buffer.BlockCopy(bytes, 0, realRaw, 0, bytes.Length);
            TerrainSize = real.size;
            Debug.Log($"Stan's Game: using LiDAR ground ({real.resolution}^2 over {real.size} m, {real.coverage:P0} measured). {real.source}");
            return true;
        }

        /// <summary>Real ground height (world y) at a game-space point, bilinear.</summary>
        static float RealY(float wx, float wz)
        {
            int n = real.resolution; float half = real.size * 0.5f;
            float fx = Mathf.Clamp((wx + half) / real.size * (n - 1), 0f, n - 1.001f);
            float fz = Mathf.Clamp((wz + half) / real.size * (n - 1), 0f, n - 1.001f);
            int ix = (int)fx, iz = (int)fz; float tx = fx - ix, tz = fz - iz;
            float a = realRaw[iz * n + ix], b = realRaw[iz * n + ix + 1], c = realRaw[(iz + 1) * n + ix], d = realRaw[(iz + 1) * n + ix + 1];
            float u = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz) / 65535f;
            return real.baseY + u * real.height;
        }

        /// <summary>Distance (m) from the bank of the River Orne; negative = in the river.</summary>
        static float OrneDistance(float x, float z)
        {
            if (real == null || real.orne == null || real.orne.Length < 2)
                return Mathf.Abs(x - OrneX - Mathf.Sin(z * 0.006f) * 25f) - OrneHalfWidth;
            float best = float.MaxValue; var p = new Vector2(x, z);
            for (int i = 0; i < real.orne.Length - 1; i++)
            {
                var a = real.orne[i]; var b = real.orne[i + 1];
                var s = new Seg(new Vector2(a.x, a.z), new Vector2(b.x, b.z), (a.halfWidth + b.halfWidth) * 0.5f);
                best = Mathf.Min(best, SegDist(p, s));
            }
            return best;
        }

        static float OrneCentreX(float z)
        {
            if (real == null || real.orne == null || real.orne.Length < 2) return OrneX + Mathf.Sin(z * 0.006f) * 25f;
            for (int i = 0; i < real.orne.Length - 1; i++)
                if (z >= real.orne[i].z && z <= real.orne[i + 1].z)
                    return Mathf.Lerp(real.orne[i].x, real.orne[i + 1].x, Mathf.InverseLerp(real.orne[i].z, real.orne[i + 1].z, z));
            return real.orne[0].x;
        }

        /// <summary>
        /// Everything was laid out on flat ground at y = 0. Lift each placed object (not the pure grouping nodes)
        /// by the height of the ground under it: the terrain, or the bridge deck for the sentry standing on it.
        /// Houses take the lowest corner so they never float on a slope.
        /// </summary>
        static void SnapToGround(Transform t)
        {
            foreach (Transform c in t)
            {
                if (pureGroups.Contains(c)) { SnapToGround(c); continue; }
                bool building = c.name.StartsWith("House") || c.name.StartsWith("Café") || c.name.StartsWith("Bénouville");
                float y = GroundAt(c.position);
                if (building)
                    foreach (var o in new[] { new Vector3(4f, 0f, 0f), new Vector3(-4f, 0f, 0f), new Vector3(0f, 0f, 4f), new Vector3(0f, 0f, -4f) })
                        y = Mathf.Min(y, GroundAt(c.position + o));
                c.position += Vector3.up * (building ? y - 0.3f : y);
            }
        }

        static float GroundAt(Vector3 p)
        {
            if (smeRaw != null) return SmeY(p.x, p.z);   // Sainte-Mere-Eglise: straight from the height file
            // read the heightmap directly: Terrain.SampleHeight returns 0 on a terrain created in the same editor call
            var td = terrainRef.terrainData; var tp = terrainRef.transform.position;
            float best = td.GetInterpolatedHeight((p.x - tp.x) / td.size.x, (p.z - tp.z) / td.size.z) + tp.y;
            if (bridgeRef)
                foreach (var h in Physics.RaycastAll(new Vector3(p.x, 60f, p.z), Vector3.down, 120f))
                    if (h.collider.transform.IsChildOf(bridgeRef) && h.normal.y > 0.8f && h.point.y < 1.5f && h.point.y > best) best = h.point.y;   // the deck, not the tower
            return best;
        }

        [MenuItem("Stan's Game/Build Pegasus Bridge Level", priority = 1)]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog("Build Pegasus Bridge level",
                "Creates a new scene Assets/StansGame/Scenes/PegasusBridge.unity (overwrites it if it exists). Continue?", "Build", "Cancel"))
                return;

            EnsureFolders();
            PropTextureFixer.Fix();
            mats.Clear();
            pureGroups.Clear();
            heightsName = "PegasusHeights"; terrainAssetName = "PegasusTerrain"; extraRoads = null;
            LoadRealHeights();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("PegasusBridge_1944").transform;
            var env = Group("Environment", root);
            var defences = Group("German Defences", root);
            var lz = Group("LZ X - Glider Landing Zone", root);
            var actors = Group("Actors", root);

            BuildLighting(env);
            BuildTerrain(env);
            BuildCanal(env);
            var bridgeEnd = BuildBridge(env);
            BuildRoads(env);
            BuildBuildings(env);
            BuildTrees(env);
            PegasusBatch5(env);
            BuildPhotoDetails(env);
            BuildGardens(env);

            BuildGrenadePrefabs();
            var defenders = BuildDefences(defences);
            PegasusDownloadProps(defences, env);
            CafeOwnerGondree(env);
            PegasusBatch6(defences);
            var gliders = BuildGliders(lz);
            PegasusGliderCargo(lz, gliders);
            BuildSupplies(actors);
            var britishPrefab = BuildSoldierPrefab("BritishSoldier", Faction.British, new Color(0.42f, 0.40f, 0.28f), "Sten Mk II", 34f, 500f, true, 32);
            var britishBren = BuildSoldierPrefab("BritishBrenGunner", Faction.British, new Color(0.42f, 0.40f, 0.28f), "Bren Mk II", 45f, 520f, true, 30);
            var britishRifle = BuildSoldierPrefab("BritishRifleman", Faction.British, new Color(0.42f, 0.40f, 0.28f), "Lee-Enfield", 70f, 30f, false, 10);
            // the officer in the maroon beret of the Parachute Regiment leads the 7th Para Battalion relief
            var paraOfficer = BuildSoldierPrefab("BritishParaOfficer", Faction.British, new Color(0.42f, 0.40f, 0.28f), "Sten Mk II", 34f, 500f, true, 32, true);
            var germanPrefab = BuildSoldierPrefab("GermanSoldier", Faction.German, new Color(0.38f, 0.40f, 0.37f), "Kar98k", 75f, 30f, false, 5);
            PegasusOrneElement(lz, defences, actors, britishRifle, britishBren, britishPrefab);   // Euston II: the Orne gliders and the river bridge guard (PegasusLevelBuilder.Orne.cs)
            foreach (var d in defenders.all) if (!d.GetComponent<SoldierAI>()) SetupDefender(d, d.name.Contains("MG42"));
            GiveGermansSmoke(defenders.all);

#if STANS_NET
            SoldierController player = null;                       // multiplayer: each machine's soldier is spawned at run time
            var netPlayerPrefab = BuildNetPlayerPrefab();
#else
            var player = BuildPlayer(actors);
#endif
            var tankGroup = BuildTankAttack(actors);
            var paras = BuildParaReinforcements(actors, britishPrefab, paraOfficer);
            var waves = BuildAttackWaves(actors);
            var snipers = BuildSnipers(actors);

            // mission
            var director = new GameObject("MissionDirector").AddComponent<MissionDirector>();
            director.transform.SetParent(root);
            director.player = player;
#if STANS_NET
            // shared mission state and combat events travel on the MissionDirector's network object
            director.gameObject.AddComponent<NetworkObject>().AutoObjectParentSync = false;
            director.gameObject.AddComponent<MissionNet>();
            var combat = director.gameObject.AddComponent<NetCombat>();
            combat.projectilePrefabs = new[]
            {
                AssetDatabase.LoadAssetAtPath<Shell>($"{PrefabDir}/PIAT_Bomb.prefab"),
                AssetDatabase.LoadAssetAtPath<Shell>($"{PrefabDir}/KwK40_HE.prefab"),
                millsBomb, stickGrenade, pak38Shell, smokeNo77, smokeNb39, smokeM8, gammonBomb,
            };
            var netGo = new GameObject("NetworkManager");
            var session = netGo.AddComponent<NetSession>();       // brings NetworkManager + UnityTransport with it
            var nm = netGo.GetComponent<NetworkManager>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = netGo.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            nm.NetworkConfig.EnableSceneManagement = true;
            session.playerPrefab = netPlayerPrefab;
            session.playerPrefabUS = AssetDatabase.LoadAssetAtPath<GameObject>(UsNetPlayerPath);   // Sainte-Mère-Église co-op (build that level first)
            session.networkPrefabs = new[] { britishPrefab, britishBren, britishRifle };
#endif
            director.gliders = gliders.ToArray();
            director.gliderSectionSpawns = new Transform[gliders.Count];
            for (int i = 0; i < gliders.Count; i++)
            {
                var spawn = new GameObject($"Glider{i + 1}_Exit").transform;
                // the port-side door near the front: the section climbs out there (follows the glider's final position)
                spawn.SetParent(gliders[i].transform);
                spawn.localPosition = new Vector3(-3.4f, 0.9f, 3.5f);
                spawn.localRotation = Quaternion.identity;
                director.gliderSectionSpawns[i] = spawn;
            }
            director.britishSoldierPrefab = britishPrefab;
            director.britishBrenPrefab = britishBren;
            director.britishRiflePrefab = britishRifle;
            director.bridgeObjective = bridgeEnd;
            director.eastBankDefenders = defenders.eastBank;
            director.tankAttackGroup = tankGroup.gameObject;
            director.panzer = tankGroup.GetComponentInChildren<Health>(true);
            director.paraReinforcements = paras;
            director.attackWaves = waves;
            director.snipers = snipers;

            if (real != null)
            {
                // the layout was made on flat ground; set every object down on the real LiDAR ground
                Physics.SyncTransforms();
                SnapToGround(env.Find("Buildings"));
                SnapToGround(env.Find("Poplars and hedges"));
                SnapToGround(defences);
                SnapToGround(lz);
                SnapToGround(actors);
                var td0 = terrainRef.terrainData;
                Debug.Log($"Stan's Game: terrain {td0.heightmapResolution} res, size {td0.size}, at {terrainRef.transform.position}; " +
                    $"LiDAR y at café {RealY(-38f, -12.5f):F2}, terrain y there {GroundAt(new Vector3(-38f, 0f, -12.5f)):F2}, at plateau (-300,-100) {RealY(-300f, -100f):F2} / {GroundAt(new Vector3(-300f, 0f, -100f)):F2}");
                var bld = env.Find("Buildings");
                Debug.Log($"Stan's Game: buildings group {(bld ? bld.childCount.ToString() : "NOT FOUND")}");
                if (bld) foreach (Transform o in bld) if (o.name.StartsWith("Caf") || o.name.StartsWith("House by")) Debug.Log($"Stan's Game: {o.name} on the ground at y = {o.position.y:F2}");
            }

            if (real != null) RescueFromWater(root);
            PegasusAmbientLife(root, env, defences, actors);   // colour grade, patrols, traffic, windows (PegasusLevelBuilder.Ambient.cs)
            BuildCinematics(root, director, env);
            PegasusHistory(root);   // "The History" film from the main menu (PegasusLevelBuilder.History.cs)
            AdoptBattle("PegasusBridge", root, Faction.British, PegasusCommanders);   // battle sheet + commanders (PegasusLevelBuilder.Battles.cs)

            string path = SceneDir + "/PegasusBridge.unity";
            EditorSceneManager.SaveScene(scene, path);
#if STANS_NET
            // network ids can only be worked out once prefabs and scene objects exist on disk: do it now and save again
            foreach (var p in new[] { "BritishSoldier", "BritishBrenGunner", "BritishRifleman", "BritishParaOfficer", "NetPlayer" }) RefreshPrefabNetworkId($"{PrefabDir}/{p}.prefab");
            int n = 0;
            foreach (var no in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { RefreshNetworkId(no); n++; }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"Stan's Game: {n} networked objects in the level");
#endif
            AddSceneToBuild(path);
            Debug.Log("Stan's Game: Pegasus Bridge level built at " + path);
            BattleValidator.Validate(false);
        }

        // ---------------------------------------------------------------- helpers

        static void EnsureFolders()
        {
            foreach (var d in new[] { Root, MatDir, PrefabDir, SceneDir, Root + "/Models" })
                if (!AssetDatabase.IsValidFolder(d))
                {
                    var parent = Path.GetDirectoryName(d).Replace('\\', '/');
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(d));
                }
        }

        static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform; t.SetParent(parent); pureGroups.Add(t); return t;
        }

        static Material Mat(string name, Color c, float smooth = 0.15f, float metal = 0f)
        {
            if (mats.TryGetValue(name, out var m)) return m;
            string path = $"{MatDir}/{name}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c); m.color = c;
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m, float yaw = 0f, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic);
            return go;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localEuler, Vector3 scale, Material m, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos; go.transform.localEulerAngles = localEuler; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        // ---------------------------------------------------------------- world

        /// <summary>
        /// The moon and the night sky as they were (PyEphem, Models/Source~/night_sky_1944.py): at Pegasus Bridge at 00:16
        /// (BDST) the moon, 99% full, stood in Scorpius at azimuth 164 deg, 22 deg up; at Sainte-Mère-Église at 01:30 it
        /// was due south, 23 deg up. Broken moonlit cloud drifts in on the north-west wind (StansGame/NightSky skybox,
        /// driven by NightSky.cs, which also dims the moonlight while a cloud crosses the moon).
        /// </summary>
        static void BuildLighting(Transform env, string skyName = "Pegasus", float moonAz = 164.2f, float moonAlt = 21.6f)
        {
            var moon = new GameObject("Moon (late night, 6 June 1944)").AddComponent<Light>();
            moon.transform.SetParent(env);
            moon.type = LightType.Directional;
            moon.color = new Color(0.58f, 0.66f, 0.92f);
            moon.intensity = 0.35f;
            moon.shadows = LightShadows.Soft;
            // shining from the moon's real position: azimuth from north (+Z) towards east (+X), altitude above the horizon
            moon.transform.rotation = Quaternion.Euler(moonAlt, moonAz + 180f, 0f);
            RenderSettings.sun = moon;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.08f, 0.12f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.009f;
            RenderSettings.fogColor = new Color(0.06f, 0.07f, 0.10f);
            var sky = NightSkyMaterial(skyName);
            RenderSettings.skybox = sky;
            if (sky)
            {
                var ns = moon.gameObject.AddComponent<NightSky>();
                ns.moon = moon; ns.sky = sky;
                ns.clouds = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/Sky/Clouds_Broken.png");
            }

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(env);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = sky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 1500f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(60f, 25f, -80f);
            camGo.transform.LookAt(Vector3.zero);
        }

        /// <summary>The skybox material for one place and hour (Textures/Sky/NightSky_*.png, Moon_1944.png, Clouds_Broken.png).</summary>
        static Material NightSkyMaterial(string skyName)
        {
            string dir = $"{Root}/Textures/Sky";
            var shader = Shader.Find("StansGame/NightSky");
            var pano = SkyTexture($"{dir}/NightSky_{skyName}.png", TextureWrapMode.Repeat, false, true);
            var moonTex = SkyTexture($"{dir}/Moon_1944.png", TextureWrapMode.Clamp, false, true);
            var clouds = SkyTexture($"{dir}/Clouds_Broken.png", TextureWrapMode.Repeat, true, false);
            if (!shader || !pano) { Debug.LogWarning("Stan's Game: night sky shader or textures missing - plain dark sky"); return null; }
            string path = $"{Root}/Materials/NightSky_{skyName}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_Panorama", pano); m.SetTexture("_MoonTex", moonTex); m.SetTexture("_CloudTex", clouds);
            // broken stratocumulus, about half the sky, drifting in from the north-west; the moon a little larger than life
            m.SetFloat("_Coverage", 0.5f); m.SetFloat("_CloudScale", 0.4f); m.SetVector("_Wind", new Vector4(-0.0045f, 0.0032f, 0f, 0f));
            m.SetColor("_CloudDark", new Color(0.05f, 0.06f, 0.085f)); m.SetColor("_CloudLit", new Color(0.20f, 0.22f, 0.27f));
            m.SetFloat("_MoonSize", 0.42f); m.SetFloat("_StarBright", 1.3f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D SkyTexture(string path, TextureWrapMode wrap, bool readable, bool srgb)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                bool dirty = ti.mipmapEnabled || ti.wrapMode != wrap || ti.isReadable != readable || ti.sRGBTexture != srgb || ti.maxTextureSize < 4096
                             || ti.textureCompression != TextureImporterCompression.CompressedHQ;
                if (dirty)
                {
                    ti.mipmapEnabled = false; ti.wrapModeU = wrap; ti.wrapModeV = path.Contains("NightSky_") ? TextureWrapMode.Clamp : wrap;
                    ti.isReadable = readable; ti.sRGBTexture = srgb; ti.maxTextureSize = 4096;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.alphaIsTransparency = false;
                    ti.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------------------------------------------------------- ground, grass and water

        const float OrneX = 430f;            // River Orne, east of the canal (Horsa Bridge on the Ranville road)
        const float OrneHalfWidth = 22f;

        struct Seg { public Vector2 a, b; public float halfWidth; public Seg(Vector2 a, Vector2 b, float hw) { this.a = a; this.b = b; halfWidth = hw; } }

        static Seg[] RoadSegments()
        {
            Vector2 bend = new Vector2(BenouvilleBend.x, BenouvilleBend.z), cross = new Vector2(BenouvilleCrossroads.x, BenouvilleCrossroads.z);
            var segs = new List<Seg>();
            void Line(Vector2[] pts, float hw) { for (int i = 0; i < pts.Length - 1; i++) segs.Add(new Seg(pts[i], pts[i + 1], hw)); }
            Line(RanvilleRoad, RoadWidth * 0.5f);                                                                  // to Ranville / Horsa Bridge
            Line(new[] { new Vector2(-Water.WestQuay, 0f), bend, cross, new Vector2(-330f, 12f), new Vector2(-720f, -2f) }, RoadWidth * 0.5f);   // to Bénouville and on
            segs.Add(new Seg(cross + new Vector2(0f, -320f), cross + new Vector2(0f, 300f), RoadWidth * 0.5f));     // N-S road
            segs.Add(new Seg(new Vector2(WestBankRoadX, -560f), new Vector2(WestBankRoadX, 560f), 2.25f));            // west-bank road
            Line(EastBankRoadSouth, 2.5f);                                                                         // east-bank roads
            Line(EastBankRoadNorth, 2.5f);
            Line(GliderTrack, 1.4f);                                                                               // the track to the field
            if (extraRoads != null) foreach (var r in extraRoads) Line(r.pts, r.hw);                               // the Bénouville level: the Caen road south, the château drive
            return segs.ToArray();
        }

        static float SegDist(Vector2 p, Seg s)
        {
            Vector2 ab = s.b - s.a; float t = Mathf.Clamp01(Vector2.Dot(p - s.a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, s.a + ab * t) - s.halfWidth;
        }

        /// <summary>Distance (m) from the edge of the nearest road; negative = on the road.</summary>
        static float RoadDistance(float x, float z, Seg[] segs)
        {
            var p = new Vector2(x, z); float d = float.MaxValue;
            foreach (var s in segs) d = Mathf.Min(d, SegDist(p, s));
            return d;
        }

        static bool InWater(float x, float z) =>
            Mathf.Abs(x) < Water.CanalShore(z, Mathf.Sign(x)) + 1.5f || OrneDistance(x, z) < 1.5f
            || (real != null && RealY(x, z) < MarshWaterY + 0.3f);

        static void BuildTerrain(Transform env)
        {
            int res = real != null ? real.resolution : 513; float size = TerrainSize;
            float height = real != null ? real.height : 40f; float baseY = real != null ? real.baseY : -20f;
            var segs = RoadSegments();
            var data = new TerrainData { heightmapResolution = res, size = new Vector3(size, height, size) };
            // save the asset first: alphamaps painted before CreateAsset are lost
            AssetDatabase.DeleteAsset(Root + $"/Scenes/{terrainAssetName}.asset");
            AssetDatabase.CreateAsset(data, Root + $"/Scenes/{terrainAssetName}.asset");
            float ground = (0f - baseY) / height;           // y = 0 in world
            float bed = (CanalDepth - baseY) / height;
            float riverBed = (-4f - baseY) / height;
            var h = new float[res, res];
            float[,] realY = null, smoothY = null;
            if (real != null)
            {
                // real ground, plus a heavily smoothed copy used to grade the roads (a ramp instead of every bump)
                realY = new float[res, res];
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                        realY[z, x] = RealY(x / (float)(res - 1) * size - size / 2f, z / (float)(res - 1) * size - size / 2f);
                smoothY = BoxBlur(BoxBlur(realY, 10), 10);
            }
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = x / (float)(res - 1) * size - size / 2f;
                    float wz = z / (float)(res - 1) * size - size / 2f;
                    // gentle undulation (Normandy is flat here), flattened to just below road level near roads
                    float rd = RoadDistance(wx, wz, segs);
                    float v;
                    if (realY != null)
                        v = (Mathf.Lerp(smoothY[z, x] - 0.04f, realY[z, x], Mathf.Clamp01(rd / 6f)) - baseY) / height;
                    else
                    {
                        float bump = (Mathf.PerlinNoise(wx * 0.012f + 31f, wz * 0.012f + 7f) - 0.35f) * 0.45f;
                        bump = Mathf.Lerp(-0.04f, bump, Mathf.Clamp01(rd / 6f));
                        v = ground + bump / height;
                    }
                    float d = Mathf.Abs(wx), sgn = wx < 0f ? -1f : 1f;
                    float quayFace = Water.QuayFace(wz, sgn);
                    // behind a quay the LiDAR has today's wider canal: give it back the ground of the bank behind
                    if (quayFace > 0f && realY != null && d > quayFace && d < 30f)
                    {
                        float bank = RealY(sgn * 31.5f, wz), yHere = v * height + baseY;
                        if (bank > yHere) v = (Mathf.Lerp(yHere, bank, 1f - Smooth01(28.5f, 30f, d)) - baseY) / height;
                    }
                    // vertical concrete quays: the bank stays at full height right up to the quay wall,
                    // so the bridge ends sit on solid ground
                    if (quayFace > 0f && d < quayFace + 0.6f) v = bed;
                    else if (d < CanalHalfWidth + 60f && Mathf.Abs(wz) < 24f)
                    {
                        // bridge approaches, shaped in the ground itself (no concrete slabs): just under the deck where
                        // it rests on the bank, then a long smooth grade out to the real ground along the road
                        float yw = v * height + baseY;
                        float deckEnd = wx < 0f ? Water.WestQuay : BridgeEastEnd;
                        float along = Smooth01(deckEnd + 0.5f, deckEnd + 38f, d);       // 0 at the deck end, 1 on the open road
                        float across = 1f - Smooth01(8f, 22f, Mathf.Abs(wz));                         // road + shoulders, fading into the fields
                        float target = d < deckEnd + 0.5f && Mathf.Abs(wz) < 6f ? 0.04f : Mathf.Lerp(0.09f, yw, along);
                        yw = Mathf.Lerp(yw, target, across);
                        v = (yw - baseY) / height;
                    }
                    // beyond the quays: natural canal banks (June 1944 photos) - a gentle shelf down from the bank to the
                    // water's edge, then down to the bed; never higher than the ground that was there
                    if (quayFace < 0f)
                    {
                        float shore = Water.CanalShore(wz, Mathf.Sign(wx));
                        // the LiDAR has today's wider canal: give the 1944 banks back the ground of the bank behind them
                        if (realY != null && d > shore && d < 30f)
                        {
                            float bank = RealY(Mathf.Sign(wx) * 31.5f, wz);
                            float yHere = v * height + baseY;
                            if (bank > yHere) v = (Mathf.Lerp(yHere, bank, Smooth01(shore, shore + 1.5f, d) * (1f - Smooth01(28.5f, 30f, d))) - baseY) / height;
                        }
                        if (d < shore + 3.4f)
                        {
                            float yGround = v * height + baseY, yb;
                            if (d < shore - 7f) yb = CanalDepth;
                            else if (d < shore) yb = Mathf.Lerp(CanalDepth, WaterLevel - 0.25f, Smooth01(shore - 7f, shore, d));
                            else yb = Mathf.Lerp(WaterLevel - 0.25f, yGround, Smooth01(shore, shore + 3.4f, d));
                            v = (Mathf.Min(yb, yGround) - baseY) / height;
                        }
                    }
                    // River Orne: natural muddy banks, slight meander (the LiDAR ground already has the real one)
                    if (realY == null)
                    {
                        float od = Mathf.Abs(wx - OrneX - Mathf.Sin(wz * 0.006f) * 25f);
                        if (od < OrneHalfWidth - 4f) v = riverBed;
                        else if (od < OrneHalfWidth + 2f) v = Mathf.Lerp(riverBed, v, (od - (OrneHalfWidth - 4f)) / 6f);
                    }
                    h[z, x] = v;
                }
            data.SetHeights(0, 0, h);
            Debug.Log($"Stan's Game: heightmap written, h[mid]={h[res / 2, res / 4]:F3} read back {data.GetHeight(res / 4, res / 2):F2} m");

            // two ground layers: meadow grass and worn earth/gravel along roads, towpaths and river banks
            var grassTex = MakeNoiseTexture("GrassTex", new Color(0.16f, 0.22f, 0.10f), new Color(0.26f, 0.31f, 0.15f));
            var dirtTex = MakeNoiseTexture("DirtTex", new Color(0.24f, 0.20f, 0.15f), new Color(0.36f, 0.31f, 0.24f));
            var grassLayer = new TerrainLayer { diffuseTexture = grassTex, tileSize = new Vector2(5f, 5f), smoothness = 0.05f };
            var dirtLayer = new TerrainLayer { diffuseTexture = dirtTex, tileSize = new Vector2(4f, 4f), smoothness = 0.1f };
            AssetDatabase.DeleteAsset(MatDir + "/Grass.terrainlayer"); AssetDatabase.DeleteAsset(MatDir + "/Dirt.terrainlayer");
            AssetDatabase.CreateAsset(grassLayer, MatDir + "/Grass.terrainlayer");
            AssetDatabase.CreateAsset(dirtLayer, MatDir + "/Dirt.terrainlayer");
            data.terrainLayers = new[] { grassLayer, dirtLayer };
            // the photo-scanned ground (if the textures are in the project) replaces the two flat layers
            var scanned = GroundLayers();
            float[,,] painted = null;
            if (scanned != null)
            {
                data.terrainLayers = scanned;
                data.alphamapResolution = 1024;
                painted = PaintGround(1024, size, segs, h, res, height, baseY);
            }

            const int amRes = 512;
            if (painted == null) data.alphamapResolution = amRes;
            var am = new float[amRes, amRes, 2];
            for (int z = 0; z < amRes; z++)
                for (int x = 0; x < amRes; x++)
                {
                    float wx = x / (float)(amRes - 1) * size - size / 2f;
                    float wz = z / (float)(amRes - 1) * size - size / 2f;
                    float rd = RoadDistance(wx, wz, segs);
                    float dirt = 1f - Mathf.Clamp01((rd - 0.5f) / 2.5f);                         // worn verges
                    float canal = Mathf.Abs(wx) - CanalHalfWidth;
                    if (canal < 3f) dirt = 1f;                                                       // quay/towpath edges
                    float od = OrneDistance(wx, wz);
                    if (od < 4f) dirt = Mathf.Max(dirt, 1f - Mathf.Clamp01(od / 4f));                // muddy river banks
                    dirt = Mathf.Max(dirt, Mathf.Clamp01((Mathf.PerlinNoise(wx * 0.05f, wz * 0.05f) - 0.72f) * 3f)); // bare patches
                    am[z, x, 0] = 1f - dirt; am[z, x, 1] = dirt;
                }
            if (painted != null) data.SetAlphamaps(0, 0, painted);
            else data.SetAlphamaps(0, 0, am);

            // grass detail: short meadow grass everywhere, long rough grass in the landing field and by hedges
            var shortGrass = MakeGrassTexture("GrassBlades", new Color(0.30f, 0.38f, 0.18f), 0.75f);
            var longGrass = MakeGrassTexture("GrassLong", new Color(0.38f, 0.40f, 0.22f), 1.0f);
            data.detailPrototypes = new[]
            {
                new DetailPrototype { prototypeTexture = shortGrass, renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false,
                    minWidth = 0.3f, maxWidth = 0.55f, minHeight = 0.15f, maxHeight = 0.35f, noiseSpread = 0.3f,
                    healthyColor = new Color(0.55f, 0.65f, 0.40f), dryColor = new Color(0.62f, 0.58f, 0.40f) },
                new DetailPrototype { prototypeTexture = longGrass, renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false,
                    minWidth = 0.35f, maxWidth = 0.7f, minHeight = 0.35f, maxHeight = 0.7f, noiseSpread = 0.5f,
                    healthyColor = new Color(0.55f, 0.60f, 0.38f), dryColor = new Color(0.70f, 0.62f, 0.42f) },
            };
            const int detRes = 1024;
            data.SetDetailResolution(detRes, 32);
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);   // values = blades per cell (Unity 6 defaults to coverage 0-255)
            var d0 = new int[detRes, detRes]; var d1 = new int[detRes, detRes];
            for (int z = 0; z < detRes; z++)
                for (int x = 0; x < detRes; x++)
                {
                    float wx = x / (float)(detRes - 1) * size - size / 2f;
                    float wz = z / (float)(detRes - 1) * size - size / 2f;
                    if (InWater(wx, wz) || RoadDistance(wx, wz, segs) < 1.2f) continue;
                    if (Mathf.Abs(wx) < Mathf.Max(Mathf.Abs(wz) < 42f ? CanalHalfWidth + 6f : 0f, Water.CanalShore(wz, Mathf.Sign(wx)) + 3.5f)) continue;   // towpath edges, banks
                    if (painted != null)
                    {
                        // no grass on the gravel, the mud or the bare earth
                        int ax = x * 1023 / (detRes - 1), az = z * 1023 / (detRes - 1);
                        float bare = painted[az, ax, 2] + painted[az, ax, 3] + painted[az, ax, 4] + painted[az, ax, 5];
                        if (bare > 0.55f || Random.value < bare) continue;
                    }
                    float n = Mathf.PerlinNoise(wx * 0.08f + 3f, wz * 0.08f + 11f);
                    d0[z, x] = n > 0.25f ? 4 + Mathf.RoundToInt(n * 6f) : 2;
                    bool lz = wx > CanalHalfWidth + 15f && wx < CanalHalfWidth + 190f && wz < -18f && wz > -200f;   // LZ X
                    if (lz || n > 0.7f) d1[z, x] = lz ? 2 + Mathf.RoundToInt(n * 3f) : 2;
                }
            data.SetDetailLayer(0, 0, 0, d0);
            data.SetDetailLayer(0, 0, 1, d1);
            data.wavingGrassAmount = 0.35f; data.wavingGrassSpeed = 0.4f; data.wavingGrassStrength = 0.4f;
            data.wavingGrassTint = new Color(0.6f, 0.65f, 0.5f);
            // creating the layer/texture assets above makes Unity reload the TerrainData from disk, where it was
            // saved (CreateAsset) before the heights were set - so write the heights again before saving
            data.SetHeights(0, 0, h);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            var t = Terrain.CreateTerrainGameObject(data);
            t.name = "Terrain (Bénouville / Ranville)";
            t.transform.SetParent(env);
            t.transform.position = new Vector3(-size / 2f, baseY, -size / 2f);
            var terrain = t.GetComponent<Terrain>();
            terrainRef = terrain;
            terrain.detailObjectDistance = 110f; terrain.detailObjectDensity = 1f;
            terrain.treeDistance = 1500f; terrain.basemapDistance = 400f;
        }

        static float[,] BoxBlur(float[,] src, int r)
        {
            int nz = src.GetLength(0), nx = src.GetLength(1);
            var tmp = new float[nz, nx]; var dst = new float[nz, nx];
            for (int z = 0; z < nz; z++)
            {
                float sum = 0f; int cnt = 0;
                for (int x = -r; x <= r; x++) if (x >= 0 && x < nx) { sum += src[z, x]; cnt++; }
                for (int x = 0; x < nx; x++)
                {
                    tmp[z, x] = sum / cnt;
                    int o = x - r, i = x + r + 1;
                    if (o >= 0) { sum -= src[z, o]; cnt--; }
                    if (i < nx) { sum += src[z, i]; cnt++; }
                }
            }
            for (int x = 0; x < nx; x++)
            {
                float sum = 0f; int cnt = 0;
                for (int z = -r; z <= r; z++) if (z >= 0 && z < nz) { sum += tmp[z, x]; cnt++; }
                for (int z = 0; z < nz; z++)
                {
                    dst[z, x] = sum / cnt;
                    int o = z - r, i = z + r + 1;
                    if (o >= 0) { sum -= tmp[o, x]; cnt--; }
                    if (i < nz) { sum += tmp[i, x]; cnt++; }
                }
            }
            return dst;
        }

        static Texture2D MakeGrassTexture(string name, Color tint, float tall)
        {
            // procedural clump of grass blades with alpha, saved as a PNG so the terrain can use it
            string path = $"{MatDir}/{name}.png";
            if (!File.Exists(path))
            {
                const int W = 128, H = 128;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                var px = new Color[W * H];
                var rnd = new System.Random(name.GetHashCode());
                for (int b = 0; b < 26; b++)
                {
                    float x0 = 10 + (float)rnd.NextDouble() * (W - 20);
                    float lean = ((float)rnd.NextDouble() - 0.5f) * 40f;
                    float top = H * (0.45f + 0.55f * tall * (float)rnd.NextDouble());
                    float wBase = 2.5f + (float)rnd.NextDouble() * 2.5f;
                    float shade = 0.7f + (float)rnd.NextDouble() * 0.5f;
                    for (int y = 0; y < (int)top && y < H; y++)
                    {
                        float k = y / top;
                        float cx = x0 + lean * k * k;
                        float hw = wBase * (1f - k);
                        for (int x = Mathf.Max(0, (int)(cx - hw - 1)); x <= Mathf.Min(W - 1, (int)(cx + hw + 1)); x++)
                        {
                            float a = Mathf.Clamp01(hw + 0.5f - Mathf.Abs(x - cx));
                            if (a <= 0f) continue;
                            var c = tint * shade * Mathf.Lerp(0.55f, 1.1f, k); c.a = a;
                            var old = px[y * W + x];
                            px[y * W + x] = old.a > a ? old : c;
                        }
                    }
                }
                tex.SetPixels(px); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D MakeWaterNormal()
        {
            // tileable ripple normal map (sum of periodic waves)
            string path = $"{MatDir}/WaterNormal.png";
            if (!File.Exists(path))
            {
                const int N = 256;
                var hgt = new float[N, N];
                var rnd = new System.Random(1944);
                var waves = new (int kx, int ky, float amp, float ph)[14];
                for (int i = 0; i < waves.Length; i++)
                    waves[i] = (rnd.Next(-6, 7), rnd.Next(1, 9), 1f / (1 + i * 0.4f), (float)rnd.NextDouble() * 6.28f);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float v = 0f;
                        foreach (var w in waves) v += w.amp * Mathf.Sin(2f * Mathf.PI * (w.kx * x + w.ky * y) / N + w.ph);
                        hgt[x, y] = v;
                    }
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = hgt[(x + 1) % N, y] - hgt[(x + N - 1) % N, y];
                        float dy = hgt[x, (y + 1) % N] - hgt[x, (y + N - 1) % N];
                        var n = new Vector3(-dx * 0.35f, -dy * 0.35f, 1f).normalized;
                        tex.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
                    }
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.NormalMap; ti.wrapMode = TextureWrapMode.Repeat;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material WaterMaterial()
        {
            var m = Mat("Water", new Color(0.035f, 0.06f, 0.065f), 0.94f);
            var nrm = MakeWaterNormal();
            m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 0.6f); m.EnableKeyword("_NORMALMAP");
            m.SetTextureScale("_BaseMap", new Vector2(40f, 40f));
            m.SetFloat("_SpecularHighlights", 1f); m.SetFloat("_EnvironmentReflections", 1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D MakeNoiseTexture(string name, Color a, Color b)
        {
            string path = $"{MatDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing) return existing;
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, true);
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.6f + Random.value * 0.4f;
                    tex.SetPixel(x, y, Color.Lerp(a, b, n));
                }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        static GameObject WaterPlane(string name, Transform parent, Vector3 pos, Vector2 size, float yaw = 0f)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = name; water.transform.SetParent(parent);
            water.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            water.transform.localScale = new Vector3(size.x / 10f, 1f, size.y / 10f);
            water.GetComponent<Renderer>().sharedMaterial = WaterShaderMaterial(name.Contains("Orne")) ?? WaterMaterial();
            water.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.AddComponent<WaterRipple>();
            return water;
        }

        static void BuildCanal(Transform env)
        {
            var canal = Group("Caen Canal", env);
            // the water reaches the natural banks (wider than the quays at the bridge)
            // (in three pieces: beside the bridge only the neck between the quays, so no water shows in the dry
            // counterweight pit behind the east quay)
            const float neck = 5f;
            float reach = TerrainSize * 0.5f - neck;
            foreach (float zs in new[] { -1f, 1f })
                WaterPlane("Canal water", canal, new Vector3(0f, WaterLevel, zs * (neck + reach * 0.5f)), new Vector2((Water.OpenHalf + 4.5f) * 2f, reach));
            WaterPlane("Canal water (at the bridge)", canal, new Vector3((Water.QuayHalf - Water.WestQuay) * 0.5f, WaterLevel, 0f), new Vector2(Water.QuayHalf + Water.WestQuay, neck * 2f));
            // concrete quays and the west pier/fixed span: PegasusLevelBuilder.Quays.cs
            BuildQuays(canal);
            var stone = Mat("Quay_Stone", new Color(0.45f, 0.43f, 0.40f));
            if (false)
            {

            // stone slipways along both quays (next to the bridge and further up and down the canal), so a soldier who
            // falls in can walk out: from the canal bed up to quay level, then a small landing to step off onto the bank
            const float rampRun = 14f, rampWidth = 2.6f;
            float rise = -CanalDepth, slope = Mathf.Sqrt(rampRun * rampRun + rise * rise), angle = Mathf.Atan2(rise, rampRun) * Mathf.Rad2Deg;
            foreach (float side in new[] { -1f, 1f })
                foreach (float start in new[] { 10f, -10f, 150f, -150f, 320f, -320f })
                {
                    float dir = Mathf.Sign(start);                      // climb away from the bridge
                    float x = side * (CanalHalfWidth - rampWidth / 2f - 0.15f);
                    float zc = start + dir * rampRun / 2f;
                    var r = Box("Slipway", canal, new Vector3(x, CanalDepth + rise / 2f - 0.2f, zc), new Vector3(rampWidth, 0.4f, slope), stone);
                    r.transform.rotation = Quaternion.Euler(-dir * angle, 0f, 0f);
                    Box("Slipway landing", canal, new Vector3(x, -0.2f, start + dir * (rampRun + 1.5f)), new Vector3(rampWidth, 0.4f, 3f), stone);
                }
            }   // (no slipways any more: away from the quays the natural banks let a swimmer walk out)

            // River Orne with Horsa Bridge (Ranville road), ~430 m east; built from short straight reaches
            var orne = Group("River Orne", env);
            if (real != null)
                // one sheet at the tidal level: it shows wherever the real ground dips below it - the Orne,
                // the pond in the landing field and the drainage ditches (above it everything is dry)
                WaterPlane("Orne, pond and ditches (tidal level)", orne, new Vector3(CanalHalfWidth + TerrainSize * 0.25f, MarshWaterY, 0f), new Vector2(TerrainSize * 0.5f - CanalHalfWidth, TerrainSize));
            else for (float z = -600f; z < 600f; z += 40f)
            {
                float x0 = OrneX + Mathf.Sin(z * 0.006f) * 25f, x1 = OrneX + Mathf.Sin((z + 40f) * 0.006f) * 25f;
                float yaw = Mathf.Atan2(x1 - x0, 40f) * Mathf.Rad2Deg;
                WaterPlane("Orne reach", orne, new Vector3((x0 + x1) * 0.5f, -1.6f, z + 20f), new Vector2(OrneHalfWidth * 2f + 2f, 42f), yaw);
            }
            BuildHorsaBridge(orne);
        }

        // the 1934 model (Models/Source~/bridge1934.py): "Leaf" turns about its origin, the centre of the rolling
        // segments, R = 5 m; the road is 1.25 m above the track, i.e. 3.75 m under that centre. "Base" is the pier:
        // counterweight pit and the two rolling tracks, from 1.2 m in front of the segments to 9.2 m behind.
        const float SegmentRadius = 5f, RoadBelowCentre = 3.75f, PitFront = -1.2f, PitBack = 9.2f, PitHalf = 4.2f;

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r) return r; }
            return null;
        }

        static Bounds MeshBounds(Transform t)
        {
            var b = new Bounds(); bool any = false;
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; if (!m) continue;
                var v = m.vertices;
                for (int i = 0; i < v.Length; i += 3)
                {
                    var w = mf.transform.TransformPoint(v[i]);
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                }
            }
            return b;
        }

        /// <summary>Whatever axes the FBX arrives in: the leaf's length along X, the pier below the segment centre, and the
        /// tower and pier to the east (the Ranville bank, both RAF photographs).</summary>
        static void OrientBridge(GameObject bridge, Transform leaf, Transform pier)
        {
            var original = bridge.transform.rotation;
            for (int rx = 0; rx < 4; rx++)
                for (int ry = 0; ry < 4; ry++)
                    for (int rz = 0; rz < 4; rz++)
                    {
                        bridge.transform.rotation = Quaternion.Euler(rx * 90f, ry * 90f, rz * 90f) * original;
                        var lb = MeshBounds(leaf); var pb = MeshBounds(pier);
                        if (lb.size.x > 2.5f * lb.size.z && pb.center.y < leaf.position.y - 2f && pb.center.x > leaf.position.x + 1f)
                        {
                            Debug.Log($"Stan's Game: bridge orientation {bridge.transform.eulerAngles}, leaf {lb.size.x:F1} m long");
                            return;
                        }
                    }
            bridge.transform.rotation = original;
            Debug.LogWarning("Stan's Game: could not orient the bridge model");
        }

        static Transform BuildBridge(Transform env)
        {
            var bridgeRoot = Group("Bénouville Bridge (Pegasus Bridge)", env);
            bridgeRef = bridgeRoot;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BridgeModel);
            var steel = Mat("Bridge_Steel", new Color(0.30f, 0.32f, 0.31f), 0.3f, 0.6f);
            var stone = Concrete();
            GameObject bridge;
            Transform leaf = null, pier = null;
            if (model)
            {
                bridge = (GameObject)PrefabUtility.InstantiatePrefab(model, bridgeRoot);
                PrefabUtility.UnpackPrefabInstance(bridge, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                bridge.transform.position = Vector3.zero;
                leaf = FindDeep(bridge.transform, "Leaf"); pier = FindDeep(bridge.transform, "Base");
            }
            else bridge = null;
            if (!leaf || !pier)
            {
                Debug.LogError("Stan's Game: " + BridgeModel + " missing or without Leaf/Base; using a blockout bridge.");
                if (bridge) Object.DestroyImmediate(bridge);
                bridge = Group("Bridge blockout", bridgeRoot).gameObject;
                Box("Deck", bridge.transform, new Vector3(3.3f, -0.15f, 0f), new Vector3(31f, 0.5f, 6.6f), steel);
            }
            else
            {
                OrientBridge(bridge, leaf, pier);
                foreach (var mf in bridge.GetComponentsInChildren<MeshFilter>())
                    if (!mf.GetComponent<Collider>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                // road on the leaf level with the banks, the free end on the west quay, centred on the road
                bridge.transform.position += Vector3.up * (0.12f + RoadBelowCentre - leaf.position.y);
                var lb = MeshBounds(leaf);
                bridge.transform.position += new Vector3(-BridgeWestEnd - lb.min.x, 0f, -leaf.position.z);
                lb = MeshBounds(leaf);
                Debug.Log($"Stan's Game: 1934 bridge: leaf x {lb.min.x:F1}..{lb.max.x:F1}, {lb.size.y:F1} m high; segments touch the track at x {leaf.position.x:F1}");

                // the leaf moves: no static batching, a kinematic body so soldiers ride and collide properly
                foreach (var t in leaf.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                var rb = leaf.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.useGravity = false;
                var lift = bridgeRoot.gameObject.AddComponent<BridgeLift>();
                lift.leaf = leaf; lift.restPosition = leaf.position; lift.restRotation = leaf.rotation;
                lift.east = Vector3.right; lift.radius = SegmentRadius; lift.maxAngle = 66f;
                lift.checkCentre = lb.center; lift.checkSize = lb.size + new Vector3(0.6f, 0.5f, 0.6f);

                // the counterweight pit: open the ground over it (terrain hole), with a stone kerb round the edge
                float px0 = leaf.position.x + PitFront, px1 = leaf.position.x + PitBack;
                CutTerrainHole(px0, px1, -PitHalf, PitHalf, out float hx0, out float hx1, out float hz0, out float hz1);
                var pitGroup = Group("Counterweight pit kerb", bridgeRoot);
                float ky = 0.06f;
                Box("Pit kerb (north)", pitGroup, new Vector3((px0 + hx1 + 0.4f) * 0.5f, ky - 0.15f, (PitHalf + Mathf.Max(hz1, PitHalf) + 0.6f) * 0.5f), new Vector3(hx1 + 0.4f - px0, 0.3f, Mathf.Max(hz1, PitHalf) + 0.6f - PitHalf), stone);
                Box("Pit kerb (south)", pitGroup, new Vector3((px0 + hx1 + 0.4f) * 0.5f, ky - 0.15f, -(PitHalf + Mathf.Max(-hz0, PitHalf) + 0.6f) * 0.5f), new Vector3(hx1 + 0.4f - px0, 0.3f, Mathf.Max(-hz0, PitHalf) + 0.6f - PitHalf), stone);
                Box("Pit kerb (east)", pitGroup, new Vector3((px1 + Mathf.Max(hx1, px1) + 0.6f) * 0.5f, ky - 0.15f, 0f), new Vector3(Mathf.Max(hx1, px1) + 0.6f - px1, 0.3f, 2f * PitHalf), stone);

                // covers over the pit, so soldiers walking beside the bridge don't drop into it and get stuck:
                // chequer plates over the strips between the leaf and the pit walls on both sides (the leaf never
                // swings into them), and an invisible floor at ground level over the whole pit (the leaf is a
                // kinematic body: it passes through it when the bridge is raised, men walk on it)
                {
                    float x0 = px0 - 0.2f, x1 = Mathf.Max(hx1, px1) + 0.2f, top = ky, thick = 0.08f;
                    // how wide the parts of the leaf that sit down in the pit are (segments, girders), at rest
                    float lowN = -99f, lowS = 99f;
                    foreach (var mf in leaf.GetComponentsInChildren<MeshFilter>())
                    {
                        if (!mf.sharedMesh) continue;
                        foreach (var v in mf.sharedMesh.vertices)
                        {
                            var w = mf.transform.TransformPoint(v);
                            if (w.y > -0.15f || w.x < px0 || w.x > px1) continue;
                            lowN = Mathf.Max(lowN, w.z); lowS = Mathf.Min(lowS, w.z);
                        }
                    }
                    if (lowN < lowS) { lowN = 3.3f; lowS = -3.3f; }
                    float leafN = lowN + 0.1f, leafS = lowS - 0.1f;
                    float edgeN = Mathf.Max(hz1, PitHalf) + 0.05f, edgeS = Mathf.Min(hz0, -PitHalf) - 0.05f;
                    var plate = ChequerPlate(new Vector2(x1 - x0, Mathf.Max(0.2f, edgeN - leafN)));
                    if (edgeN > leafN)
                        Box("Pit cover plate (north)", pitGroup, new Vector3((x0 + x1) * 0.5f, top - thick * 0.5f, (leafN + edgeN) * 0.5f), new Vector3(x1 - x0, thick, edgeN - leafN), plate);
                    if (leafS > edgeS)
                        Box("Pit cover plate (south)", pitGroup, new Vector3((x0 + x1) * 0.5f, top - thick * 0.5f, (leafS + edgeS) * 0.5f), new Vector3(x1 - x0, thick, leafS - edgeS), plate);
                    var floor = Box("Pit floor (invisible, walkable)", pitGroup, new Vector3((x0 + x1) * 0.5f, top - 0.25f, (edgeN + edgeS) * 0.5f), new Vector3(x1 - x0, 0.5f, edgeN - edgeS), plate);
                    Object.DestroyImmediate(floor.GetComponent<MeshRenderer>());
                    Debug.Log($"Stan's Game: pit covers x {x0:F1}..{x1:F1}, leaf in the pit z {lowS:F2}..{lowN:F2}, pit z {edgeS:F2}..{edgeN:F2}");
                }

                // the bridge-keeper's hut and its controller
                BuildControlHut(bridgeRoot, lift);
            }

            // the free end rests on the west pier, the fixed span runs on to the west quay
            BuildWestSpan(bridgeRoot);
            // an invisible floor under the seam between the fixed span and the leaf so nobody slips through it
            {
                float inner = BridgeWestEnd - 0.25f, outer = BridgeWestEnd + 1.4f, top = 0.04f, thick = 0.6f;
                var floor = Box("Approach floor (west, invisible)", Group("Bridge approaches", bridgeRoot),
                    new Vector3(-(inner + outer) * 0.5f, top - thick * 0.5f, 0f), new Vector3(outer - inner, thick, 6f), stone);
                Object.DestroyImmediate(floor.GetComponent<MeshRenderer>());
            }

            var westEnd = new GameObject("Objective - West end of bridge").transform;
            westEnd.SetParent(bridgeRoot);
            westEnd.position = new Vector3(-Water.WestQuay - 5f, 0f, 0f);
            return westEnd;
        }

        /// <summary>Cut a hole in the terrain over [x0,x1] x [z0,z1] (whole hole cells); returns the cells' world extent.</summary>
        static void CutTerrainHole(float x0, float x1, float z0, float z1, out float hx0, out float hx1, out float hz0, out float hz1)
        {
            hx0 = x0; hx1 = x1; hz0 = z0; hz1 = z1;
            if (!terrainRef) return;
            var td = terrainRef.terrainData; var tp = terrainRef.transform.position;
            int res = td.holesResolution;
            float cx = td.size.x / res, cz = td.size.z / res;
            // cells whose centres fall inside the pit (so the hole never runs past the pit's own walls by more than half a cell)
            int i0 = Mathf.CeilToInt((x0 - tp.x) / cx - 0.5f), i1 = Mathf.FloorToInt((x1 - tp.x) / cx - 0.5f);
            int j0 = Mathf.CeilToInt((z0 - tp.z) / cz - 0.5f), j1 = Mathf.FloorToInt((z1 - tp.z) / cz - 0.5f);
            if (i1 < i0 || j1 < j0) return;
            var holes = new bool[j1 - j0 + 1, i1 - i0 + 1];      // false = hole
            td.SetHoles(i0, j0, holes);
            hx0 = tp.x + i0 * cx; hx1 = tp.x + (i1 + 1) * cx; hz0 = tp.z + j0 * cz; hz1 = tp.z + (j1 + 1) * cz;
            Debug.Log($"Stan's Game: terrain hole over the counterweight pit x {hx0:F1}..{hx1:F1}, z {hz0:F1}..{hz1:F1} ({cx:F2} m cells)");
        }

        /// <summary>
        /// The bridge-keeper's hut on the east bank beside the counterweight (pb08, aerial photos): a small brick hut
        /// with a window towards the bridge and, under it, the controller - a cast-iron pedestal with the lever.
        /// </summary>
        static void BuildControlHut(Transform parent, BridgeLift lift)
        {
            var hut = new GameObject("Bridge-keeper's hut (controls)").transform;
            hut.SetParent(parent);
            var pos = new Vector3(CanalHalfWidth + 4f, 0f, 8f);
            pos.y = GroundAt(pos);
            hut.position = pos;
            var wall = Mat("Cabin_White", new Color(0.82f, 0.80f, 0.76f));
            var roof = Mat("Roof_Slate", new Color(0.22f, 0.23f, 0.25f));
            var glass = Mat("Bridge1934_GlassHut", new Color(0.08f, 0.10f, 0.12f), 0.9f);
            var iron = Mat("Controller_Iron", new Color(0.12f, 0.13f, 0.12f), 0.4f, 0.6f);
            var brass = Mat("Controller_Brass", new Color(0.62f, 0.50f, 0.25f), 0.7f, 0.8f);
            const float W = 3.4f, D = 3.0f, H = 2.7f, T = 0.15f;
            void Part(string n, Vector3 c, Vector3 s, Material m, bool col = true) => Prim(PrimitiveType.Cube, n, hut, c, Vector3.zero, s, m, col);
            Part("Floor", new Vector3(0f, -0.05f, 0f), new Vector3(W + 0.2f, 0.3f, D + 0.2f), wall);
            Part("Back wall", new Vector3(0f, H / 2f, D / 2f), new Vector3(W, H, T), wall);
            Part("East wall", new Vector3(W / 2f, H / 2f, 0f), new Vector3(T, H, D), wall);
            // west wall faces the bridge: a wide window over the controller
            Part("West wall (low)", new Vector3(-W / 2f, 0.5f, 0f), new Vector3(T, 1.0f, D), wall);
            Part("West wall (top)", new Vector3(-W / 2f, 2.35f, 0f), new Vector3(T, 0.7f, D), wall);
            Part("West wall (post S)", new Vector3(-W / 2f, 1.5f, -D / 2f + 0.25f), new Vector3(T, 1.0f, 0.5f), wall);
            Part("West wall (post N)", new Vector3(-W / 2f, 1.5f, D / 2f - 0.25f), new Vector3(T, 1.0f, 0.5f), wall);
            Part("Window", new Vector3(-W / 2f, 1.5f, 0f), new Vector3(0.04f, 1.0f, D - 1.0f), glass, false);
            // front wall faces the road, with the door
            Part("Front wall (W)", new Vector3(-1.1f, H / 2f, -D / 2f), new Vector3(1.2f, H, T), wall);
            Part("Front wall (E)", new Vector3(1.15f, H / 2f, -D / 2f), new Vector3(1.1f, H, T), wall);
            Part("Lintel", new Vector3(0.05f, H - 0.3f, -D / 2f), new Vector3(1.1f, 0.6f, T), wall);
            Part("Roof", new Vector3(0f, H + 0.08f, 0f), new Vector3(W + 0.5f, 0.16f, D + 0.5f), roof);
            // the controller under the window
            Part("Controller pedestal", new Vector3(-W / 2f + 0.55f, 0.5f, 0f), new Vector3(0.5f, 1.0f, 0.8f), iron);
            Part("Controller top", new Vector3(-W / 2f + 0.55f, 1.03f, 0f), new Vector3(0.62f, 0.06f, 0.92f), brass, false);
            Part("Dial", new Vector3(-W / 2f + 0.33f, 0.8f, 0f), new Vector3(0.04f, 0.25f, 0.25f), brass, false);
            var panel = new GameObject("Controller (E)").transform;
            panel.SetParent(hut, false); panel.localPosition = new Vector3(-W / 2f + 0.55f, 1.06f, 0f);
            var pivot = new GameObject("Lever pivot").transform;
            pivot.SetParent(hut, false); pivot.localPosition = new Vector3(-W / 2f + 0.6f, 1.08f, 0.15f);
            Prim(PrimitiveType.Cylinder, "Lever", pivot, new Vector3(0f, 0.28f, 0f), Vector3.zero, new Vector3(0.05f, 0.28f, 0.05f), iron);
            Prim(PrimitiveType.Sphere, "Lever knob", pivot, new Vector3(0f, 0.58f, 0f), Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f), brass);
            pivot.localRotation = Quaternion.Euler(0f, 0f, 35f);
            lift.panel = panel; lift.lever = pivot;
            foreach (var t in hut.GetComponentsInChildren<Transform>()) if (t != pivot && !t.IsChildOf(pivot)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>
        /// The bridge is 31.1 m long, 9.8 m wide and 8.3 m tall with the counterweight tower on top.
        /// Whatever axis convention the FBX arrives in, pick the 90-degree orientation where length runs
        /// along X, height is the smallest extent, and most of the detail (the tower) sits above the middle.
        /// </summary>
        static void StandUpright(GameObject go)
        {
            var original = go.transform.rotation;
            var filters = go.GetComponentsInChildren<MeshFilter>();
            Quaternion best = original; float bestScore = float.MinValue;
            for (int rx = 0; rx < 4; rx++)
                for (int rz = 0; rz < 4; rz++)
                    for (int ry = 0; ry < 2; ry++)
                    {
                        var q = Quaternion.Euler(rx * 90f, ry * 90f, rz * 90f) * original;
                        go.transform.rotation = q;
                        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity; float sumY = 0f; int n = 0;
                        foreach (var mf in filters)
                        {
                            var m = mf.sharedMesh; if (!m) continue;
                            var v = m.vertices;
                            for (int i = 0; i < v.Length; i += 11)
                            {
                                var w = mf.transform.TransformPoint(v[i]);
                                min = Vector3.Min(min, w); max = Vector3.Max(max, w); sumY += w.y; n++;
                            }
                        }
                        if (n == 0) return;
                        Vector3 size = max - min;
                        float upness = (sumY / n - (min.y + max.y) * 0.5f) / Mathf.Max(0.01f, size.y); // >0: detail is high up
                        float score = 0f;
                        if (size.x >= size.y && size.x >= size.z) score += 10f;   // length along X (the road)
                        if (size.y <= size.z) score += 5f;                         // height smallest
                        score += upness * 4f;
                        if (score > bestScore) { bestScore = score; best = q; }
                    }
            go.transform.rotation = best;
            Debug.Log($"Stan's Game: bridge orientation set to {best.eulerAngles}");
        }

        static float TowerSideX(GameObject go)
        {
            float sum = 0f; int n = 0;
            float top = Bounds(go).max.y;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; if (!m) continue;
                var v = m.vertices;
                for (int i = 0; i < v.Length; i += 7)
                {
                    var w = mf.transform.TransformPoint(v[i]);
                    if (w.y > top - 4f) { sum += w.x; n++; }
                }
            }
            return n > 0 ? sum / n : 0f;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void BuildRoads(Transform env)
        {
            var roads = Group("Roads", env);
            if (real != null) return;   // on the LiDAR ground the roads are graded into the terrain and painted as gravel
            var road = Mat("Road_Gravel", new Color(0.36f, 0.34f, 0.31f), 0.05f);
            int k = 0;
            foreach (var sg in RoadSegments())
                Segment($"Road {++k}", roads, new Vector3(sg.a.x, 0f, sg.a.y), new Vector3(sg.b.x, 0f, sg.b.y), road, sg.halfWidth * 2f);
        }

        static void Segment(string name, Transform parent, Vector3 a, Vector3 b, Material m, float width)
        {
            Vector3 d = b - a; d.y = 0f;
            var go = Box(name, parent, (a + b) * 0.5f + Vector3.up * 0.02f, new Vector3(width, 0.08f, d.magnitude + width), m);
            go.transform.rotation = Quaternion.LookRotation(d.normalized);
        }

        static void BuildBuildings(Transform env)
        {
            var b = Group("Buildings", env);
            var render = Mat("House_Render", new Color(0.70f, 0.67f, 0.60f));
            var roof = Mat("Roof_Slate", new Color(0.22f, 0.23f, 0.25f));
            var wood = Mat("Wood_Dark", new Color(0.25f, 0.18f, 0.12f));

            CafeGondree(b, wood);

            // (the bridge-keeper's hut with the bridge controller is built with the bridge: BuildControlHut)

            // houses and gardens just west of the bridge, south of the road (aerial photo)
            // (the first one: the small Bénouville house from _downloads, if it is there)
            if (!GroundProp("BenouvilleHouse", b, new Vector3(-CanalHalfWidth - 38f, 0f, -22f), 0f, "House by the café 1", true))
                House("House by the café 1", b, new Vector3(-CanalHalfWidth - 38f, 0f, -22f), new Vector3(10f, 7f, 8f), render, roof, 0f);
            House("House by the café 2", b, new Vector3(-CanalHalfWidth - 56f, 0f, -26f), new Vector3(11f, 7f, 8f), render, roof, 10f);
            // orchard behind them
            var bark = Mat("Bark", new Color(0.20f, 0.17f, 0.13f));
            var crown = Mat("Orchard_Leaves", new Color(0.11f, 0.17f, 0.09f));
            Random.InitState(61944);
            for (int ox = 0; ox < 6; ox++)
                for (int oz = 0; oz < 5; oz++)
                    AppleTree(b, new Vector3(-CanalHalfWidth - 30f - ox * 9f + Random.Range(-1f, 1f), 0f, -40f - oz * 9f + Random.Range(-1f, 1f)), bark, crown);
            // houses along the bend towards Bénouville
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = Vector3.Lerp(BenouvilleBend, BenouvilleCrossroads, (i + 0.5f) / 4f);
                Vector3 hp = p + new Vector3(0f, 0f, (i % 2 == 0 ? 14f : -14f));
                // two of them are the Bénouville house model, front to the road
                if (i % 2 == 1 && GroundProp("BenouvilleHouse", b, hp, 162f, $"House Bénouville road {i + 1}", true)) continue;
                House($"House Bénouville road {i + 1}", b, hp, new Vector3(10f + (i % 3) * 2f, 7f, 8f), render, roof, -18f);
            }
            // Bénouville village around the crossroads
            for (int i = 0; i < 10; i++)
                if (Mathf.Abs(-110f + i * 26f) > 12f)   // (not on the Bénouville road itself)
                House($"Bénouville {i + 1}", b, BenouvilleCrossroads + new Vector3(i % 2 == 0 ? -16f : 16f, 0f, -110f + i * 26f), new Vector3(9f, 7.5f, 12f), render, roof, 0f);
        }

        /// <summary>
        /// Café Gondrée, from Joan's photos (IMG_8815 from the road, IMG_2885 from the east bank): red-brick two-storey
        /// house with a steep brown-tiled roof, long side (7 shuttered windows) facing the canal across a lawn, the gable
        /// end with the café door and sign facing the Bénouville road, and a lower white-rendered wing to the south.
        /// The "Pegasus Bridge Café" lettering is post-war; in 1944 it was just the Gondrée family's café.
        /// </summary>
        static void CafeGondree(Transform parent, Material wood)
        {
            var brick = Mat("Brick_Red", new Color(0.55f, 0.24f, 0.18f));
            var tiles = Mat("Roof_Tiles_Brown", new Color(0.30f, 0.22f, 0.18f));
            var white = Mat("Shutter_White", new Color(0.86f, 0.86f, 0.83f));
            var render = Mat("House_Render", new Color(0.70f, 0.67f, 0.60f));
            var glass = Mat("Window_Dark", new Color(0.05f, 0.05f, 0.06f), 0.8f);
            // footprint: 8.5 m (across, x) by 15 m (along the canal, z); north gable 5 m south of the road centre
            const float W = 8.5f, L = 15f, H = 7.4f;
            var cafe = new GameObject("Café Gondrée").transform; cafe.SetParent(parent);
            Vector3 c = new Vector3(-CanalHalfWidth - 17f, 0f, -5f - L / 2f);
            cafe.position = c;
            Box("Walls (brick)", cafe, c + new Vector3(0f, H / 2f, 0f), new Vector3(W, H, L), brick);
            // steep roof, ridge along the canal, and two brick chimneys
            float pitch = 50f, run = W / 2f + 0.4f, slope = run / Mathf.Cos(pitch * Mathf.Deg2Rad);
            foreach (float side in new[] { -1f, 1f })
            {
                var r = Box(side < 0 ? "Roof W" : "Roof E", cafe, c + new Vector3(side * run / 2f, H + Mathf.Tan(pitch * Mathf.Deg2Rad) * run / 2f, 0f), new Vector3(slope, 0.25f, L + 0.6f), tiles);
                r.transform.rotation = Quaternion.Euler(0f, 0f, -side * pitch);
            }
            float ridge = H + Mathf.Tan(pitch * Mathf.Deg2Rad) * run;
            // hipped ends (the road front in IMG_8815 has a level eave with the roof sloping back from it)
            foreach (float end in new[] { -1f, 1f })
            {
                float hrun = 2.4f, hslope = Mathf.Sqrt(hrun * hrun + (ridge - H) * (ridge - H));
                var hip = Box(end > 0 ? "Roof hip N" : "Roof hip S", cafe, c + new Vector3(0f, (H + ridge) / 2f, end * (L / 2f + 0.3f - hrun / 2f)), new Vector3(W * 0.62f, 0.25f, hslope), tiles);
                hip.transform.rotation = Quaternion.Euler(end * Mathf.Atan2(ridge - H, hrun) * Mathf.Rad2Deg, 0f, 0f);
            }
            Box("Chimney N", cafe, c + new Vector3(-1.2f, ridge, L / 2f - 1.5f), new Vector3(0.8f, 2.2f, 0.8f), brick);
            Box("Chimney S", cafe, c + new Vector3(-1.2f, ridge - 0.4f, -L / 2f + 2f), new Vector3(0.8f, 2.2f, 0.8f), brick);
            // shuttered windows: 7 per floor on the canal (east) side, fewer on the back
            for (int fl = 0; fl < 2; fl++)
                for (int i = 0; i < 7; i++)
                {
                    float z = -L / 2f + (i + 0.5f) * L / 7f, y = 1.6f + fl * 3.2f;
                    foreach (float side in new[] { 1f, -1f })
                    {
                        if (side < 0 && i % 2 == 1) continue;
                        float x = side * (W / 2f + 0.03f);
                        Box("Window", cafe, c + new Vector3(x, y, z), new Vector3(0.05f, 1.6f, 0.9f), glass, 0f, false);
                        Box("Shutter", cafe, c + new Vector3(x + side * 0.03f, y, z - 0.75f), new Vector3(0.05f, 1.7f, 0.5f), white, 0f, false);
                        Box("Shutter", cafe, c + new Vector3(x + side * 0.03f, y, z + 0.75f), new Vector3(0.05f, 1.7f, 0.5f), white, 0f, false);
                    }
                }
            // road gable: café door between two windows downstairs, two windows upstairs, sign between them
            float gz = L / 2f + 0.03f;
            Box("Café door", cafe, c + new Vector3(0f, 1.15f, gz), new Vector3(1.4f, 2.3f, 0.05f), wood, 0f, false);
            foreach (float x in new[] { -2.6f, 2.6f })
            {
                Box("Window", cafe, c + new Vector3(x, 1.6f, gz), new Vector3(1.1f, 1.7f, 0.05f), glass, 0f, false);
                Box("Window", cafe, c + new Vector3(x, 4.9f, gz), new Vector3(1.1f, 1.7f, 0.05f), glass, 0f, false);
                foreach (float o in new[] { -0.85f, 0.85f })
                {
                    Box("Shutter", cafe, c + new Vector3(x + o, 1.6f, gz + 0.03f), new Vector3(0.6f, 1.8f, 0.05f), white, 0f, false);
                    Box("Shutter", cafe, c + new Vector3(x + o, 4.9f, gz + 0.03f), new Vector3(0.6f, 1.8f, 0.05f), white, 0f, false);
                }
            }
            Box("Café sign", cafe, c + new Vector3(0f, 4.6f, gz + 0.05f), new Vector3(1.8f, 2.2f, 0.06f), wood, 0f, false);
            // the two-storey white-rendered block to the south with its grid of shutters and a slate roof (IMG_2885)
            var slate = Mat("Roof_Slate", new Color(0.22f, 0.23f, 0.25f));
            const float L2 = 13f, H2 = 6.8f;
            Vector3 w = c + new Vector3(0.3f, 0f, -L / 2f - L2 / 2f);
            Box("South block", cafe, w + new Vector3(0f, H2 / 2f, 0f), new Vector3(W - 0.6f, H2, L2), render);
            foreach (float side in new[] { -1f, 1f })
            {
                var r2 = Box("South block roof", cafe, w + new Vector3(side * run / 2f, H2 + 1.6f, 0f), new Vector3(run * 1.35f, 0.25f, L2 + 0.4f), slate);
                r2.transform.rotation = Quaternion.Euler(0f, 0f, -side * 35f);
            }
            for (int fl = 0; fl < 2; fl++)
                for (int i = 0; i < 6; i++)
                {
                    float z = -L2 / 2f + (i + 0.5f) * L2 / 6f, y = 1.6f + fl * 3.0f, x = (W - 0.6f) / 2f + 0.03f;
                    Box("Shutter (closed)", cafe, w + new Vector3(x, y, z), new Vector3(0.05f, 1.8f, 1.3f), white, 0f, false);
                }
            GameObjectUtility.SetStaticEditorFlags(cafe.gameObject, StaticEditorFlags.BatchingStatic);
        }

        static GameObject House(string name, Transform parent, Vector3 pos, Vector3 size, Material walls, Material roofMat, float yaw)
        {
            var h = new GameObject(name); h.transform.SetParent(parent);
            h.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var body = Prim(PrimitiveType.Cube, "Walls", h.transform, new Vector3(0f, size.y / 2f, 0f), Vector3.zero, size, walls, true);
            var r1 = Prim(PrimitiveType.Cube, "Roof N", h.transform, new Vector3(0f, size.y + 1.4f, size.z * 0.25f), new Vector3(40f, 0f, 0f), new Vector3(size.x + 0.6f, 0.25f, size.z * 0.66f), roofMat, true);
            var r2 = Prim(PrimitiveType.Cube, "Roof S", h.transform, new Vector3(0f, size.y + 1.4f, -size.z * 0.25f), new Vector3(-40f, 0f, 0f), new Vector3(size.x + 0.6f, 0.25f, size.z * 0.66f), roofMat, true);
            var glass = Mat("Window_Dark", new Color(0.05f, 0.05f, 0.06f), 0.8f);
            for (int f = 0; f < 2; f++)
                for (int w = 0; w < 4; w++)
                {
                    float wx = -size.x / 2f + (w + 0.5f) * size.x / 4f;
                    Prim(PrimitiveType.Cube, "Window", h.transform, new Vector3(wx, 1.8f + f * 3f, size.z / 2f + 0.02f), Vector3.zero, new Vector3(1.1f, 1.6f, 0.05f), glass);
                    Prim(PrimitiveType.Cube, "Window", h.transform, new Vector3(wx, 1.8f + f * 3f, -size.z / 2f - 0.02f), Vector3.zero, new Vector3(1.1f, 1.6f, 0.05f), glass);
                }
            GameObjectUtility.SetStaticEditorFlags(h, StaticEditorFlags.BatchingStatic);
            return h;
        }

        // Trees from the 1944 photos (pb01 shows the row behind the gliders): tall Canadian/black poplars with long
        // bare trunks and high, ragged, wind-leaned crowns along the roads and field edges; rounder broadleaf trees
        // (ash/oak) mixed in; apple orchards in rows further out. The canal bank where the gliders landed was open grass.
        static void BuildTrees(Transform env)
        {
            var trees = Group("Poplars and hedges", env);
            var bark = Mat("Bark", new Color(0.20f, 0.17f, 0.13f));
            var poplarBark = Mat("Bark_Poplar", new Color(0.30f, 0.29f, 0.26f));
            var leaves = Mat("Poplar_Leaves", new Color(0.10f, 0.16f, 0.08f));
            var leaves2 = Mat("Poplar_Leaves_Light", new Color(0.14f, 0.20f, 0.10f));
            var broad = Mat("Broadleaf_Leaves", new Color(0.08f, 0.14f, 0.07f));
            var apple = Mat("Orchard_Leaves", new Color(0.11f, 0.17f, 0.09f));
            var hedge = Mat("Hedge", new Color(0.09f, 0.14f, 0.07f));
            Random.InitState(1944);
            void RoadTree(Vector3 p)
            {
                if (Random.value < 0.18f) Broadleaf(trees, p, bark, broad);
                else TallPoplar(trees, p, poplarBark, Random.value < 0.5f ? leaves : leaves2);
            }
            // rows along both sides of the Ranville road east of the canal (pb01, pb09)
            for (float x = CanalHalfWidth + 30f; x < OrneCentreX(0f) - 30f; x += 13f)
                foreach (float z in new[] { -9f, 9f })
                    RoadTree(new Vector3(x + Random.Range(-2f, 2f), 0f, RoadZ(x) + z + Random.Range(-1f, 1f)));
            // rows along both canal-bank roads, as in the aerial photo - except the east bank beside the landing
            // field, which pb01 shows open (the gliders came to rest right by the bank)
            foreach (float side in new[] { -1f, 1f })
                foreach (float off in new[] { TowpathRoadOffset - 4f, TowpathRoadOffset + 4f })
                    for (float z = -560f; z < 560f; z += 15f)
                    {
                        if (Mathf.Abs(z) <= 16f) continue;
                        if (side < 0f && off > TowpathRoadOffset && z > -34f && z < 0f) continue;   // café garden
                        if (side > 0f && z < -16f && z > -320f) continue;                           // open bank at LZ X
                        RoadTree(new Vector3(side * (CanalHalfWidth + off) + Random.Range(-0.8f, 0.8f), 0f, z + Random.Range(-2f, 2f)));
                    }
            // the tree line behind the gliders in pb01: field boundary on the far (east) side of LZ X
            for (float z = -330f; z < -20f; z += 12f)
                RoadTree(new Vector3(CanalHalfWidth + 118f + Random.Range(-1.5f, 1.5f), 0f, z + Random.Range(-2f, 2f)));
            // a few big broadleaf trees around the field corners
            foreach (var p in new[] { new Vector3(CanalHalfWidth + 125f, 0f, RoadZ(CanalHalfWidth + 125f) + 9f), new Vector3(CanalHalfWidth + 135f, 0f, -340f), new Vector3(CanalHalfWidth + 20f, 0f, -335f) })
                Broadleaf(trees, p, bark, broad);
            // apple orchard band beyond the tree line (the rows of low round trees in pb01)
            for (float x = CanalHalfWidth + 140f; x < CanalHalfWidth + 230f; x += 9f)
                for (float z = -60f; z > -260f; z -= 9f)
                    AppleTree(trees, new Vector3(x + Random.Range(-1f, 1f), 0f, z + Random.Range(-1f, 1f)), bark, apple);
            // hedge / embankment on the LZ edge where the gliders ended up (pb02, pb11)
            // (it follows the Ranville road, 7 m south of it)
            Hedgerow(trees, new Vector3(CanalHalfWidth + 35f, 0f, RoadZ(CanalHalfWidth + 35f) - 7f), new Vector3(88f, 0f, RoadZ(88f) - 7f));
            Hedgerow(trees, new Vector3(88f, 0f, RoadZ(88f) - 7f), new Vector3(CanalHalfWidth + 185f, 0f, RoadZ(CanalHalfWidth + 185f) - 7f));
            // the trees and scrub between the east-bank road and the gliders (orange on the 1944 photos)
            EastBankCopse(trees, poplarBark, leaves, leaves2);
            PegasusFieldRows(trees, poplarBark, leaves, leaves2, bark, broad);   // the 1944 field pattern (FieldRows.cs)
        }

        /// <summary>Canadian/black poplar: long bare trunk, ragged crown high up, leaning a little with the west wind.</summary>
        static void TallPoplar(Transform parent, Vector3 pos, Material bark, Material leaves)
        {
            var t = new GameObject("Poplar"); t.transform.SetParent(parent); t.transform.position = pos;
            float h = Random.Range(18f, 25f), lean = Random.Range(1f, 5f);
            var tree = new GameObject("Tree").transform; tree.SetParent(t.transform, false);
            tree.localRotation = Quaternion.Euler(Random.Range(-1.5f, 1.5f), 0f, -lean);   // lean east, away from the sea wind
            Prim(PrimitiveType.Cylinder, "Trunk", tree, new Vector3(0f, h * 0.35f, 0f), Vector3.zero, new Vector3(0.55f, h * 0.35f, 0.55f), bark, true);
            Prim(PrimitiveType.Cylinder, "Upper trunk", tree, new Vector3(0f, h * 0.8f, 0f), Vector3.zero, new Vector3(0.3f, h * 0.12f, 0.3f), bark);
            int puffs = Random.Range(5, 8);
            for (int i = 0; i < puffs; i++)
            {
                float y = Mathf.Lerp(h * 0.55f, h * 0.95f, Random.value);
                float ang = Random.value * Mathf.PI * 2f, r = Random.Range(0.8f, 3.2f) * (1.1f - (y / h - 0.55f));
                var off = new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);
                float sz = Random.Range(2.8f, 5.2f);
                Prim(PrimitiveType.Sphere, "Crown", tree, off, new Vector3(0f, Random.value * 360f, 0f), new Vector3(sz, sz * Random.Range(0.8f, 1.3f), sz), leaves);
                if (i < 3)   // a few bare limbs reaching up into the crown
                {
                    var limb = Prim(PrimitiveType.Cylinder, "Limb", tree, (off + new Vector3(0f, h * 0.55f, 0f)) * 0.5f, Vector3.zero, new Vector3(0.14f, 0f, 0.14f), bark);
                    Vector3 d = off - new Vector3(0f, h * 0.55f, 0f);
                    limb.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
                    limb.transform.localScale = new Vector3(0.14f, d.magnitude * 0.5f, 0.14f);
                }
            }
            AddTreeModel(t, Random.value < 0.35f ? 1 : 2, h);
            GameObjectUtility.SetStaticEditorFlags(t, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>Round broadleaf tree (ash/oak): short trunk, wide domed crown.</summary>
        static void Broadleaf(Transform parent, Vector3 pos, Material bark, Material leaves)
        {
            var t = new GameObject("Broadleaf tree"); t.transform.SetParent(parent); t.transform.position = pos;
            float h = Random.Range(12f, 17f), w = Random.Range(9f, 13f);
            Prim(PrimitiveType.Cylinder, "Trunk", t.transform, new Vector3(0f, h * 0.22f, 0f), Vector3.zero, new Vector3(0.8f, h * 0.22f, 0.8f), bark, true);
            Prim(PrimitiveType.Sphere, "Crown core", t.transform, new Vector3(0f, h * 0.62f, 0f), Vector3.zero, new Vector3(w * 0.8f, h * 0.55f, w * 0.8f), leaves);
            for (int i = 0; i < 5; i++)
            {
                float ang = i * 1.256f + Random.value * 0.5f;
                var off = new Vector3(Mathf.Cos(ang) * w * 0.3f, h * Random.Range(0.55f, 0.75f), Mathf.Sin(ang) * w * 0.3f);
                float sz = w * Random.Range(0.4f, 0.55f);
                Prim(PrimitiveType.Sphere, "Crown", t.transform, off, Vector3.zero, new Vector3(sz, sz * 0.8f, sz), leaves);
            }
            AddTreeModel(t, 2, h * 1.25f);
            GameObjectUtility.SetStaticEditorFlags(t, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>Normandy apple tree: short crooked trunk, low wide crown.</summary>
        static void AppleTree(Transform parent, Vector3 pos, Material bark, Material leaves)
        {
            var t = new GameObject("Apple tree"); t.transform.SetParent(parent); t.transform.position = pos;
            float s = Random.Range(0.85f, 1.15f);
            Prim(PrimitiveType.Cylinder, "Trunk", t.transform, new Vector3(0f, 0.9f * s, 0f), new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f)), new Vector3(0.3f, 0.9f * s, 0.3f), bark, true);
            Prim(PrimitiveType.Sphere, "Crown", t.transform, new Vector3(0f, 2.9f * s, 0f), Vector3.zero, new Vector3(4.6f * s, 2.8f * s, 4.6f * s), leaves);
            Prim(PrimitiveType.Sphere, "Crown", t.transform, new Vector3(Random.Range(-1f, 1f), 3.4f * s, Random.Range(-1f, 1f)), Vector3.zero, new Vector3(3f * s, 2.2f * s, 3f * s), leaves);
            AddTreeModel(t, 3, 5.2f * s);   // close up: a real low round-crowned tree (_downloads/trees small)
            GameObjectUtility.SetStaticEditorFlags(t, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>
        /// A round anti-tank gun pit dug into the ground (radius 3 m, 0.55 m deep), a concrete rim round the edge
        /// standing a little proud of the ground, the spoil banked up outside as a low berm, and a ramp out of the open
        /// back (gapYaw: the direction of the opening). The gun (Models/Props/SME/Pak38.fbx, made by
        /// Models/Source~/pak38_1944.py) sits on the floor facing gunYaw.
        /// </summary>
        static FieldGun GunPit(Transform parent, Vector3 c, float gapYaw, float gunYaw)
        {
            const float R = 3.0f, Depth = 0.55f, Berm = 0.4f, GapHalf = 28f;
            float y0 = TerrainY(c.x, c.z);
            Vector3 gapDir = Quaternion.Euler(0f, gapYaw, 0f) * Vector3.forward;
            TerrainEdit(c.x - R - 3.5f, c.x + R + 3.5f, c.z - R - 3.5f, c.z + R + 3.5f, (x, z, y) =>
            {
                var d = new Vector3(x - c.x, 0f, z - c.z); float r = d.magnitude;
                if (r >= R + 3f) return y;
                float floorY = y0 - Depth;
                if (r < R) return floorY;
                float t = r - R;
                float h = t < 0.6f ? y0 + Mathf.Lerp(-Depth, Berm, t / 0.6f) : y0 + Mathf.Lerp(Berm, 0f, Mathf.Clamp01((t - 0.6f) / 2.4f));
                // the opening: a ramp from the floor up to the ground
                float gap = Mathf.Clamp01((GapHalf - Vector3.Angle(d, gapDir)) / 8f);
                float ramp = Mathf.Lerp(floorY, y0, Mathf.Clamp01(t / 3f));
                return Mathf.Lerp(h, ramp, gap);
            });
            // the concrete rim (set down on the pit floor when the level is laid on the ground)
            var concrete = Mat("Concrete", new Color(0.52f, 0.51f, 0.48f));
            var rim = new GameObject("Gun pit (concrete rim)").transform; rim.SetParent(parent); rim.position = new Vector3(c.x, 0f, c.z);
            const int Segs = 30; const float H = Depth + 0.18f;
            for (int i = 0; i < Segs; i++)
            {
                float a = (i + 0.5f) * 360f / Segs;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                if (Vector3.Angle(dir, gapDir) < GapHalf) continue;
                var seg = Prim(PrimitiveType.Cube, "Rim", rim, dir * (R + 0.12f) + Vector3.up * (H * 0.5f), new Vector3(0f, a, 0f),
                    new Vector3(2f * Mathf.PI * (R + 0.12f) / Segs * 1.06f, H, 0.25f), concrete, true);
            }
            Spade(rim, new Vector3(0.6f, 0f, -(R - 0.35f)), 0f, -16f);   // an entrenching tool against the rim
            // the gun, on the floor of the pit
            var gun = new GameObject("5 cm PaK 38").transform; gun.SetParent(parent); gun.position = new Vector3(c.x, 0f, c.z);
            var model = SmeProp("Pak38", gun, c, gunYaw, 0f, "PaK 38 (model)");
            if (model) { var lp = model.transform.localPosition; lp.y = 0f; model.transform.localPosition = lp; return RigFieldGun(model); }
            Debug.LogWarning("Stan's Game: Models/Props/SME/Pak38.fbx missing (run Models/Source~/pak38_1944.py)");
            return null;
        }

        /// <summary>
        /// A wooden sentry box (Schilderhaus): plank walls, open front facing the road, pitched roof; 1.1 x 1.1 m, 2.5 m high.
        /// </summary>
        static void SentryBox(Transform parent, Vector3 pos, float yaw)
        {
            var wood = Mat("Wood_Sentry_Box", new Color(0.24f, 0.21f, 0.17f));
            var roofM = Mat("Roof_Felt", new Color(0.12f, 0.12f, 0.12f));
            var box = new GameObject("Sentry box").transform; box.SetParent(parent);
            box.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            const float W = 1.1f, H = 2.1f, T = 0.05f;
            Prim(PrimitiveType.Cube, "Back", box, new Vector3(0f, H * 0.5f, -W * 0.5f + T * 0.5f), Vector3.zero, new Vector3(W, H, T), wood, true);
            Prim(PrimitiveType.Cube, "Side L", box, new Vector3(-W * 0.5f + T * 0.5f, H * 0.5f, 0f), Vector3.zero, new Vector3(T, H, W), wood, true);
            Prim(PrimitiveType.Cube, "Side R", box, new Vector3(W * 0.5f - T * 0.5f, H * 0.5f, 0f), Vector3.zero, new Vector3(T, H, W), wood, true);
            Prim(PrimitiveType.Cube, "Floor", box, new Vector3(0f, 0.05f, 0f), Vector3.zero, new Vector3(W, 0.1f, W), wood, true);
            Prim(PrimitiveType.Cube, "Roof L", box, new Vector3(-0.3f, H + 0.17f, 0f), new Vector3(0f, 0f, 32f), new Vector3(0.75f, 0.05f, W + 0.2f), roofM);
            Prim(PrimitiveType.Cube, "Roof R", box, new Vector3(0.3f, H + 0.17f, 0f), new Vector3(0f, 0f, -32f), new Vector3(0.75f, 0.05f, W + 0.2f), roofM);
            Prim(PrimitiveType.Cube, "Gable", box, new Vector3(0f, H + 0.12f, -W * 0.5f + T * 0.5f), new Vector3(0f, 0f, 45f), new Vector3(0.5f, 0.5f, T), wood);
        }

        // ---------------------------------------------------------------- Germans

        class Defenders { public List<Health> all = new List<Health>(); public List<Health> eastBank = new List<Health>(); }

        static Defenders BuildDefences(Transform parent)
        {
            var d = new Defenders();
            var concrete = Mat("Concrete", new Color(0.52f, 0.51f, 0.48f));
            var earth = Mat("Trench_Earth", new Color(0.20f, 0.16f, 0.12f));
            var wire = Mat("Barbed_Wire", new Color(0.15f, 0.14f, 0.13f), 0.4f, 0.7f);

            // MG bunker on the east bank, north of the road: the field bunker from _downloads/ww2-field-bunker
            // (Models/Props/SME/FieldBunker.fbx, room 5.1 x 5.2 m and 2.4 m high, a firing slit in each wall),
            // its entrance at the back (east), away from the bridge. The old Pillbox.fbx if it is missing.
            var pill = Group("MG pillbox (east bank)", parent);
            Vector3 bunkerC = new Vector3(CanalHalfWidth + 12.5f, 0f, 14f);
            bool bunker = GroundProp("FieldBunker", pill, bunkerC, 90f, "Field bunker", false, true);
            if (!bunker && !PlaceProp("Pillbox.fbx", "Pillbox", pill, new Vector3(CanalHalfWidth + 12f, -0.55f, 14f), 0f, Vector3.one, true, "Pillbox"))
            {
                Box("Pillbox walls", pill, new Vector3(CanalHalfWidth + 12f, 1.1f, 14f), new Vector3(5f, 2.2f, 5f), concrete);
                Box("Pillbox roof", pill, new Vector3(CanalHalfWidth + 12f, 2.4f, 14f), new Vector3(5.8f, 0.5f, 5.8f), concrete);
            }
            if (bunker)
            {
                // earth banked against the north side; a sandbag wall along the south side, facing the road
                EarthMound(bunkerC + new Vector3(0.5f, 0f, 4.4f), 0f, 7.5f, 2.6f, 1.0f);
                SandbagLine(pill, bunkerC + new Vector3(-3.2f, 0f, -4.4f), bunkerC + new Vector3(3.0f, 0f, -4.4f), 1f);
                // the MG at the west slit (facing the bridge), the loader beside him; the floor is 0.33 m up
                d.eastBank.Add(Soldier("MG42 gunner (pillbox)", pill, bunkerC + new Vector3(-1.7f, 0.35f, 0f), 270f));
                d.eastBank.Add(Soldier("MG42 loader (pillbox)", pill, bunkerC + new Vector3(-1.1f, 0.35f, 1.3f), 250f));
                Spade(pill, bunkerC + new Vector3(1.9f, 0.33f, -2.3f), 0f, -14f);
            }
            else
            {
                // earth banked up against the back and the sides (the slit and the door stay clear)
                EarthMound(new Vector3(CanalHalfWidth + 16.2f, 0f, 13.6f), 90f, 7.5f, 3.4f, 1.5f);
                EarthMound(new Vector3(CanalHalfWidth + 12.4f, 0f, 9.9f), 0f, 6.5f, 3.0f, 1.2f);
                EarthMound(new Vector3(CanalHalfWidth + 9.9f, 0f, 18.2f), 0f, 2.6f, 2.6f, 1.0f);
                SandbagLine(pill, new Vector3(CanalHalfWidth + 8.6f, 0f, 17.4f), new Vector3(CanalHalfWidth + 8.6f, 0f, 10.4f), 0.55f);
                SandbagLine(pill, new Vector3(CanalHalfWidth + 9.2f, 0f, 10.3f), new Vector3(CanalHalfWidth + 15.6f, 0f, 10.3f), 1f);
                d.eastBank.Add(Soldier("MG42 gunner (pillbox)", pill, new Vector3(CanalHalfWidth + 12f, 0f, 14f), 200f));
                d.eastBank.Add(Soldier("MG42 loader (pillbox)", pill, new Vector3(CanalHalfWidth + 13f, 0f, 15f), 200f));
            }

            // anti-tank gun pit at the east end, south of the road: a round pit with a concrete rim ("aerial photo
            // bridge.jpg"), the 5 cm PaK 38 in it facing east up the Ranville road, open at the back towards the bridge
            var at = Group("Anti-tank gun pit", parent);
            Vector3 pitC = new Vector3(CanalHalfWidth + 17f, 0f, -14f);   // south of the Ranville road, east of the bank road and the track
            var pak = GunPit(at, pitC, 270f, 85f);
            var atCrew = Soldier("AT gun crew", at, pitC + new Vector3(-0.9f, 0f, 1.3f), 85f);
            d.eastBank.Add(atCrew);
            if (pak) pak.crew.Add(atCrew);

            // the fire trench between the canal and the east-bank road (blue on the 1944 photos): PegasusLevelBuilder.EastBank.cs
            ZigzagTrench(parent, EastBankTrench, d.eastBank, 4);

            // sentries on the bridge and west bank
            var west = Group("Sentries and west bank", parent);
            d.all.Add(Soldier("Sentry on bridge", west, new Vector3(-4f, 0.3f, 1.6f), 90f));
            d.all.Add(Soldier("Sentry on bridge (east end)", west, new Vector3(BridgeWestEnd - 3f, 0.3f, -1.6f), 270f));
            d.all.Add(Soldier("NCO with MP40 (west end)", west, new Vector3(-CanalHalfWidth - 4f, 0f, 3f), 90f));
            d.all.Add(Soldier("Rifleman by café", west, new Vector3(-CanalHalfWidth - 10f, 0f, -3f), 90f));
            d.all.Add(Soldier("MG42 west trench", west, new Vector3(-CanalHalfWidth - 10f, 0f, 12f), 90f));
            // the sentry box at the west end of the bridge, south side, beside the café ("aerial photo bridge.jpg")
            SentryBox(west, new Vector3(WestBankRoadX - 6f, 0f, -RoadWidth * 0.5f - 1.3f), 0f);
            // the west-bank MG nest: a horseshoe of sandbags facing the bridge; a sentry position by the road
            SandbagRing(west, new Vector3(-CanalHalfWidth - 10f, 0f, 12f), 2.6f, 0.85f, Vector3.left, 2);
            SandbagLine(west, new Vector3(-CanalHalfWidth - 9f, 0f, -RoadWidth * 0.5f - 2.2f), new Vector3(-CanalHalfWidth - 3.5f, 0f, -RoadWidth * 0.5f - 2.2f), 0.85f);

            // barbed wire around the triangular field (Howard's glider went through it)
            var w = Group("Barbed wire", parent);
            // wooden knife-rest frames strung with wire, end to end along the edge of the field
            for (int i = 0; i < 12; i++)
                KnifeRest(w, new Vector3(CanalHalfWidth + 20.5f + i * 5.05f, 0f, -22f), Random.Range(-3f, 3f));
            // the wire that No. 91 ran its nose into, across the end of the track (the frame under its nose knocked askew)
            {
                var g91 = GliderRests[0];
                Vector3 nose = g91.pos + Quaternion.Euler(0f, g91.yaw + g91.slew, 0f) * Vector3.forward * 9.5f;
                for (int i = -2; i <= 2; i++)
                    KnifeRest(w, new Vector3(nose.x + i * 5.05f + (i == 0 ? 0.6f : 0f), 0f, nose.z + 0.4f), i == 0 ? 28f : Random.Range(-3f, 3f));
            }

            d.all.AddRange(d.eastBank);
            return d;
        }

        static Health Soldier(string name, Transform parent, Vector3 pos, float yaw)
        {
            // the Normandy Germans (PegasusLevelBuilder.Germans.cs) when their models are in; else the older single body
            var prefab = GermanSoldierPrefab(name)
                ?? AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{(name.Contains("MG42") ? "German_MG" : name.Contains("MP40") ? "German_SMG" : "German_Rifleman")}.prefab");
            if (prefab)
            {
                var s = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                s.name = name;
                s.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                var ai = s.GetComponent<SoldierAI>();
                if (ai) { ai.mode = SoldierAI.Mode.HoldPosition; ai.reactionTime = 4f; ai.skill = name.Contains("MG42") ? 0.35f : 0.3f; }
                if (ai)
                {
                    // sentries walk their rounds; once the shooting starts the riflemen dash between firing positions.
                    // MG teams, the pillbox and the anti-tank gun crew stay at their weapons.
                    bool fixedPost = name.Contains("MG42") || name.Contains("pillbox") || name.Contains("AT gun") || name.Contains("loader");
                    ai.patrolRadius = fixedPost ? 0f : name.Contains("bridge") ? 9f : name.Contains("Sentry") || name.Contains("NCO") ? 12f : 5f;
                    ai.combatRoam = fixedPost ? 0f : name.Contains("trench") ? 6f : 10f;
                    // only the guards are awake: the sentries on the bridge and the NCO on his rounds.
                    // Everyone else (pillbox, trenches, AT gun, the MG team) is asleep until the shooting starts.
                    bool guard = name.Contains("Sentry") || name.Contains("NCO");
                    ai.sleepsUntilAlarm = !guard;
                    if (!guard) ai.patrolRadius = 0f;
                }
                // what he leaves behind: MG gunners, loaders and NCOs carried a P08 pistol; some had a stick grenade
                var loot = s.AddComponent<LootOnDeath>();
                loot.grenade = stickGrenade;
                if (name.Contains("MG42") || name.Contains("NCO") || name.Contains("loader"))
                {
                    var luger = Pistol(s.transform, "Luger P08", 32f);
                    luger.gameObject.SetActive(false);
                    loot.sidearm = luger;
                }
                AddNet(s);
                return s.GetComponent<Health>();
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var h = go.AddComponent<Health>(); h.faction = Faction.German;
            AddNet(go);
            return h;
        }

        static void SetupDefender(Health h, bool mg)
        {
            var go = h.gameObject;
            var grey = Mat("Feldgrau", new Color(0.36f, 0.38f, 0.35f));
            var helmet = Mat("Helmet_German", new Color(0.24f, 0.26f, 0.24f));
            BodyBlockout(go.transform, grey, helmet, true);
            var cc = go.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            var w = AddWeapon(go.transform, mg ? "MG42" : "Kar98k", mg ? 20f : 55f, mg ? 1200f : 30f, mg, mg ? 50 : 5);
            var ai = go.AddComponent<SoldierAI>();
            ai.weapon = w; ai.mode = SoldierAI.Mode.HoldPosition; ai.skill = mg ? 0.35f : 0.3f; ai.reactionTime = 4f; // half-asleep sentries
            go.AddComponent<Ragdoll>();
        }

        static Weapon AddWeapon(Transform owner, string name, float dmg, float rpm, bool auto, int mag)
        {
            var wgo = new GameObject(name);
            wgo.transform.SetParent(owner, false);
            wgo.transform.localPosition = new Vector3(0.25f, 1.35f, 0.35f);
            string modelName = name.StartsWith("Sten") ? "Weapon_Sten" : name.StartsWith("Bren") ? "Weapon_Bren"
                : name.StartsWith("Lee") ? "Weapon_LeeEnfield" : name.StartsWith("Kar") ? "Weapon_Kar98k" : name.StartsWith("MG42") ? "Weapon_MG42"
                : name.StartsWith("MP40") ? "Weapon_MP40" : name.StartsWith("Luger") ? "Weapon_Luger" : name.Contains("1911") ? "Weapon_M1911"
                : name.StartsWith("PIAT") ? "Weapon_PIAT"
                : name.StartsWith("Thompson") ? "Weapon_Thompson" : name.StartsWith("M1 Garand") ? "Weapon_Garand" : name.StartsWith("M1A1 carbine") ? "Weapon_Carbine" : name.Contains("azooka") ? "Weapon_Bazooka" : null;
            bool pistol = name.StartsWith("Luger") || name.Contains("1911");
            var model = modelName != null ? AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{modelName}.prefab") : null;
            Transform muzzle;
            if (model)
            {
                var vis = (GameObject)PrefabUtility.InstantiatePrefab(model, wgo.transform);
                muzzle = vis.transform.Find("Muzzle");
            }
            else if (pistol)
            {
                // stand-in until the model is added: slide/barrel and grip
                var steel = Mat("Gun_Metal", new Color(0.1f, 0.1f, 0.1f), 0.4f, 0.8f);
                Prim(PrimitiveType.Cube, "Slide", wgo.transform, new Vector3(0f, 0.03f, 0.05f), Vector3.zero, new Vector3(0.03f, 0.035f, name.StartsWith("Luger") ? 0.22f : 0.21f), steel);
                Prim(PrimitiveType.Cube, "Grip", wgo.transform, new Vector3(0f, -0.04f, -0.02f), new Vector3(name.StartsWith("Luger") ? -35f : -20f, 0f, 0f), new Vector3(0.028f, 0.11f, 0.045f), Mat("Grip_Wood", new Color(0.30f, 0.20f, 0.12f)));
                muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(wgo.transform, false); muzzle.localPosition = new Vector3(0f, 0.03f, 0.17f);
                wgo.transform.localPosition = new Vector3(0.3f, 1.3f, 0.35f);
            }
            else if (name.StartsWith("PIAT"))
            {
                var olive = Mat("PIAT_Olive", new Color(0.24f, 0.25f, 0.18f), 0.2f, 0.3f);
                Prim(PrimitiveType.Cylinder, "Body", wgo.transform, new Vector3(0f, 0f, 0.1f), new Vector3(90f, 0f, 0f), new Vector3(0.09f, 0.5f, 0.09f), olive);
                Prim(PrimitiveType.Cube, "Bomb tray", wgo.transform, new Vector3(0f, 0.02f, 0.68f), Vector3.zero, new Vector3(0.13f, 0.1f, 0.2f), olive);
                Prim(PrimitiveType.Cube, "Shoulder pad", wgo.transform, new Vector3(0f, -0.02f, -0.42f), Vector3.zero, new Vector3(0.08f, 0.16f, 0.06f), Mat("Pad_Dark", new Color(0.08f, 0.08f, 0.08f)));
                muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(wgo.transform, false); muzzle.localPosition = new Vector3(0f, 0f, 0.8f);
            }
            else
            {
                Prim(PrimitiveType.Cube, "Gun", wgo.transform, Vector3.zero, Vector3.zero, new Vector3(0.06f, 0.08f, 0.9f), Mat("Gun_Metal", new Color(0.1f, 0.1f, 0.1f), 0.4f, 0.8f));
                muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(wgo.transform, false); muzzle.localPosition = new Vector3(0f, 0f, 0.48f);
            }
            var w = wgo.AddComponent<Weapon>();
            w.displayName = name; w.damage = dmg; w.roundsPerMinute = rpm; w.automatic = auto; w.magazineSize = mag; w.muzzle = muzzle;
            var flash = new GameObject("Muzzle light").AddComponent<Light>();
            flash.transform.SetParent(muzzle, false); flash.type = LightType.Point; flash.range = 6f; flash.intensity = 3f; flash.color = new Color(1f, 0.75f, 0.4f);
            w.muzzleLight = flash;
            var src = wgo.AddComponent<AudioSource>(); src.spatialBlend = 1f; src.maxDistance = 400f; w.audioSource = src;
            return w;
        }

        static void BodyBlockout(Transform t, Material body, Material helmet, bool german)
        {
            Prim(PrimitiveType.Capsule, "Body", t, new Vector3(0f, 0.9f, 0f), Vector3.zero, new Vector3(0.55f, 0.9f, 0.4f), body);
            var head = Prim(PrimitiveType.Sphere, "Head", t, new Vector3(0f, 1.62f, 0f), Vector3.zero, new Vector3(0.24f, 0.26f, 0.24f), Mat("Skin", new Color(0.72f, 0.56f, 0.45f)), true);
            head.GetComponent<Collider>().isTrigger = false;
            // helmets: German M40 deep with flared skirt, British MkII flat brodie
            if (german) Prim(PrimitiveType.Sphere, "Helmet M40", t, new Vector3(0f, 1.70f, 0f), Vector3.zero, new Vector3(0.30f, 0.22f, 0.32f), helmet);
            else Prim(PrimitiveType.Cylinder, "Helmet MkII", t, new Vector3(0f, 1.73f, 0f), Vector3.zero, new Vector3(0.40f, 0.03f, 0.40f), helmet);
        }

        // ---------------------------------------------------------------- British

        static GameObject BuildSoldierPrefab(string name, Faction faction, Color uniform, string gun, float dmg, float rpm, bool auto, int mag, bool officer = false)
        {
            var go = new GameObject(name);
            var cc = go.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            var h = go.AddComponent<Health>(); h.faction = faction;
            var w = AddWeapon(go.transform, gun, dmg, rpm, auto, mag);
            var ai = go.AddComponent<SoldierAI>(); ai.weapon = w; ai.skill = faction == Faction.British ? 0.6f : 0.4f;
            // each section carried smoke: the No. 77 to cover a rush across open ground
            if (faction == Faction.British) { ai.smokeGrenade = smokeNo77; ai.smokeGrenades = 1; }
            else { ai.smokeGrenade = smokeNb39; ai.smokeGrenades = 1; }
            go.AddComponent<Ragdoll>();
            if (faction == Faction.British && AddBritishBody(go, officer)) MountWeapons(go, w);
            else BodyBlockout(go.transform, Mat(name + "_Uniform", uniform), Mat(name + "_Helmet", new Color(0.26f, 0.27f, 0.20f)), faction == Faction.German);
            AddNet(go);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static SoldierController BuildPlayer(Transform parent)
        {
            var go = new GameObject("Player - Ox & Bucks, D Company");
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(70f, 0.2f, -30f);
            var cc = go.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            var h = go.AddComponent<Health>(); h.faction = Faction.British; h.maxHealth = 200f; h.regenPerSecond = 5f;
            // airborne kit: Sten gun, a .45 pistol, two Mills bombs and a Gammon bomb, two No. 77 smoke grenades and the first field dressing
            var sten = AddWeapon(go.transform, "Sten Mk II", 34f, 500f, true, 32);
            var colt = Pistol(go.transform, "M1911", 40f);
            var pc = go.AddComponent<SoldierController>();
            pc.weapons = new[] { sten, colt };
            pc.cam = Camera.main;
            var kit = go.AddComponent<Loadout>();
            kit.primary = sten; kit.sidearm = colt;
            kit.grenades = new List<Shell> { millsBomb, millsBomb, gammonBomb };
            kit.smokes = new List<Shell> { smokeNo77, smokeNo77 };
            kit.dressings = 1;
            go.AddComponent<VehicleEntry>();
            go.AddComponent<Ragdoll>();
            if (AddBritishBody(go)) { pc.animator = go.GetComponentInChildren<Animator>(); kit.weaponMount = MountWeapons(go, sten, colt); }
            else BodyBlockout(go.transform, Mat("Denison_Smock", new Color(0.45f, 0.40f, 0.27f)), Mat("Helmet_British", new Color(0.27f, 0.28f, 0.20f)), false);
            return pc;
        }

#if STANS_NET
        static readonly System.Reflection.MethodInfo NetworkObjectValidate = typeof(NetworkObject).GetMethod("OnValidate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        /// <summary>NetworkObject works out its id in OnValidate, which needs the object saved; added from script it stays 0.</summary>
        static void RefreshNetworkId(NetworkObject no)
        {
            NetworkObjectValidate?.Invoke(no, null);
            EditorUtility.SetDirty(no);
        }

        static void RefreshPrefabNetworkId(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset) return;
            foreach (var no in asset.GetComponentsInChildren<NetworkObject>(true)) RefreshNetworkId(no);
            AssetDatabase.SaveAssets();
        }
#endif

        /// <summary>Multiplayer: give a world object a network identity, a host-driven transform and synced health.</summary>
        static void AddNet(GameObject go)
        {
#if STANS_NET
            var netObj = go.GetComponent<NetworkObject>();
            if (!netObj) netObj = go.AddComponent<NetworkObject>();
            netObj.AutoObjectParentSync = false;   // keep our tidy hierarchy (groups are not network objects)
            if (!go.GetComponent<NetworkTransform>())
            {
                var nt = go.AddComponent<NetworkTransform>();
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            }
            if (!go.GetComponent<NetEntity>()) go.AddComponent<NetEntity>();
            var anim = go.GetComponentInChildren<Animator>();
            if (anim && !go.GetComponent<NetworkAnimator>()) go.AddComponent<NetworkAnimator>().Animator = anim;
#endif
        }

#if STANS_NET
        /// <summary>The multiplayer soldier every player gets: same kit as the single-player soldier, driven by its owner.</summary>
        static GameObject BuildNetPlayerPrefab()
        {
            var tmp = new GameObject("tmp");
            var pc = BuildPlayer(tmp.transform);
            var go = pc.gameObject;
            go.transform.SetParent(null);
            go.name = "NetPlayer";
            go.transform.position = Vector3.zero;
            pc.cam = null;          // the owner's Main Camera is attached at spawn
            pc.enabled = false;     // only the owner's copy takes input
            go.AddComponent<NetworkObject>().AutoObjectParentSync = false;
            var nt = go.AddComponent<OwnerNetworkTransform>();
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            go.AddComponent<NetEntity>();
            go.AddComponent<NetPlayer>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/NetPlayer.prefab");
            Object.DestroyImmediate(go); Object.DestroyImmediate(tmp);
            return prefab;
        }
#endif

        static Weapon Pistol(Transform owner, string name, float dmg)
        {
            var w = AddWeapon(owner, name, dmg, 300f, false, name.Contains("1911") ? 7 : 8);
            w.range = 50f; w.hipSpread = 4f; w.aimSpread = 1.4f; w.impactForce = 30f;
            return w;
        }

        static Shell millsBomb, stickGrenade, piatBomb, pak38Shell;

        /// <summary>
        /// Hand grenades. Mills bomb No. 36M: cast-iron, segmented, 4-second fuse, splinters. Stielhandgranate 24:
        /// the German stick grenade, a thin steel can of explosive on a wooden handle, 4.5-second fuse, mostly blast.
        /// </summary>
        static void BuildGrenadePrefabs()
        {
            millsBomb = BuildGrenade("Grenade_Mills", "Mills bomb No. 36", 4.0f, 160f, 9f, false);
            stickGrenade = BuildGrenade("Grenade_Stielhandgranate", "Stielhandgranate 24", 4.5f, 170f, 7f, true);
            piatBomb = BuildShellPrefab("PIAT_Bomb", 0.09f, 500f, 140f, 4f, 77f);
            piatBomb.displayName = "PIAT bomb";
            // 5 cm PaK 38: armour-piercing shell with a small bursting charge (Pzgr 39), 823 m/s
            pak38Shell = BuildShellPrefab("PaK38_Shell", 0.03f, 650f, 90f, 3.5f, 823f);
            pak38Shell.displayName = "5 cm Pzgr 39";
            EditorUtility.SetDirty(piatBomb);
            BuildSmokePrefabs();   // PegasusLevelBuilder.Smoke.cs
        }

        static Shell BuildGrenade(string file, string display, float fuse, float blast, float radius, bool stick)
        {
            string path = $"{PrefabDir}/{file}.prefab";
            var go = new GameObject(file);
            var steel = Mat(stick ? "Grenade_Steel" : "Grenade_Iron", stick ? new Color(0.22f, 0.24f, 0.21f) : new Color(0.24f, 0.26f, 0.20f), 0.3f, 0.5f);
            var model = GrenadeModel(go.transform, stick);
            if (model)
            {
                var c = go.AddComponent<CapsuleCollider>();
                c.radius = stick ? 0.035f : 0.03f; c.height = stick ? 0.36f : 0.095f; c.direction = 1;
            }
            else if (stick)
            {
                Prim(PrimitiveType.Cylinder, "Handle", go.transform, new Vector3(0f, -0.06f, 0f), Vector3.zero, new Vector3(0.035f, 0.14f, 0.035f), Mat("Grenade_Handle", new Color(0.40f, 0.32f, 0.22f)));
                Prim(PrimitiveType.Cylinder, "Head", go.transform, new Vector3(0f, 0.11f, 0f), Vector3.zero, new Vector3(0.07f, 0.05f, 0.07f), steel);
                var c = go.AddComponent<CapsuleCollider>(); c.radius = 0.035f; c.height = 0.36f; c.center = new Vector3(0f, 0.02f, 0f);
            }
            else
            {
                Prim(PrimitiveType.Sphere, "Body", go.transform, Vector3.zero, Vector3.zero, new Vector3(0.06f, 0.09f, 0.06f), steel);
                Prim(PrimitiveType.Cube, "Lever", go.transform, new Vector3(0.03f, 0.01f, 0f), Vector3.zero, new Vector3(0.01f, 0.08f, 0.018f), steel);
                var c = go.AddComponent<CapsuleCollider>(); c.radius = 0.03f; c.height = 0.09f;
            }
            string pmPath = $"{MatDir}/Grenade_Bounce.physicMaterial";
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(pmPath);
            if (!pm) { pm = new PhysicsMaterial("Grenade_Bounce") { bounciness = 0.25f, dynamicFriction = 0.7f, staticFriction = 0.8f }; AssetDatabase.CreateAsset(pm, pmPath); }
            foreach (var c in go.GetComponents<Collider>()) c.sharedMaterial = pm;
            var rb = go.AddComponent<Rigidbody>(); rb.mass = stick ? 0.6f : 0.77f; rb.angularDamping = 0.8f;
            var s = go.AddComponent<Shell>();
            s.displayName = display; s.fuse = fuse; s.directDamage = 0f; s.blastDamage = blast; s.blastRadius = radius; s.blastForce = 350f;
            if (model)
            {
                // the same model, without physics, for a grenade lying on the ground to be picked up
                var vis = new GameObject(file + "_Pickup");
                GrenadeModel(vis.transform, stick);
                s.pickupVisual = PrefabUtility.SaveAsPrefabAsset(vis, $"{PrefabDir}/{file}_Pickup.prefab");
                Object.DestroyImmediate(vis);
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<Shell>();
        }

        /// <summary>The Mills bomb / Stielhandgranate model (from _downloads), centred and at real size.</summary>
        static GameObject GrenadeModel(Transform parent, bool stick)
        {
            string file = stick ? "Stielhandgranate" : "MillsBomb";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/Weapons/{file}.fbx");
            if (!asset) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            Material TexMat(string name, string tex, string normal, Color tint, float smooth)
            {
                var m = Mat(name, tint, smooth);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Models/Weapons/{tex}");
                if (t) { m.SetTexture("_BaseMap", t); m.mainTexture = t; }
                if (normal != null)
                {
                    string np = $"{Root}/Models/Weapons/{normal}";
                    if (AssetImporter.GetAtPath(np) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap) { ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport(); }
                    var n = AssetDatabase.LoadAssetAtPath<Texture2D>(np);
                    if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
                }
                m.SetColor("_BaseColor", tint);
                EditorUtility.SetDirty(m);
                return m;
            }
            var mills = stick ? null : TexMat("MillsBomb", "MillsBomb_BaseColor.jpg", "MillsBomb_Normal.png", Color.white, 0.35f);
            // the stick grenade's texture is a grey detail map: tint it field grey for the steel head, wood for the handle
            var head = stick ? TexMat("Stiel_Head", "Stielhandgranate_BaseColor.jpg", null, new Color(0.36f, 0.39f, 0.33f), 0.3f) : null;
            var handle = stick ? TexMat("Stiel_Handle", "Stielhandgranate_BaseColor.jpg", null, new Color(0.62f, 0.48f, 0.32f), 0.15f) : null;
            var rs = inst.GetComponentsInChildren<Renderer>();
            foreach (var r in rs)
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                    ms[i] = !stick ? mills : (ms[i] && ms[i].name.Contains("Head")) ? head : handle;
                r.sharedMaterials = ms;
            }
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (longest > 0.0001f) inst.transform.localScale = Vector3.one * ((stick ? 0.365f : 0.095f) / longest);   // real sizes
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.position += parent.position - b.center;
            return inst;
        }

        /// <summary>
        /// Weapons lying ready to be picked up (E): a Bren by glider 91 (each section had one), and the PIAT with
        /// its bombs at the west end of the bridge, where D Company's PIAT team waited for the tanks.
        /// </summary>
        static void BuildSupplies(Transform parent)
        {
            var g = Group("Supplies (pick up with E)", parent);
            Weapon Lay(string name, float dmg, float rpm, bool auto, int mag, Vector3 pos, float yaw)
            {
                var holder = new GameObject(name + " (on the ground)").transform; holder.SetParent(g); holder.position = pos;
                var w = AddWeapon(holder, name, dmg, rpm, auto, mag);
                w.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                w.transform.localRotation = Quaternion.Euler(0f, yaw, 90f);
                var p = holder.gameObject.AddComponent<Pickup>(); p.kind = Pickup.Kind.Weapon; p.weapon = w;
                return w;
            }
            Vector3 g91 = GliderRests[0].pos;
            Lay("Bren Mk II", 45f, 520f, true, 28, g91 + new Vector3(-7f, 0f, 2f), 30f);
            var piat = Lay("PIAT", 0f, 20f, false, 1, new Vector3(-CanalHalfWidth - 6f, 0f, 8.5f), -60f);
            piat.range = 120f; piat.hipSpread = 2f; piat.aimSpread = 0.5f; piat.projectile = piatBomb;
            Lay("Lee-Enfield", 70f, 30f, false, 10, g91 + new Vector3(-7.5f, 0f, 0.5f), 80f);
            // a couple of No. 77 smoke grenades thrown out of glider 91 with the kit
            foreach (var off in new[] { new Vector3(-6.4f, 0.05f, 1.2f), new Vector3(-6.9f, 0.05f, 1.6f) })
            {
                var sp = Pickup.SpawnItem(Pickup.Kind.Grenade, smokeNo77, g91 + off);
                if (sp) sp.transform.SetParent(g, true);
            }
        }

        static Shell BuildShellPrefab(string name, float radius, float direct, float blast, float blastRadius, float speedForRb)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.localScale = Vector3.one * radius * 2f;
            go.GetComponent<Renderer>().sharedMaterial = Mat("Shell", new Color(0.2f, 0.2f, 0.18f), 0.3f, 0.6f);
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 1.2f;
            var s = go.AddComponent<Shell>();
            s.directDamage = direct; s.blastDamage = blast; s.blastRadius = blastRadius;
            var light = new GameObject("Glow").AddComponent<Light>(); light.transform.SetParent(go.transform, false);
            light.type = LightType.Point; light.range = 5f; light.intensity = 2f; light.color = new Color(1f, 0.6f, 0.3f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<Shell>();
        }

        /// <summary>
        /// Where the three Horsas came to rest, measured on the RAF vertical of June 1944 (re-measured, scaled by the canal at
        /// the bridge): in a line running south-east into
        /// the triangular field, about 25, 55 and 90 m east of the east-bank road. No. 91 (Howard) slewed round
        /// broadside at the edge of the trees with its nose in the wire; No. 92 broke its back; No. 93 came in last
        /// furthest down the field. yaw = landing heading, slew = how far it swung round at the end.
        /// </summary>
        static readonly (Vector3 pos, float yaw, float slew, GliderApproach.Damage damage, float delay)[] GliderRests =
        {
            // No. 91: on the June 1944 vertical (scaled by the canal at the bridge) its wings span the field ~40 m east of
            // the canal and ~37 m south of the Ranville road; it came in from the south and stopped nose to the north,
            // swung ~13 degrees east, its nose in the wire where the track to the field ends - some 45 m from the bridge
            (new Vector3(CanalHalfWidth + 19.6f, 0f, -37.2f), 0f, 13f, GliderApproach.Damage.NoseStoved, 4f),
            // Nos. 92 and 93 behind it, as on the vertical: both came in from the south and slewed round to end up
            // broadside, noses to the west (their wings run north-south on the photo, tails to the east)
            (new Vector3(CanalHalfWidth + 33.3f, 0f, -59.5f), -10f, -66f, GliderApproach.Damage.BrokenBack, 64f),
            (new Vector3(CanalHalfWidth + 48.7f, 0f, -72.1f), -18f, -57f, GliderApproach.Damage.WheelsTorn, 124f),
        };

        /// <summary>
        /// How each Horsa lay broken (sketches glider_damage_91..93 from pb02, pb03, pb06, pb11, pb12, pb13): No. 91 rolled
        /// onto its starboard wing, nose stove in, the fuselage cracked behind the wing with the tail twisted to port and the
        /// fin leaning, the starboard outer panel bent down into the grass; No. 92 broken in two at the door, the rear half
        /// swung to starboard and over on its side, the cockpit a bare frame, the port outer panel torn and folded to the
        /// ground; No. 93 whole but hard over to port (one tip in the grass, the other high), cockpit glazing gone.
        /// roll: + = port side down.
        /// </summary>
        static readonly (float drop, float pitch, float roll, Vector3 tail, bool crushed, bool bare, float portFold, float stbdFold)[] GliderWrecks =
        {
            (0.75f, 4.5f, -9f, new Vector3(4f, 16f, -10f), true, false, 0f, 13f),
            (0.62f, 1.5f, 6f, new Vector3(-6f, -32f, 35f), false, true, 18f, 0f),
            (0.62f, 1.0f, 15f, Vector3.zero, false, true, 0f, 0f),
        };

        static List<GliderApproach> BuildGliders(Transform lz)
        {
            var list = new List<GliderApproach>();
            var rests = GliderRests;
            if (real == null)
            {
            var pondGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pondGo.name = "Pond"; pondGo.transform.SetParent(lz);
            pondGo.transform.position = new Vector3(CanalHalfWidth + 64f, -0.12f, -70f);
            pondGo.transform.localScale = new Vector3(14f, 0.05f, 9f);
            pondGo.GetComponent<Renderer>().sharedMaterial = WaterShaderMaterial(true) ?? WaterMaterial();
            Object.DestroyImmediate(pondGo.GetComponent<Collider>());
            }

            for (int i = 0; i < rests.Length; i++)
            {
                var marker = new GameObject($"Glider {i + 1} rest point").transform;
                marker.SetParent(lz);
                marker.SetPositionAndRotation(rests[i].pos, Quaternion.Euler(0f, rests[i].yaw, 0f));   // the landing heading
                var g = BuildHorsa($"Horsa glider {i + 1} (chalk {91 + i})", lz, 91 + i);
                g.transform.SetPositionAndRotation(marker.position, marker.rotation);   // shown at its landing spot in the editor; flies in at runtime
                var ga = g.AddComponent<GliderApproach>();
                ga.landingPoint = marker; ga.delay = rests[i].delay;
                ga.slew = rests[i].slew; ga.damage = rests[i].damage;
                var wk = GliderWrecks[i];
                ga.settleDrop = wk.drop; ga.settlePitch = wk.pitch; ga.settleRoll = wk.roll; ga.tailBreak = wk.tail;
                ga.noseCrushed = wk.crushed; ga.noseFrameBare = wk.bare; ga.portWingFold = wk.portFold; ga.starboardWingFold = wk.stbdFold;
                g.transform.rotation = marker.rotation * Quaternion.Euler(0f, rests[i].slew, 0f);   // in the editor: as it ended up
                AddNet(g);
                list.Add(ga);
            }
            return list;
        }

        static Transform BuildTankAttack(Transform parent)
        {
            var group = Group("02:00 Counter-attack (Bénouville road)", parent);
            var waypoints = new List<Transform>();
            var wpts = new[] { BenouvilleCrossroads, Vector3.Lerp(BenouvilleCrossroads, BenouvilleBend, 0.5f), BenouvilleBend, new Vector3(-CanalHalfWidth - 45f, 0f, 0f) };
            for (int i = 0; i < wpts.Length; i++)
            {
                var t = new GameObject($"Tank waypoint {i + 1}").transform; t.SetParent(group); t.position = wpts[i]; waypoints.Add(t);
            }
            // the lead vehicle Thornton faced: a converted Hotchkiss (PaK 40 on the H39/H35 chassis). (The StuG III that
            // followed it is left out for now - BuildArmour can still make one.)
            var tank = BuildArmour("Hotchkiss 7.5 cm PaK 40 (Sf) (lead)", group, BenouvilleCrossroads + new Vector3(0f, 0.2f, -40f), -90f, Armour.HotchkissPak40);
            var tc = tank.GetComponent<TankController>();
            tc.maxForwardSpeed = 7.8f;          // 28 km/h on the road
            tc.maxTraverse = 30f;               // the PaK 40 in Becker's casemate: 30 degrees each way, then the hull turns
            tc.traverseSpeed = 10f; tc.reloadTime = 6f;
            tc.fireSound = "tank_fire_h35"; tc.engineSound = "tank_engine"; tc.trackSound = "tank_tracks"; tc.reloadSound = "tank_reload";
            var ai = tank.AddComponent<TankAI>(); ai.waypoints = waypoints.ToArray();
            ai.cruiseSpeed = 3.5f; ai.bendSpeed = 1.6f;
            return group;
        }

        enum Armour { PanzerIV, HotchkissPak40, StuG }

        /// <summary>A model from VehicleImporter as the visible body, scaled to its real length (FBX unit settings vary).</summary>
        static GameObject PlaceModel(string fbx, Transform parent, float realLength)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{VehicleImporter_Dir}/{fbx}.fbx");
            if (!asset) return null;
            var m = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            m.name = "Model"; m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.identity;
            var rs = m.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float len = Mathf.Max(b.size.x, b.size.z);
                if (len > 0.01f) m.transform.localScale *= realLength / len;
                // the long side must run along +Z (the drive direction)
                if (b.size.x > b.size.z * 1.2f) m.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                var b2 = rs[0].bounds; foreach (var r in rs) b2.Encapsulate(r.bounds);
                m.transform.position += Vector3.up * (parent.position.y - b2.min.y);
            }
            foreach (var c in m.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            return m;
        }
        const string VehicleImporter_Dir = "Assets/StansGame/Models/Vehicles";

        /// <summary>
        /// The textured Panzer IV: the model's turret and gun go under plain pivots (TankController sets their local
        /// rotations outright, which would undo the import rotations of the model's own objects).
        /// </summary>
        static bool PanzerIvModel(Transform t, out Transform turret, out Transform gun, out Transform muzzle)
        {
            turret = gun = muzzle = null;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{VehicleImporter_Dir}/PanzerIV_Camo.fbx");
            if (!asset) return false;
            var m = (GameObject)PrefabUtility.InstantiatePrefab(asset, t);
            PrefabUtility.UnpackPrefabInstance(m, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            m.name = "Model"; m.transform.localPosition = Vector3.zero; m.transform.localRotation = asset.transform.localRotation;
            var tm = FindDeep(m.transform, "PzIV_Turret"); var gm = FindDeep(m.transform, "PzIV_Gun"); var mz = FindDeep(m.transform, "PzIV_Muzzle");
            if (!tm || !gm || !mz) { Object.DestroyImmediate(m); return false; }
            // the gun must point down +Z (the drive direction)
            if (t.InverseTransformPoint(mz.position).z < 0f) m.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * m.transform.localRotation;
            turret = new GameObject("Turret").transform; turret.SetParent(t, false); turret.position = tm.position;
            gun = new GameObject("Gun").transform; gun.SetParent(turret, false); gun.position = gm.position;
            muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(gun, false); muzzle.position = mz.position;
            tm.SetParent(turret, true); gm.SetParent(gun, true);
            return true;
        }

        static GameObject BuildArmour(string name, Transform parent, Vector3 pos, float yaw, Armour kind)
        {
            var t = new GameObject(name); t.transform.SetParent(parent);
            t.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw + 90f, 0f));
            var grey = Mat("Panzer_Dunkelgelb", new Color(0.55f, 0.50f, 0.36f), 0.2f, 0.3f);
            var dark = Mat("Track_Dark", new Color(0.12f, 0.11f, 0.10f), 0.3f, 0.5f);
            Transform turret, gun, muzzle;
            float mass = 25000f, maxHp = 320f, armour = 50f;
            GameObject model = kind == Armour.HotchkissPak40 ? PlaceModel("Hotchkiss_H35_Hull", t.transform, 4.22f)
                             : kind == Armour.StuG ? PlaceModel("StuG_III", t.transform, 6.85f) : null;
            if (kind == Armour.HotchkissPak40)
            {
                // 7.5 cm PaK 40 (Sf) auf Geschützwagen 39H(f): Becker's open-topped fighting compartment on the hull,
                // the PaK 40 firing forward over the front deck. 4.2 m long, 1.85 m wide, ~2.2 m tall; light armour.
                mass = 12800f; maxHp = 200f; armour = 18f;
                if (!model) Prim(PrimitiveType.Cube, "Hull", t.transform, new Vector3(0f, 0.75f, 0f), Vector3.zero, new Vector3(1.85f, 1.1f, 4.2f), grey);
                var hullCol = t.AddComponent<BoxCollider>(); hullCol.center = new Vector3(0f, 0.75f, 0f); hullCol.size = new Vector3(1.85f, 1.3f, 4.2f);
                var plate = Mat("Becker_Plate", new Color(0.50f, 0.46f, 0.33f), 0.15f, 0.3f);
                Prim(PrimitiveType.Cube, "Casemate left", t.transform, new Vector3(-0.85f, 1.75f, -0.5f), Vector3.zero, new Vector3(0.06f, 0.9f, 2.0f), plate, true);
                Prim(PrimitiveType.Cube, "Casemate right", t.transform, new Vector3(0.85f, 1.75f, -0.5f), Vector3.zero, new Vector3(0.06f, 0.9f, 2.0f), plate, true);
                Prim(PrimitiveType.Cube, "Casemate rear", t.transform, new Vector3(0f, 1.65f, -1.5f), Vector3.zero, new Vector3(1.76f, 0.7f, 0.06f), plate, true);
                Prim(PrimitiveType.Cube, "Casemate front", t.transform, new Vector3(0f, 1.8f, 0.5f), new Vector3(-20f, 0f, 0f), new Vector3(1.76f, 1.0f, 0.06f), plate, true);
                turret = new GameObject("Gun mount").transform; turret.SetParent(t.transform, false); turret.localPosition = new Vector3(0f, 1.75f, 0.2f);
                gun = new GameObject("Gun").transform; gun.SetParent(turret, false); gun.localPosition = new Vector3(0f, 0.15f, 0.3f);
                Prim(PrimitiveType.Cylinder, "PaK 40 barrel", gun, new Vector3(0f, 0f, 1.6f), new Vector3(90f, 0f, 0f), new Vector3(0.09f, 1.6f, 0.09f), dark);
                Prim(PrimitiveType.Cube, "Muzzle brake", gun, new Vector3(0f, 0f, 3.2f), Vector3.zero, new Vector3(0.2f, 0.14f, 0.3f), dark);
                muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(gun, false); muzzle.localPosition = new Vector3(0f, 0f, 3.4f);
            }
            else if (kind == Armour.StuG)
            {
                // StuG III Ausf. G: 6.85 m with the gun, 2.95 m wide, 2.16 m tall; the 7.5 cm StuK 40 sits in the casemate
                mass = 23900f; maxHp = 360f; armour = 60f;
                if (!model)
                {
                    Prim(PrimitiveType.Cube, "Hull", t.transform, new Vector3(0f, 0.95f, 0f), Vector3.zero, new Vector3(2.9f, 1.3f, 5.4f), grey);
                    Prim(PrimitiveType.Cube, "Casemate", t.transform, new Vector3(0f, 1.85f, 0.1f), Vector3.zero, new Vector3(2.2f, 0.6f, 2.6f), grey);
                }
                var hullCol = t.AddComponent<BoxCollider>(); hullCol.center = new Vector3(0f, 1.05f, 0f); hullCol.size = new Vector3(2.9f, 2.1f, 5.4f);
                turret = new GameObject("Gun mount").transform; turret.SetParent(t.transform, false); turret.localPosition = new Vector3(0f, 1.6f, 1.4f);
                gun = new GameObject("Gun").transform; gun.SetParent(turret, false);
                if (!model) Prim(PrimitiveType.Cylinder, "Barrel", gun, new Vector3(0f, 0f, 1.3f), new Vector3(90f, 0f, 0f), new Vector3(0.13f, 1.3f, 0.13f), dark);
                muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(gun, false); muzzle.localPosition = new Vector3(0f, 0f, 2.1f);
            }
            else if (PanzerIvModel(t.transform, out turret, out gun, out muzzle))
            {
                // the camouflaged Panzer IV Ausf. H (Models/Vehicles/PanzerIV_Camo.fbx, Models/Source~/panzer_iv_camo.py)
                var hullCol = t.AddComponent<BoxCollider>(); hullCol.center = new Vector3(0f, 1.0f, 0f); hullCol.size = new Vector3(2.9f, 1.9f, 5.8f);
                var turCol = turret.gameObject.AddComponent<BoxCollider>(); turCol.center = new Vector3(0f, 0.45f, 0f); turCol.size = new Vector3(2.2f, 0.9f, 2.4f);
            }
            else
            {
                // Panzer IV Ausf. H blockout: 5.9 m hull, 2.88 m wide, 2.68 m tall, 7.5 cm KwK 40 L/48
                Prim(PrimitiveType.Cube, "Hull", t.transform, new Vector3(0f, 1.2f, 0f), Vector3.zero, new Vector3(2.4f, 1.1f, 5.9f), grey, true);
                Prim(PrimitiveType.Cube, "Track L", t.transform, new Vector3(-1.25f, 0.6f, 0f), Vector3.zero, new Vector3(0.5f, 1.1f, 5.9f), dark, true);
                Prim(PrimitiveType.Cube, "Track R", t.transform, new Vector3(1.25f, 0.6f, 0f), Vector3.zero, new Vector3(0.5f, 1.1f, 5.9f), dark, true);
                turret = new GameObject("Turret").transform; turret.SetParent(t.transform, false); turret.localPosition = new Vector3(0f, 1.95f, 0.3f);
                Prim(PrimitiveType.Cube, "Turret body", turret, new Vector3(0f, 0.4f, 0f), Vector3.zero, new Vector3(1.9f, 0.85f, 2.4f), grey, true);
                gun = new GameObject("Gun").transform; gun.SetParent(turret, false); gun.localPosition = new Vector3(0f, 0.45f, 1.2f);
                Prim(PrimitiveType.Cylinder, "Barrel", gun, new Vector3(0f, 0f, 1.8f), new Vector3(90f, 0f, 0f), new Vector3(0.13f, 1.8f, 0.13f), dark);
                muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(gun, false); muzzle.localPosition = new Vector3(0f, 0f, 3.7f);
            }
            var rb = t.AddComponent<Rigidbody>(); rb.mass = mass; rb.linearDamping = 0.2f; rb.angularDamping = 2f;
            var tc = t.AddComponent<TankController>();
            if (kind != Armour.PanzerIV) AddTrackBelts(t);   // link-by-link tracks: a running belt over them
            AddTrackScroll(t);   // the tracks run with the hull (PegasusLevelBuilder.Drivable.cs)
            tc.turret = turret; tc.gun = gun; tc.muzzle = muzzle;
            tc.shellPrefab = BuildShellPrefab("KwK40_HE", 0.075f, 300f, 150f, 6f, 740f);
            var hp = t.AddComponent<Health>(); hp.faction = Faction.German; hp.maxHealth = maxHp; hp.armour = armour;
            var fire = new GameObject("Burning").AddComponent<Light>(); fire.transform.SetParent(t.transform, false);
            fire.type = LightType.Point; fire.range = 18f; fire.intensity = 0f; fire.color = new Color(1f, 0.5f, 0.2f);
            t.AddComponent<BurnOnDeath>().fireLight = fire;
            AddNet(t);
#if STANS_NET
            foreach (var part in new[] { turret, gun })
            {
                var nt = part.gameObject.AddComponent<NetworkTransform>();
                nt.InLocalSpace = true; nt.SyncPositionX = nt.SyncPositionY = nt.SyncPositionZ = false;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            }
#endif
            return t;
        }

        /// <summary>
        /// The German probing attacks after the bridge is taken (out of play until the mission brings them in):
        /// 1) down the Bénouville road from the village, 2) from Le Port along the west bank, 3) up the east-bank road
        /// from the south. Each a section of riflemen with an NCO (MP 40); the third brings an MG 42 team.
        /// </summary>
        static GameObject[] BuildAttackWaves(Transform parent)
        {
            var waves = new List<GameObject>();
            var plans = new[]
            {
                (name: "Wave 1 (Bénouville road)", from: BenouvilleBend + new Vector3(-55f, 0f, -12f), to: new Vector3(-CanalHalfWidth - 12f, 0f, 1f), n: 6, mg: false),
                (name: "Wave 2 (Le Port, west bank)", from: new Vector3(-CanalHalfWidth - TowpathRoadOffset, 0f, 175f), to: new Vector3(-CanalHalfWidth - 10f, 0f, 14f), n: 6, mg: false),
                (name: "Wave 3 (east-bank road)", from: new Vector3(CanalHalfWidth + TowpathRoadOffset, 0f, -230f), to: new Vector3(CanalHalfWidth + 12f, 0f, -6f), n: 7, mg: true),
            };
            foreach (var w in plans)
            {
                var g = Group(w.name, parent);
                var objective = new GameObject("objective").transform; objective.SetParent(g); objective.position = w.to;
                Vector3 dir = (w.to - w.from); dir.y = 0f; dir.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                for (int i = 0; i < w.n; i++)
                {
                    string role = i == 0 ? "NCO with MP40" : w.mg && i == 1 ? "MG42 gunner" : w.mg && i == 2 ? "MG42 loader" : "rifleman";
                    Vector3 p = w.from - dir * (i / 2) * 4f + side * ((i % 2) * 2f - 1f) * (2f + i * 0.3f);
                    var h = Soldier($"{w.name}: {role}", g, p, Quaternion.LookRotation(dir).eulerAngles.y);
                    var ai = h ? h.GetComponent<SoldierAI>() : null;
                    if (!ai) continue;
                    ai.mode = SoldierAI.Mode.Advance; ai.advanceTarget = objective; ai.startAlert = true;
                    ai.sleepsUntilAlarm = false; ai.patrolRadius = 0f; ai.combatRoam = 8f; ai.skill = 0.4f;
                    ai.preferredRange = role.Contains("MG42") ? 45f : 22f;
                }
                waves.Add(g.gameObject);
            }
            return waves.ToArray();
        }

        /// <summary>Snipers in the village and the château grounds, after the armour is beaten off (long sight, patient).</summary>
        static GameObject BuildSnipers(Transform parent)
        {
            var g = Group("Snipers (village and château grounds)", parent);
            var spots = new[]
            {
                (BenouvilleBend + new Vector3(-20f, 0f, -26f), 70f),
                (BenouvilleBend + new Vector3(-48f, 0f, 22f), 100f),
                (new Vector3(-CanalHalfWidth - 120f, 0f, -95f), 45f),
            };
            int k = 0;
            foreach (var (pos, yaw) in spots)
            {
                var h = Soldier($"Sniper {++k}", g, pos, yaw);
                var ai = h ? h.GetComponent<SoldierAI>() : null;
                if (!ai) continue;
                ai.mode = SoldierAI.Mode.HoldPosition; ai.startAlert = true; ai.sleepsUntilAlarm = false;
                ai.patrolRadius = 0f; ai.combatRoam = 3f; ai.skill = 0.7f; ai.viewRange = 170f; ai.reactionTime = 2.5f;
            }
            return g.gameObject;
        }

        static GameObject BuildParaReinforcements(Transform parent, GameObject prefab, GameObject officerPrefab)
        {
            var g = Group("7th Para Bn reinforcements", parent);
            for (int i = 0; i < 10; i++)
            {
                var s = (GameObject)PrefabUtility.InstantiatePrefab(i == 0 && officerPrefab ? officerPrefab : prefab, g);
                s.name = i == 0 ? "7 Para officer" : $"7 Para {i + 1}";
                s.transform.position = new Vector3(260f + (i % 5) * 2f, 0.2f, -6f - (i / 5) * 3f);
                var ai = s.GetComponent<SoldierAI>();
                ai.mode = SoldierAI.Mode.Advance; ai.startAlert = true;
                var target = new GameObject("7 Para objective").transform; target.SetParent(g);
                target.position = new Vector3(-CanalHalfWidth - 20f - (i % 5) * 3f, 0f, -8f + (i / 5) * 16f);
                ai.advanceTarget = target;
            }
            return g.gameObject;   // MissionDirector keeps them out of play until they arrive
        }

        /// <summary>The Longest Day look and the film scenes (see CinematicDirector).</summary>
        static void BuildCinematics(Transform root, MissionDirector director, Transform env)
        {
            const string profilePath = "Assets/StansGame/Settings/FilmLook.asset";
            Directory.CreateDirectory("Assets/StansGame/Settings");
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(-100f); color.contrast.Override(28f); color.postExposure.Override(0.25f);
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3); grain.intensity.Override(0.7f); grain.response.Override(0.6f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.38f); vig.smoothness.Override(0.5f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();

            var go = new GameObject("Film (The Longest Day)");
            go.transform.SetParent(root);
            var vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10f; vol.weight = 0f; vol.sharedProfile = profile;
            var cine = go.AddComponent<CinematicDirector>();
            cine.mission = director;
            cine.filmLook = vol;
            Transform Mark(string n, Vector3 p) { var t = new GameObject(n).transform; t.SetParent(go.transform); t.position = p; return t; }
            cine.bridgeCentre = Mark("Shot: bridge", new Vector3(0f, 0.5f, 0f));
            cine.cafe = env.Find("Buildings/Café Gondrée");
            cine.junction = Mark("Shot: T-junction", new Vector3(-CanalHalfWidth - 45f, 0f, 0f));
            cine.benouvilleRoad = Mark("Shot: Bénouville road", BenouvilleBend);
            cine.lovatStart = Mark("Lovat's commandos: start", BenouvilleBend);
            cine.lovatEnd = Mark("Lovat's commandos: over the bridge", new Vector3(CanalHalfWidth + 60f, 0f, 0f));
            cine.commandoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/BritishParaOfficer.prefab");
            BuildTowShot(cine, go.transform);   // the Halifax and Horsa 91 on tow, for the cast-off in the intro
            var cam = Camera.main;
            if (cam)
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (!data) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
            }
        }

        static void AddSceneToBuild(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == path)) list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
