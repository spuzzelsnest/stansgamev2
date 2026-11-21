using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if STANS_NET
using Unity.Netcode;
#endif

namespace StansGame.EditorTools
{
    /// <summary>
    /// Chapter 2 of the British sector: Château de Bénouville (6 June 1944, 01:40 to the evening relief).
    ///
    /// The same game frame as Pegasus Bridge (origin on the bridge, +Z along the canal at bearing 28°), on a larger LiDAR
    /// window (Terrain/BenouvilleHeights, 2048 m, IGN RGE ALTI 0.5 m tiles 0461-0462 / 6909-6910; west of E 461000 is
    /// not covered and is filled). The Pegasus world is reused as it is (canal, bridge, café, houses, trees, the wrecked
    /// Horsas); this file adds the south of Bénouville, the Caen road, the château and its park.
    ///
    /// The château: Claude-Nicolas Ledoux, 1770s; 1 avenue de Caen; the park runs to the canal (the chapel is seen from
    /// the towpath); the terrace and parterre on the north side. Position from the park inventory point
    /// (49.236483 N, 0.281356 W, Lambert-93 461071 / 6909012) = game (-172, -801); the modern canal has been widened
    /// and moved west here, so the château is set 145 m from the 1944 canal axis, as it was from the old west bank.
    /// Story (A Company, 7th Para Bn, Major Taylor; Hoeller on the roof; Parr's shells; MacDonald; the Royal Warwicks):
    /// project doc claude/chateau-de-benouville.md. Mission logic: Scripts/Mission/BenouvilleMission.cs.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static (Vector2[] pts, float hw)[] extraRoads;   // more roads for RoadSegments (graded and painted into the terrain)

        static readonly Vector3 ChateauPos = new Vector3(-162f, 0f, -800f);
        static readonly Vector2[] CaenRoad = { new Vector2(-260f, -300f), new Vector2(-264f, -470f), new Vector2(-272f, -640f), new Vector2(-281f, -800f), new Vector2(-292f, -1015f) };
        static readonly Vector3 SouthEdge = new Vector3(-266f, 0f, -560f);       // A Company's first objective: the south end of the village
        static readonly Vector3 TaylorHq = new Vector3(-250f, 0f, -600f);
        static readonly Vector3 AidPost = new Vector3(-247f, 0f, -515f);
        const float TrueNorthYaw = -28f;                                            // the game frame is turned 28° from north

        static float CaenRoadX(float z)
        {
            for (int i = 0; i < CaenRoad.Length - 1; i++)
                if (z <= CaenRoad[i].y && z >= CaenRoad[i + 1].y)
                    return Mathf.Lerp(CaenRoad[i].x, CaenRoad[i + 1].x, Mathf.InverseLerp(CaenRoad[i].y, CaenRoad[i + 1].y, z));
            return z > CaenRoad[0].y ? -260f : CaenRoad[CaenRoad.Length - 1].x;
        }

        [MenuItem("Stan's Game/Build Château de Bénouville Level", priority = 3)]
        public static void BuildBenouville()
        {
            if (!EditorUtility.DisplayDialog("Build Château de Bénouville level",
                "Creates a new scene Assets/StansGame/Scenes/ChateauBenouville.unity (overwrites it if it exists). Continue?", "Build", "Cancel"))
                return;
            EnsureFolders();
            PropTextureFixer.Fix();
            mats.Clear();
            pureGroups.Clear();
            Random.InitState(1940);
            heightsName = "BenouvilleHeights"; terrainAssetName = "BenouvilleTerrain";
            extraRoads = new[] { (CaenRoad, RoadWidth * 0.5f) };
            try { BuildBenouvilleScene(); }
            finally { heightsName = "PegasusHeights"; terrainAssetName = "PegasusTerrain"; extraRoads = null; }
        }

        static void BuildBenouvilleScene()
        {
            if (!LoadRealHeights()) Debug.LogWarning("Stan's Game: Terrain/BenouvilleHeights.r16 not found - flat ground");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Benouville_1944").transform;
            var env = Group("Environment", root);
            var village = Group("Bénouville (south)", env);
            var park = Group("Château de Bénouville", env);
            var lz = Group("LZ X - the wrecked gliders", root);
            var actors = Group("Actors", root);

            // 02:00 BDST: the moon low in the south-south-west (az ~199°, alt ~20°): in the game frame, minus 28°
            BuildLighting(env, "Pegasus", 199f - 28f, 20f);
            if (RenderSettings.sun) RenderSettings.sun.intensity = 0.55f;
            BuildTerrain(env);
            BuildCanal(env);
            var bridgeEnd = BuildBridge(env);
            BuildRoads(env);
            BuildBuildings(env);
            BuildTrees(env);
            PegasusBatch5(env);
            BuildPhotoDetails(env);
            BuildGardens(env);

            // the Horsas lie where they came down two hours before
            var gliders = BuildGliders(lz);
            foreach (var ga in gliders) { ga.Wreck(true); Object.DestroyImmediate(ga); }

            var chateau = BuildChateau(park);
            BuildPark(park, chateau);
            var vion = ChateauWomen(park, chateau);
            BuildSouthVillage(village);

            // kit and soldiers
            BuildGrenadePrefabs();
            GameObject Prefab(string n, System.Func<GameObject> make) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{n}.prefab") ?? make();
            var sten = Prefab("BritishSoldier", () => BuildSoldierPrefab("BritishSoldier", Faction.British, new Color(0.42f, 0.40f, 0.28f), "Sten Mk II", 34f, 500f, true, 32));
            var bren = Prefab("BritishBrenGunner", () => BuildSoldierPrefab("BritishBrenGunner", Faction.British, new Color(0.42f, 0.40f, 0.28f), "Bren Mk II", 45f, 520f, true, 30));
            var rifle = Prefab("BritishRifleman", () => BuildSoldierPrefab("BritishRifleman", Faction.British, new Color(0.42f, 0.40f, 0.28f), "Lee-Enfield", 70f, 30f, false, 10));
            var officer = Prefab("BritishParaOfficer", () => BuildSoldierPrefab("BritishParaOfficer", Faction.British, new Color(0.42f, 0.40f, 0.28f), "Sten Mk II", 34f, 500f, true, 32, true));

            var player = BuildPlayer(actors);
            player.gameObject.name = "Player - A Company, 7th Parachute Battalion";
            player.transform.SetPositionAndRotation(new Vector3(18f, 0.3f, -3f), Quaternion.Euler(0f, -90f, 0f));   // east end of the bridge, facing west
            var kit = player.GetComponent<Loadout>();
            if (kit && gammonBomb) kit.grenades.Add(gammonBomb);   // A Company stopped the armour with Gammon bombs: two of them

            var british = Group("British", actors);
            var section = Group("A Company - the player's section", british);
            GameObject Para(GameObject prefab, string name, Transform parent, Vector3 pos, float yaw)
            {
                var s = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                s.name = name; s.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                var ai = s.GetComponent<SoldierAI>(); if (ai) { ai.startAlert = true; ai.sleepsUntilAlarm = false; }
                return s;
            }
            var mates = new[] { (sten, "Cpl (Sten)"), (bren, "Bren gunner"), (rifle, "Rifleman"), (rifle, "Rifleman"), (sten, "L/Cpl (Sten)") };
            for (int i = 0; i < mates.Length; i++)
            {
                var s = Para(mates[i].Item1, $"A Coy: {mates[i].Item2}", section, new Vector3(22f + i * 1.8f, 0.3f, -6f + (i % 2) * 2.5f), -90f);
                var ai = s.GetComponent<SoldierAI>(); if (ai) ai.mode = SoldierAI.Mode.FollowPlayer;
            }
            // Howard's men hold the bridge and the café
            var howard = Group("D Company, Ox & Bucks (Howard) at the bridge", british);
            var holds = new[] { new Vector3(-30f, 0f, 9f), new Vector3(-31f, 0f, -8f), new Vector3(-48f, 0f, 3f), new Vector3(-42f, 0f, 14f), new Vector3(25f, 0f, 8f), new Vector3(27f, 0f, -9f) };
            for (int i = 0; i < holds.Length; i++) Para(i % 3 == 1 ? bren : i % 2 == 0 ? rifle : sten, "Ox & Bucks: holding the bridge", howard, holds[i], -90f);
            // the rest of A Company: dug in along the south edge of the village and the château grounds once the time
            // jumps forward (out of play until the "hold" phase)
            var aCoy = Group("A Company dug in (from the hold)", british);
            var posts = new List<(Vector3 p, Vector3 watch)>();
            foreach (float z in new[] { -470f, -540f, -610f, -680f })
            {
                float rx = CaenRoadX(z);
                posts.Add((new Vector3(rx - 14f, 0f, z), Vector3.left));
                posts.Add((new Vector3(rx + 9f, 0f, z - 8f), new Vector3(-0.5f, 0f, -0.5f)));
            }
            posts.Add((new Vector3(-215f, 0f, -735f), Vector3.back)); posts.Add((new Vector3(-150f, 0f, -735f), Vector3.back));
            for (int i = 0; i < posts.Count; i++)
                Para(i % 3 == 0 ? bren : i % 2 == 0 ? sten : rifle, $"A Coy: post {i + 1}", aCoy, posts[i].p, Quaternion.LookRotation(posts[i].watch).eulerAngles.y);
            var taylor = Para(officer, "Major Nigel Taylor (OC A Company)", aCoy, TaylorHq + new Vector3(3f, 0f, 2f), -90f);
            // C Company's platoon under Lt MacDonald (about 19:00) and the 2nd Royal Warwicks (evening): from the north
            var macdonald = Group("C Company platoon (Lt MacDonald), 19:00", british);
            for (int i = 0; i < 8; i++) Para(i == 0 ? officer : i % 3 == 0 ? bren : rifle, i == 0 ? "Lt MacDonald" : "C Coy platoon", macdonald, new Vector3(CaenRoadX(-260f) + (i % 2) * 3f - 1.5f, 0f, -240f + i * 3f), 180f);
            var warwicks = Group("2nd Royal Warwickshire Regiment (relief)", british);
            for (int i = 0; i < 12; i++) Para(i % 4 == 0 ? bren : rifle, "2nd Royal Warwicks", warwicks, new Vector3(-260f + (i % 2) * 3f - 1.5f, 0f, 60f + i * 3f), 180f);

            // Germans
            var germans = Group("Germans", actors);
            var inVillage = Group("Germans in the south of the village (02:00)", germans);
            for (int i = 0; i < 8; i++)
            {
                float z = -470f - i * 22f; float x = CaenRoadX(z) + (i % 2 == 0 ? -12f : 11f);
                var h = Soldier(i == 0 ? "NCO with MP40 (village)" : i == 3 ? "MG42 gunner (village)" : "Grenadier (village)", inVillage, new Vector3(x, 0f, z), 0f);
                Wake(h, 6f);
            }
            var inPark = Group("Germans in the château park", germans);
            var parkSpots = new[] { new Vector3(-205f, 0f, -735f), new Vector3(-180f, 0f, -748f), new Vector3(-135f, 0f, -742f), new Vector3(-112f, 0f, -790f), new Vector3(-230f, 0f, -800f), new Vector3(-215f, 0f, -860f) };
            for (int i = 0; i < parkSpots.Length; i++) Wake(Soldier(i == 1 ? "MG42 gunner (park)" : "Grenadier (park)", inPark, parkSpots[i], 20f), 5f);
            // (under the château, not the actors: they stand on the roof, not on the ground)
            var roof = Group("Lt Hoeller's party on the château roof", park);
            var roofMark = chateau ? FindDeep(chateau.transform, "RoofMarker") : null;
            Physics.SyncTransforms();
            if (roofMark)
                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = roofMark.position + roofMark.right * (i - 1) * 1.6f;
                    if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 60f)) p = hit.point + Vector3.up * 0.05f;
                    var bridgeDir = -p; bridgeDir.y = 0f;
                    var h = Soldier(i == 0 ? "Lt Hans Hoeller (NCO with MP40)" : "Grenadier with Hoeller", roof, p, Quaternion.LookRotation(bridgeDir).eulerAngles.y);
                    var ai = h ? h.GetComponent<SoldierAI>() : null; if (ai) ai.enabled = false;
                }
            // the counter-attacks: Kampfgruppe Rauch (Panzergrenadier-Regiment 192) from the west and south-west, all day
            var waves = new List<GameObject>();
            var plans = new[]
            {
                ("Counter-attack 1 (west, over the fields)", new Vector3(-560f, 0f, -520f), new Vector3(-282f, 0f, -540f), 7, false),
                ("Counter-attack 2 (south-west, the orchards)", new Vector3(-560f, 0f, -760f), new Vector3(-290f, 0f, -690f), 7, true),
                ("Counter-attack 3 (south, the Caen road)", new Vector3(-300f, 0f, -1000f), new Vector3(-275f, 0f, -700f), 8, true),
                ("Counter-attack 4 (north-west)", new Vector3(-560f, 0f, -330f), new Vector3(-280f, 0f, -480f), 7, false),
                ("Counter-attack 5 (south-west, evening)", new Vector3(-520f, 0f, -900f), new Vector3(-240f, 0f, -740f), 8, true),
                ("Counter-attack 6 (west, evening)", new Vector3(-580f, 0f, -620f), new Vector3(-285f, 0f, -600f), 8, true),
            };
            foreach (var w in plans) waves.Add(AttackWave(germans, w.Item1, w.Item2, w.Item3, w.Item4, w.Item5));
            var armour = BenouvilleArmour(germans);
            Kubelwagen(germans, "German Kübelwagen (parked south of the château)", new Vector3(-252f, 0f, -930f), 8f);

            // mission
            var mission = new GameObject("BenouvilleMission").AddComponent<BenouvilleMission>();
            mission.transform.SetParent(root);
            mission.player = player;
            mission.section = section.gameObject;
            mission.bridge = Mark("Bridge (west end)", root, new Vector3(-30f, 0f, 0f));
            mission.southEdge = Mark("South edge of Bénouville", root, SouthEdge);
            mission.chateau = chateau ? chateau.transform : Mark("Château", root, ChateauPos);
            mission.parkCentre = Mark("Château park", root, new Vector3(-180f, 0f, -770f));
            mission.taylorHq = Mark("A Company HQ", root, TaylorHq);
            mission.aidPost = Mark("Regimental Aid Post", root, AidPost);
            mission.villageGermans = inVillage.gameObject; mission.parkGermans = inPark.gameObject; mission.roofParty = roof.gameObject;
            mission.aCompanyDugIn = aCoy.gameObject; mission.taylor = taylor ? taylor.GetComponent<SoldierAI>() : null;
            mission.macdonald = macdonald.gameObject; mission.warwicks = warwicks.gameObject;
            mission.counterAttacks = waves.ToArray();
            mission.armour = armour.gameObject;
            mission.parrGun = FindDeepByPrefix(root, "PaK") ?? FindDeepByPrefix(root, "Pak");
            mission.vion = vion;
            mission.reliefTarget = Mark("Relief: the south of the village", root, new Vector3(-266f, 0f, -600f));

            if (real != null)
            {
                Physics.SyncTransforms();
                SnapToGround(env.Find("Buildings"));
                SnapToGround(env.Find("Poplars and hedges"));
                SnapToGround(village);
                SnapToGround(lz);
                SnapToGround(actors);
                foreach (Transform t in mission.transform.parent) if (t.name.StartsWith("Mark: ")) t.position = new Vector3(t.position.x, GroundAt(t.position), t.position.z);
                RescueFromWater(root);
            }
            ColourGrade(root, "PegasusGrade");   // the same grade as Pegasus Bridge
            mission.filmLook = FilmLookVolume(root);
#if STANS_NET
            // co-op: the shared mission state and the combat events travel on the mission's network object
            mission.gameObject.AddComponent<NetworkObject>().AutoObjectParentSync = false;
            mission.gameObject.AddComponent<BenouvilleMissionNet>();
            var combat = mission.gameObject.AddComponent<NetCombat>();
            var shells = new List<Shell>();
            foreach (var sh in new[] { AssetDatabase.LoadAssetAtPath<Shell>($"{PrefabDir}/PIAT_Bomb.prefab"), AssetDatabase.LoadAssetAtPath<Shell>($"{PrefabDir}/KwK40_HE.prefab"),
                                       millsBomb, stickGrenade, pak38Shell, smokeNo77, smokeNb39, smokeM8, gammonBomb })
                if (sh && !shells.Contains(sh)) shells.Add(sh);
            combat.projectilePrefabs = shells.ToArray();
#endif
            var bld = env.Find("Buildings"); if (bld) WindowLightsFor(bld, 6645, 0.15f, 0.1f, Vector3.zero, 12);
            WindowLightsFor(village, 6646, 0.2f, 0.1f, SouthEdge, 16);

            var cam = Camera.main;
            if (cam) { cam.transform.position = new Vector3(60f, 25f, -60f); cam.transform.LookAt(new Vector3(-150f, 5f, -600f)); player.cam = cam; }

            string path = SceneDir + "/ChateauBenouville.unity";
            EditorSceneManager.SaveScene(scene, path);
#if STANS_NET
            // network ids can only be worked out once the scene is on disk: do it now and save again
            int nets = 0;
            foreach (var no in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { RefreshNetworkId(no); nets++; }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"Stan's Game: Château de Bénouville co-op - {nets} networked objects");
#endif
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == path)) list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("Stan's Game: Château de Bénouville level built at " + path);
        }

        /// <summary>The black-and-white film look of Pegasus Bridge's cutscenes (Settings/FilmLook.asset, made by BuildCinematics).</summary>
        static UnityEngine.Rendering.Volume FilmLookVolume(Transform root)
        {
            const string profilePath = "Assets/StansGame/Settings/FilmLook.asset";
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(profilePath);
            if (!profile)
            {
                System.IO.Directory.CreateDirectory("Assets/StansGame/Settings");
                profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
                var color = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
                color.saturation.Override(-100f); color.contrast.Override(28f); color.postExposure.Override(0.25f);
                var grain = profile.Add<UnityEngine.Rendering.Universal.FilmGrain>(true);
                grain.type.Override(UnityEngine.Rendering.Universal.FilmGrainLookup.Medium3); grain.intensity.Override(0.7f); grain.response.Override(0.6f);
                var vig = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
                vig.intensity.Override(0.38f); vig.smoothness.Override(0.5f);
                foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            }
            var go = new GameObject("Film (The Longest Day)");
            go.transform.SetParent(root);
            var vol = go.AddComponent<UnityEngine.Rendering.Volume>(); vol.isGlobal = true; vol.priority = 10f; vol.weight = 0f; vol.sharedProfile = profile;
            return vol;
        }

        static void Wake(Health h, float roam)
        {
            var ai = h ? h.GetComponent<SoldierAI>() : null;
            if (!ai) return;
            ai.sleepsUntilAlarm = false; ai.startAlert = true; ai.patrolRadius = 0f; ai.combatRoam = roam; ai.skill = 0.35f;
        }

        static Transform Mark(string name, Transform parent, Vector3 p)
        {
            var t = new GameObject("Mark: " + name).transform; t.SetParent(parent); t.position = p; return t;
        }

        static Transform FindDeepByPrefix(Transform t, string prefix)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name.StartsWith(prefix)) return c;
            return null;
        }

        static GameObject AttackWave(Transform parent, string name, Vector3 from, Vector3 to, int n, bool mg)
        {
            var g = Group(name, parent);
            var objective = new GameObject("objective").transform; objective.SetParent(g); objective.position = to;
            Vector3 dir = to - from; dir.y = 0f; dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            for (int i = 0; i < n; i++)
            {
                string role = i == 0 ? "NCO with MP40" : mg && i == 1 ? "MG42 gunner" : mg && i == 2 ? "MG42 loader" : "Panzergrenadier";
                Vector3 p = from - dir * (i / 2) * 5f + side * ((i % 2) * 2f - 1f) * (3f + i * 0.6f);
                var h = Soldier($"{name}: {role}", g, p, Quaternion.LookRotation(dir).eulerAngles.y);
                var ai = h ? h.GetComponent<SoldierAI>() : null;
                if (!ai) continue;
                ai.mode = SoldierAI.Mode.Advance; ai.advanceTarget = objective; ai.startAlert = true;
                ai.sleepsUntilAlarm = false; ai.patrolRadius = 0f; ai.combatRoam = 8f; ai.skill = 0.4f;
                ai.preferredRange = role.Contains("MG42") ? 50f : 24f;
            }
            return g.gameObject;
        }

        /// <summary>The armour A Company knocked out with Gammon bombs: a Panzer IV and a self-propelled gun, up the Caen road.</summary>
        static Transform BenouvilleArmour(Transform parent)
        {
            var group = Group("Armour on the Caen road (Panzer IV and a self-propelled gun)", parent);
            var wps = new List<Transform>();
            foreach (var z in new[] { -940f, -800f, -690f, -600f, -520f })
            {
                var t = new GameObject($"Tank waypoint z{z}").transform; t.SetParent(group); t.position = new Vector3(CaenRoadX(z), 0f, z); wps.Add(t);
            }
            var specs = new[] { ("Panzer IV Ausf. H (21st Panzer Division)", Armour.PanzerIV, -1010f), ("7.5 cm PaK 40 (Sf) self-propelled gun", Armour.HotchkissPak40, -1035f) };
            foreach (var (name, kind, z) in specs)
            {
                var pos = new Vector3(CaenRoadX(z), 0.3f, z);
                var t = BuildArmour(name, group, pos, 0f, kind);
                t.transform.rotation = Quaternion.LookRotation(new Vector3(CaenRoadX(z + 20f), 0f, z + 20f) - pos);
                var tc = t.GetComponent<TankController>();
                tc.maxForwardSpeed = 6f; tc.reloadTime = 7f;
                tc.fireSound = "tank_fire_h35"; tc.engineSound = "tank_engine"; tc.trackSound = "tank_tracks"; tc.reloadSound = "tank_reload";
                var ai = t.AddComponent<TankAI>(); ai.waypoints = wps.ToArray(); ai.cruiseSpeed = 3f; ai.bendSpeed = 1.5f; ai.engageRange = 140f;
            }
            return group;
        }

        // ---------------------------------------------------------------- the château

        static GameObject BuildChateau(Transform park)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Town/Chateau/Chateau_Benouville.fbx");
            if (!asset) { Debug.LogWarning("Stan's Game: Chateau_Benouville.fbx not found"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, park);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Château de Bénouville (Ledoux, 1770s) - maternity home";
            go.transform.position = ChateauPos;
            // turn it so the terrace and parterre face true north (the portico to the south, the entrance court)
            var portico = FindDeep(go.transform, "PorticoMarker"); var terrace = FindDeep(go.transform, "TerraceMarker");
            if (portico && terrace)
            {
                Vector3 now = terrace.position - portico.position; now.y = 0f;
                float turn = Vector3.SignedAngle(now, Quaternion.Euler(0f, TrueNorthYaw, 0f) * Vector3.forward, Vector3.up);
                go.transform.rotation = Quaternion.Euler(0f, turn, 0f) * go.transform.rotation;
            }
            go.transform.position = new Vector3(ChateauPos.x, GroundAt(ChateauPos) + 0.05f, ChateauPos.z);
            var glass = GlassMat();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name.EndsWith("Marker")) continue;
                var r = mf.GetComponent<Renderer>();
                if (r)
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) if (ms[i] && ms[i].name.Contains("Glass")) ms[i] = glass;
                    r.sharedMaterials = ms;
                }
                if (!mf.GetComponent<Collider>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            // the Red Cross flag Madame Vion flew over the maternity home
            var rc = FindDeep(go.transform, "RedCrossMarker");
            Vector3 fp = rc ? rc.position : go.transform.position + Vector3.up * 22f;
            var pole = Mat("Flagpole", new Color(0.85f, 0.85f, 0.82f), 0.4f);
            var flagRoot = new GameObject("Red Cross flag (maternity home)").transform; flagRoot.SetParent(park); flagRoot.position = fp;
            Prim(PrimitiveType.Cylinder, "Pole", flagRoot, new Vector3(0f, 2.5f, 0f), Vector3.zero, new Vector3(0.08f, 2.5f, 0.08f), pole, false);
            Prim(PrimitiveType.Quad, "Flag", flagRoot, new Vector3(1.0f, 4.3f, 0f), Vector3.zero, new Vector3(2.0f, 1.4f, 1f), RedCrossMat(), false);
            Prim(PrimitiveType.Quad, "Flag (back)", flagRoot, new Vector3(1.0f, 4.3f, 0f), new Vector3(0f, 180f, 0f), new Vector3(2.0f, 1.4f, 1f), RedCrossMat(), false);
            flagRoot.rotation = Quaternion.Euler(0f, 60f, 0f);
            return go;
        }

        static Material RedCrossMat()
        {
            var tex = new Texture2D(64, 44, TextureFormat.RGBA32, false);
            for (int y = 0; y < 44; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool cross = (Mathf.Abs(x - 32) < 5 && Mathf.Abs(y - 22) < 15) || (Mathf.Abs(y - 22) < 5 && Mathf.Abs(x - 32) < 15);
                    tex.SetPixel(x, y, cross ? new Color(0.75f, 0.05f, 0.05f) : new Color(0.92f, 0.92f, 0.88f));
                }
            tex.Apply();
            string path = MatDir + "/RedCross_Flag.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var m = Mat("RedCross_Flag", Color.white, 0.1f);
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            m.SetTexture("_BaseMap", t); m.mainTexture = t;
            m.SetFloat("_Cull", 0f);
            return m;
        }

        /// <summary>The park: a wall along the Caen road with the gate, the drive with its trees, the parterre, lawns to the canal.</summary>
        static void BuildPark(Transform park, GameObject chateau)
        {
            var stone = Mat("Park_Wall_Stone", new Color(0.62f, 0.58f, 0.50f), 0.1f);
            var bark = Mat("Bark", new Color(0.20f, 0.17f, 0.13f));
            var broad = Mat("Broadleaf_Leaves", new Color(0.08f, 0.14f, 0.07f));
            var walls = Group("Park walls", park);
            // along the road (east side of it), with the gate opposite the château
            float z0 = -700f, z1 = -905f, gateZ = ChateauPos.z;
            for (float z = z0; z > z1; z -= 10f)
            {
                float za = z, zb = Mathf.Max(z - 10f, z1);
                if (Mathf.Abs((za + zb) * 0.5f - gateZ) < 6f) continue;   // the gate
                var a = new Vector3(CaenRoadX(za) + 9f, 0f, za); var b = new Vector3(CaenRoadX(zb) + 9f, 0f, zb);
                var w = Box("Park wall (Caen road)", walls, (a + b) * 0.5f + Vector3.up * 1.2f, new Vector3(0.6f, 2.4f, Vector3.Distance(a, b) + 0.3f), stone);
                w.transform.rotation = Quaternion.LookRotation(b - a);
            }
            foreach (var s in new[] { -1f, 1f })
                Box("Gate pier", walls, new Vector3(CaenRoadX(gateZ) + 9f, 1.6f, gateZ + s * 6f), new Vector3(1.1f, 3.2f, 1.1f), stone);
            // north and south walls, from the road to the towpath
            foreach (var z in new[] { z0, z1 })
            {
                float xa = CaenRoadX(z) + 9f, xb = -42f;
                for (float x = xa; x < xb; x += 12f)
                {
                    float xe = Mathf.Min(x + 12f, xb);
                    if (z == z0 && Mathf.Abs((x + xe) * 0.5f - (-200f)) < 7f) continue;   // a door into the park from the village
                    Box("Park wall", walls, new Vector3((x + xe) * 0.5f, 1.2f, z), new Vector3(xe - x + 0.3f, 2.4f, 0.6f), stone);
                }
            }
            // the drive from the gate to the château, lined with lime trees
            var trees = Group("Drive and park trees", park);
            Vector3 gate = new Vector3(CaenRoadX(gateZ) + 9f, 0f, gateZ);
            Vector3 front = chateau ? chateau.transform.position : ChateauPos;
            Vector3 d = front - gate; d.y = 0f; float len = d.magnitude - 30f; d.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, d);
            for (float t = 8f; t < len; t += 11f)
                foreach (var s in new[] { -1f, 1f }) Broadleaf(trees, gate + d * t + side * s * 6.5f, bark, broad);
            // park trees scattered towards the canal, clear of the château and the parterre
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector3(Random.Range(-250f, -50f), 0f, Random.Range(z1 + 8f, z0 - 8f));
                if (Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(front.x, 0f, front.z)) < 55f) continue;
                if (Mathf.Abs(Vector3.Dot(p - gate, side)) < 10f && Vector3.Dot(p - gate, d) > 0f && Vector3.Dot(p - gate, d) < len) continue;
                Broadleaf(trees, p, bark, broad);
            }
            // the parterre north of the terrace: box hedges in a grid
            var terrace = chateau ? FindDeep(chateau.transform, "TerraceMarker") : null;
            if (terrace)
            {
                var parterre = Group("Parterre (box hedges)", park);
                Vector3 n = terrace.position - front; n.y = 0f; n.Normalize();
                Vector3 e = Vector3.Cross(Vector3.up, n);
                Vector3 c = terrace.position + n * 22f; c.y = 0f;
                for (int i = -3; i <= 3; i++)
                    for (int j = 0; j < 4; j++)
                        if (i != 0) BuxusBush(parterre, c + e * i * 5f + n * j * 5f, 0.7f, Quaternion.LookRotation(n).eulerAngles.y);
            }
        }

        /// <summary>The south of the village along the Caen road: houses and walled gardens both sides, the aid post.</summary>
        static void BuildSouthVillage(Transform village)
        {
            // the château's Caen stone and slate textures on the village too (tiled over the box faces)
            string cd = Root + "/Models/Town/Chateau";
            Material Tiled(string name, string tex, Color tint, Vector2 tiling)
            {
                var m = TexMat(name, $"{cd}/{tex}", 0.08f, 0f, tint);
                m.SetTextureScale("_BaseMap", tiling); m.mainTextureScale = tiling;
                var n = AssetDatabase.LoadAssetAtPath<Texture2D>($"{cd}/{tex.Replace("_d.", "_n.")}");
                if (n && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(n)) is TextureImporter ti && ti.textureType == TextureImporterType.NormalMap)
                { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetTextureScale("_BumpMap", tiling); }
                EditorUtility.SetDirty(m);
                return m;
            }
            var render = Tiled("Village_Render", "Chateau_Stone_d.png", new Color(0.86f, 0.83f, 0.76f), new Vector2(2f, 1.5f));
            var stoneWall = Tiled("Village_Stone", "Chateau_Rustic_d.png", Color.white, new Vector2(2.5f, 2f));
            var roof = Tiled("Village_Slate", "Chateau_Slate_d.png", Color.white, new Vector2(3f, 2f));
            var tile = Mat("Roof_Tile", new Color(0.36f, 0.20f, 0.15f));
            var door = Mat("Door_Wood", new Color(0.22f, 0.16f, 0.11f), 0.2f);
            var garden = Mat("Park_Wall_Stone", new Color(0.62f, 0.58f, 0.50f), 0.1f);
            int k = 0;
            for (float z = -320f; z > -690f; z -= Random.Range(16f, 26f))
            {
                foreach (var s in new[] { -1f, 1f })
                {
                    if (Random.value < 0.22f) continue;   // a gap: a garden, a yard
                    float x = CaenRoadX(z) + s * Random.Range(11f, 14f);
                    var size = new Vector3(Random.Range(8f, 13f), Random.Range(6f, 8.5f), Random.Range(7f, 9f));
                    float yaw = s < 0f ? 90f : -90f;   // the long side to the road
                    var hs = House($"House on the Caen road {++k}", village, new Vector3(x, 0f, z), size, Random.value < 0.5f ? render : stoneWall, Random.value < 0.7f ? roof : tile, yaw);
                    // a door on the road side (local -z faces the road for these yaws... both long sides get one)
                    Prim(PrimitiveType.Cube, "Door", hs.transform, new Vector3(size.x * 0.2f, 1.1f, -size.z / 2f - 0.03f), Vector3.zero, new Vector3(1.1f, 2.2f, 0.06f), door, false);
                    Prim(PrimitiveType.Cube, "Door", hs.transform, new Vector3(-size.x * 0.25f, 1.1f, size.z / 2f + 0.03f), Vector3.zero, new Vector3(1.1f, 2.2f, 0.06f), door, false);
                    // the garden wall behind
                    Box("Garden wall", village, new Vector3(x + s * (size.z * 0.5f + 12f), 0.9f, z), new Vector3(0.5f, 1.8f, size.x + 6f), garden);
                }
            }
            // the Regimental Aid Post (overrun during the day; the padre, Capt Parry, was killed)
            var rap = House("Regimental Aid Post, 7th Para Bn", village, AidPost, new Vector3(12f, 7f, 9f), stoneWall, roof, -90f);
            Prim(PrimitiveType.Cube, "Red Cross sign", rap.transform, new Vector3(0f, 3.2f, -4.6f), Vector3.zero, new Vector3(1.6f, 1.2f, 0.05f), RedCrossMat(), false);
            // orchards and hedges to the west, where the counter-attacks came from
            var orchards = Group("Orchards west of the village", village);
            var bark = Mat("Bark", new Color(0.20f, 0.17f, 0.13f));
            var apple = Mat("Orchard_Leaves", new Color(0.11f, 0.17f, 0.09f));
            var hedge = Mat("Hedge", new Color(0.09f, 0.14f, 0.07f));
            for (float z = -360f; z > -860f; z -= 11f)
                for (float x = -330f; x > -420f; x -= 11f)
                    if (Random.value < 0.75f && Mathf.Abs(z + 620f) > 12f) AppleTree(orchards, new Vector3(x + Random.Range(-1.5f, 1.5f), 0f, z + Random.Range(-1.5f, 1.5f)), bark, apple);
            foreach (var z in new[] { -350f, -480f, -620f, -760f, -880f })
                Box("Hedge", orchards, new Vector3(-400f, 1.1f, z), new Vector3(170f, 2.2f, 1.6f), hedge, 0f, true);
            Box("Hedge", orchards, new Vector3(-318f, 1.1f, -610f), new Vector3(1.6f, 2.2f, 520f), hedge, 0f, true);
        }
    }
}
