using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// The Proving Ground (Scenes/ProvingGround.unity), the last option on the main menu: a quiet summer's day on a
    /// training field to look at and try everything in the game without a mission.
    ///   Training station - a long table under a canvas awning with every gun, and a small one with every grenade and
    ///                      smoke (E to pick up; a fresh one is laid back a few seconds later), a firing range behind
    ///                      it with boards at 25, 50 and 100 m.
    ///   Vehicle park     - every vehicle side by side with room to walk between, all drivable (E).
    ///   Overhead         - a vic of C-47s now and then.
    ///   The characters   - every soldier and civilian in the game strolling about with a name tag, peaceful
    ///                      (their AI is off: nobody fights). Shoot one to try his ragdoll.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string ProvingScene = "ProvingGround";

        [MenuItem("Stan's Game/Build Proving Ground", priority = 4)]
        public static void BuildProvingGround()
        {
            if (!ConfirmBuild("Build the Proving Ground",
                $"Creates a new scene {SceneDir}/{ProvingScene}.unity (overwrites it if it exists). Continue?")) return;
            EnsureFolders();
            mats.Clear();
            Random.InitState(1944);
            PropTextureFixer.Fix();   // the vehicles' textures (the Panzer IV's camouflage among them)
            flatGround = true;
            try { BuildProvingGroundScene(); }
            finally { flatGround = false; terrainRef = null; }   // the next level builds its own ground
        }

        static void BuildProvingGroundScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("ProvingGround").transform;
            var env = Group("Environment", root);
            var actors = Group("Actors", root);

            // ------------------------------------------------ a summer's day
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(env); sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.color = new Color(1f, 0.95f, 0.86f); sun.intensity = 1.25f; sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            string skyPath = Root + "/Materials/ProvingGround_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (!sky)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                if (!AssetDatabase.IsValidFolder(Root + "/Materials")) AssetDatabase.CreateFolder(Root, "Materials");
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetFloat("_SunSize", 0.04f); sky.SetFloat("_AtmosphereThickness", 0.9f); sky.SetFloat("_Exposure", 1.2f);
            sky.SetColor("_SkyTint", new Color(0.5f, 0.6f, 0.75f)); sky.SetColor("_GroundColor", new Color(0.37f, 0.36f, 0.33f));
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox; RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 250f; RenderSettings.fogEndDistance = 1200f;
            RenderSettings.fogColor = new Color(0.72f, 0.78f, 0.85f);
            pgWorn.Clear();
            PgLight(root, sun, sky);   // late afternoon, and the Pegasus Bridge colour grade
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera"; cam.gameObject.AddComponent<AudioListener>();
            cam.transform.SetParent(env); cam.farClipPlane = 2500f; cam.clearFlags = CameraClearFlags.Skybox;
            cam.transform.position = new Vector3(0f, 8f, -30f); cam.transform.LookAt(new Vector3(10f, 0f, 5f));

            // ------------------------------------------------ the field
            var turf = Mat("PG_Turf", Color.white, 0.12f);
            var turfTex = GroundTex("Ground_Turf_Albedo.jpg", TextureImporterType.Default, true);
            if (turfTex) { turf.SetTexture("_BaseMap", turfTex); turf.SetTextureScale("_BaseMap", new Vector2(150f, 150f)); }
            else turf.SetColor("_BaseColor", new Color(0.33f, 0.42f, 0.22f));
            ProvingTerrain(env, turfTex);
            var gravel = Mat("PG_Gravel", Color.white, 0.2f);
            var gravelTex = GroundTex("Ground_Gravel_Albedo.jpg", TextureImporterType.Default, true);
            if (gravelTex) { gravel.SetTexture("_BaseMap", gravelTex); gravel.SetTextureScale("_BaseMap", new Vector2(8f, 3f)); }
            else gravel.SetColor("_BaseColor", new Color(0.5f, 0.48f, 0.44f));
            // (the gravel of the station apron, the roads and the motor pool is painted on the ground: PaintProvingGround)

            // ------------------------------------------------ training station: the gun table under an awning
            var station = Group("Training station", root);
            var wood = Mat("PG_Wood", new Color(0.42f, 0.3f, 0.18f), 0.2f);
            var canvas = Mat("PG_Canvas", new Color(0.46f, 0.44f, 0.32f), 0.1f);
            var white = Mat("PG_Paint_White", new Color(0.88f, 0.86f, 0.8f), 0.2f);
            BuildGrenadePrefabs();   // the grenades on the tables, the PIAT's and bazooka's bombs, the player's grenades
            // three tables, one for each army, with a walkway between them; each army's weapons and grenades together
            var armies = new (string title, float x, (string name, float dmg, float rpm, bool auto, int mag)[] guns, string[] throws)[]
            {
                ("BRITISH", -4.9f, new[] { ("Lee-Enfield", 70f, 30f, false, 10), ("Sten Mk II", 34f, 500f, true, 32), ("Bren Mk II", 45f, 520f, true, 28), ("PIAT", 0f, 20f, false, 1) },
                    new[] { "Grenade_Mills_Pickup", "Grenade_No82_Gammon_Pickup", "Smoke_No77_Pickup" }),
                ("AMERICAN", 0f, new[] { ("M1 Garand rifle", 70f, 120f, false, 8), ("M1A1 carbine", 45f, 150f, false, 15), ("Thompson M1A1", 34f, 700f, true, 30), ("M1911", 40f, 300f, false, 7), ("Bazooka M1A1", 0f, 20f, false, 1) },
                    new[] { "Grenade_MkII_Pickup", "Smoke_ANM8_Pickup" }),
                ("GERMAN", 4.9f, new[] { ("Kar98k", 55f, 30f, false, 5), ("MP40", 30f, 500f, true, 32), ("MG42", 20f, 1200f, true, 50), ("Luger P08", 32f, 300f, false, 8) },
                    new[] { "Grenade_Stielhandgranate_Pickup", "Smoke_Nb39_Pickup" }),
            };
            const float tableLen = 3.7f;
            foreach (var army in armies)
            {
                Prim(PrimitiveType.Cube, $"Table ({army.title.ToLowerInvariant()})", station, new Vector3(army.x, 0.86f, 0f), Vector3.zero, new Vector3(tableLen, 0.08f, 1.2f), wood, true);
                foreach (float lx in new[] { -tableLen / 2f + 0.15f, tableLen / 2f - 0.15f }) foreach (float z in new[] { -0.5f, 0.5f })
                    Prim(PrimitiveType.Cube, "Table leg", station, new Vector3(army.x + lx, 0.41f, z), Vector3.zero, new Vector3(0.08f, 0.82f, 0.08f), wood, true);
                // the army's name on a little board standing at the back of its table
                Prim(PrimitiveType.Cube, "Name board", station, new Vector3(army.x, 1.12f, 0.55f), Vector3.zero, new Vector3(1.3f, 0.36f, 0.04f), white);
                FitLabel(station, army.title, new Vector3(army.x, 1.12f, 0.525f), 1.15f, 0.26f, new Color(0.1f, 0.1f, 0.1f), Quaternion.identity);
            }
            foreach (float x in new[] { -7.2f, -2.45f, 2.45f, 7.2f }) foreach (float z in new[] { -1.6f, 1.6f })
                Prim(PrimitiveType.Cylinder, "Awning post", station, new Vector3(x, 1.4f, z), Vector3.zero, new Vector3(0.12f, 1.4f, 0.12f), wood, true);
            Prim(PrimitiveType.Cube, "Awning (canvas)", station, new Vector3(0f, 2.85f, 0f), new Vector3(6f, 0f, 0f), new Vector3(15.2f, 0.05f, 3.8f), canvas);
            var board = Prim(PrimitiveType.Cube, "Sign board", station, new Vector3(0f, 3.25f, -1.75f), Vector3.zero, new Vector3(5.2f, 0.7f, 0.06f), wood);
            Label(station, "TRAINING STATION  -  ARMOURY", new Vector3(0f, 3.33f, -1.79f), 0.05f, Color.white, Quaternion.identity);
            Label(station, "E: pick up  ·  a fresh one is put back after a few seconds", new Vector3(0f, 3.1f, -1.79f), 0.02f, new Color(0.95f, 0.9f, 0.75f), Quaternion.identity);

            var table = new GameObject("Armoury (weapons on the tables)").AddComponent<ArmouryTable>();
            table.transform.SetParent(station);
            var slots = new List<ArmouryTable.Slot>();
            var ink = new Color(0.15f, 0.12f, 0.08f);
            foreach (var army in armies)
            {
                float x = army.x - tableLen / 2f + 0.4f;
                foreach (var g in army.guns)
                {
                    var prefab = ArmouryWeapon(g.name, g.dmg, g.rpm, g.auto, g.mag);
                    if (!prefab) continue;
                    slots.Add(new ArmouryTable.Slot { prefab = prefab, position = new Vector3(x, 0.95f, 0.05f), rotation = Quaternion.Euler(0f, 0f, 90f) });   // on its side, muzzle to the range
                    FitLabel(station, g.name, new Vector3(x, 0.905f, -0.47f), 0.5f, 0.12f, ink, Quaternion.Euler(90f, 0f, 0f));
                    x += 0.56f;
                }
                foreach (var t in army.throws)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{t}.prefab");
                    if (!prefab) continue;
                    slots.Add(new ArmouryTable.Slot { prefab = prefab, position = new Vector3(x - 0.08f, 0.97f, 0.1f), rotation = Quaternion.identity });
                    FitLabel(station, t.Replace("_Pickup", "").Replace("Grenade_", "").Replace("Smoke_", "Smoke ").Replace("_", " "), new Vector3(x - 0.08f, 0.905f, -0.45f), 0.32f, 0.1f, ink, Quaternion.Euler(90f, 0f, 0f));
                    x += 0.34f;
                }
            }
            table.slots = slots.ToArray();

            // beside each table, that army's main character (the soldier the player plays) on show: he stays on his
            // spot and goes through his animations, the name of each over his head (AnimationShowcase)
            var protagonists = BuildProtagonists();
            string[] who = { "British para (player)", "US paratrooper (player)", "German rifleman (player-to-be)" };
            var showSpots = new List<Bounds>();
            for (int i = 0; i < armies.Length && i < protagonists.Length; i++)
            {
                if (!protagonists[i]) continue;
                var pos = new Vector3(armies[i].x - 1.1f, 0f, -2.9f);
                var man = (GameObject)PrefabUtility.InstantiatePrefab(protagonists[i], station);
                man.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 180f, 0f));   // facing the walkway, towards you
                var show = man.AddComponent<AnimationShowcase>();
                show.title = who[i];
                show.label = Label(man.transform, who[i], pos + Vector3.up * 2.2f, 0.035f, new Color(1f, 0.92f, 0.7f), Quaternion.identity);
                show.label.anchor = TextAnchor.LowerCenter;
                // an ammunition box to sit on, out only when he sits (the sitting clips sit at chair height)
                show.seat = Prim(PrimitiveType.Cube, "Box to sit on", man.transform, new Vector3(0f, 0.21f, -0.2f), Vector3.zero, new Vector3(0.5f, 0.42f, 0.4f), wood);   // under his hips (behind his feet)
                show.seat.SetActive(false);
                showSpots.Add(new Bounds(pos + Vector3.up, new Vector3(2.6f, 3f, 2.6f)));
            }

            // the firing range behind the table: boards at 25, 50, 75 and 100 m from the firing point (the table's
            // front edge), each on its own bearing so they don't hide one another, and an earth bank at the back
            var range = Group("Firing range", station);
            // wooden targets: bullseye boards and figure targets, at their true distance from the firing point
            // (the table's front edge), each on its own bearing so they don't hide one another
            foreach (var (d, bearing, figure) in new[] { (15f, -25f, true), (25f, -12f, false), (25f, 14f, true), (40f, -20f, true), (50f, 6f, false),
                                                         (60f, 18f, true), (75f, -8f, false), (90f, 12f, true), (100f, 0f, false), (100f, -12f, true) })
                WoodTarget(range, Quaternion.Euler(0f, bearing, 0f) * Vector3.forward * d, bearing, d, figure, wood);
            var earth = Mat("PG_Earth", new Color(0.36f, 0.3f, 0.22f), 0.1f);
            Prim(PrimitiveType.Cube, "Stop butt (earth bank)", range, new Vector3(0f, 2.5f, 120f), Vector3.zero, new Vector3(56f, 5f, 6f), earth, true);
            DressRange(range, wood, earth);

            // ------------------------------------------------ the vehicle park
            var park = Group("Motor pool (German camp)", root);
            var keepOut = new List<Bounds>
            {
                new Bounds(new Vector3(0f, 1f, 0f), new Vector3(16f, 4f, 5f)),     // the three tables and the awning
                new Bounds(new Vector3(0f, 1f, -2.9f), new Vector3(16f, 4f, 3f)),  // the three main characters on show in front of them
                new Bounds(new Vector3(0f, 1f, 3.5f), new Vector3(42f, 4f, 6f)),   // the gun pits and the ammunition along the firing point
                new Bounds(new Vector3(-21f, 1f, -5f), new Vector3(7f, 4f, 6f)),  // the range hut
                new Bounds(new Vector3(0f, 1f, 62f), new Vector3(54f, 4f, 124f)),  // the firing range, from wire to wire
            };
            float zz = -36f;
            void Park(GameObject v, string label)
            {
                if (!v) return;
                var bs = WorldBounds(v);
                keepOut.Add(bs);
                // the painted plank sign on two weathered posts (the Sainte-Mère-Église town sign, Models/Props/SME/TownSign):
                // a fresh coat of paint over both faces of its plaque, the name on the side facing the walkway
                float z = v.transform.position.z;
                var sign = SmeProp("TownSign", park, new Vector3(GermanParkX - 7.8f, 0f, z), 90f, 0f, "Sign: " + label);
                var paint = Mat("PG_SignPaint", new Color(0.86f, 0.84f, 0.78f), 0.1f);
                var edge = Mat("PG_SignEdge", new Color(0.12f, 0.11f, 0.1f), 0.1f);
                Transform st = sign ? sign.transform : park;
                if (!sign) Prim(PrimitiveType.Cube, "Sign post", park, new Vector3(GermanParkX - 7.8f, 0.7f, z), Vector3.zero, new Vector3(0.08f, 1.4f, 0.08f), wood, true);
                foreach (float side in new[] { -1f, 1f })
                {
                    var panel = Prim(PrimitiveType.Cube, "Painted face", park, new Vector3(GermanParkX - 7.8f + side * 0.052f, PlaqueY, z), Vector3.zero, new Vector3(0.012f, PlaqueH, PlaqueW), edge);
                    Prim(PrimitiveType.Cube, "Paint", park, new Vector3(GermanParkX - 7.8f + side * 0.059f, PlaqueY, z), Vector3.zero, new Vector3(0.004f, PlaqueH - 0.06f, PlaqueW - 0.06f), paint);
                    panel.transform.SetParent(st, true);
                }
                FitLabel(park, label, new Vector3(GermanParkX - 7.8f - 0.064f, PlaqueY, z), PlaqueW - 0.16f, PlaqueH - 0.14f, new Color(0.08f, 0.08f, 0.08f), Quaternion.Euler(0f, 90f, 0f));
            }
            GameObject Slot(System.Func<Vector3, GameObject> make, string label)
            {
                var v = make(new Vector3(GermanParkX, 0f, zz)); zz += 12f;
                Park(v, label);
                return v;
            }
            Slot(p => ParkedLorry("OpelBlitzCovered", park, p, 270f, "Opel Blitz (canvas tilt)"), "Opel Blitz 3.6-36S (canvas tilt)");
            Slot(p => ParkedLorry("OpelBlitzOpen", park, p, 270f, "Opel Blitz (open bed)"), "Opel Blitz 3.6-36S (open bed)");
            Slot(p => Kubelwagen(park, "Kübelwagen", p, 270f), "VW Kübelwagen Typ 82");
            // (BuildArmour turns its vehicles a further 90 degrees: 180 here lines them up with the lorries, nose to the west)
            Slot(p => BuildArmour("Panzer IV", park, p, 180f, Armour.PanzerIV), "Panzer IV Ausf. H");
            Slot(p => BuildArmour("StuG III", park, p, 180f, Armour.StuG), "StuG III Ausf. G");
            Slot(p => BuildArmour("Hotchkiss H39 (Pak 40)", park, p, 180f, Armour.HotchkissPak40), "Hotchkiss H39 with Pak 40");
            Slot(p => BuildArmour("Marder III M", park, p, 180f, Armour.Marder), "Marder III Ausf. M");
            Slot(p => BuildArmour("Jagdpanzer 38(t) Hetzer", park, p, 180f, Armour.Hetzer), "Jagdpanzer 38(t) (from mid-1944)");
            Slot(p => BuildArmour("Flakpanzer IV Wirbelwind", park, p, 180f, Armour.Wirbelwind), "Flakpanzer IV Wirbelwind (from July 1944)");
            foreach (var hp in park.GetComponentsInChildren<Health>(true)) hp.regenPerSecond = Mathf.Max(hp.regenPerSecond, 50f);   // practice shots don't wreck them for good

            // ------------------------------------------------ overhead
            var air = Group("Overhead", root);
            var vic = new GameObject("C-47 vic"); vic.transform.SetParent(air);
            foreach (var (off, n) in new[] { (Vector3.zero, "Leader"), (new Vector3(-40f, -8f, -45f), "No. 2"), (new Vector3(40f, 6f, -50f), "No. 3") })
            {
                var plane = SmeC47($"C-47 ({n})", vic.transform);
                foreach (var f in plane.GetComponentsInChildren<C47Flight>(true)) Object.DestroyImmediate(f);
                foreach (var l in plane.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
                plane.transform.localPosition = off; plane.transform.localRotation = Quaternion.identity;
            }
            var fly = vic.AddComponent<AmbientAircraft>();
            fly.passes = new[]
            {
                new AmbientAircraft.Pass { from = new Vector3(-1200f, 170f, 60f), to = new Vector3(1200f, 170f, 20f) },
                new AmbientAircraft.Pass { from = new Vector3(400f, 200f, -1200f), to = new Vector3(-300f, 200f, 1200f) },
                new AmbientAircraft.Pass { from = new Vector3(1200f, 150f, -60f), to = new Vector3(-1200f, 150f, 80f) },
            };
            fly.volume = 0.45f; fly.speed = 60f; fly.firstPassAt = 20f; fly.gap = new Vector2(60f, 140f);
            vic.transform.position = new Vector3(0f, -2000f, 0f);

            // ------------------------------------------------ the player, at the training station
            var campAreas = BuildCamps(root, keepOut, wood);
            BuildVillage(root, keepOut);   // the hamlet south of the station (PegasusLevelBuilder.Village.cs)
            PaintProvingGround();
            ProvingTrees(env, keepOut);
            var player = BuildPlayer(actors);
            player.gameObject.name = "Player";
            player.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, -6f), Quaternion.identity);
            player.cam = cam;
            PgPostProcessingOn();   // the grade needs post-processing on the cameras

            // ------------------------------------------------ everyone in the game, out for a stroll
            BuildParades(actors, keepOut);   // each side's section marching round its lines (PegasusLevelBuilder.Parade.cs)
            var people = Group("Characters (strolling, peaceful)", actors);
            var area = new Bounds(new Vector3(0f, 0f, -24f), new Vector3(60f, 20f, 36f));   // townspeople: round the training station, behind the firing point
            var stroll = StrollController();
            var all = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!pf || !pf.GetComponent<Health>() || pf.GetComponent<TankController>()) continue;
                string n = pf.name;
                if (n.StartsWith("NetPlayer") || n == "GermanSoldier" || n == "German_MG" || n == "German_Rifleman" || n == "German_SMG") continue;   // players; old placeholders
                if (!pf.GetComponentInChildren<Animator>(true)) continue;
                all.Add(pf);
            }
            all.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            for (int i = 0; i < all.Count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(all[i], people);
                go.name = all[i].name;
                bool armed = go.GetComponentInChildren<Weapon>(true);
                var anim = go.GetComponentInChildren<Animator>(true);
                // the two animation sets (Stan's Game > Build Animation Sets): townspeople, and soldiers with their side's override
                string nm = go.name.ToLowerInvariant();
                bool civilian = !armed && (nm.Contains("civilian") || nm.Contains("nurse") || nm.Contains("woman") || nm.Contains("man_") || nm.Contains("french"));
                var hp0 = go.GetComponent<Health>();
                // each soldier in his own side's camp; the townspeople round the training station
                bool german = hp0 && hp0.faction == Faction.German;
                bool american = nm.StartsWith("us") || nm.Contains("american") || nm.Contains("82nd") || nm.Contains("505");
                var myArea = civilian ? area : german ? campAreas.german : american ? campAreas.american : campAreas.british;
                Vector3 p;
                int guard = 0;
                do { p = new Vector3(Random.Range(myArea.min.x, myArea.max.x), 0f, Random.Range(myArea.min.z, myArea.max.z)); }
                while (guard++ < 80 && keepOut.Exists(b => { var e = b; e.Expand(2f); return e.Contains(new Vector3(p.x, e.center.y, p.z)); }));
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                foreach (var ai in go.GetComponentsInChildren<SoldierAI>(true)) ai.enabled = false;   // nobody fights here
                if (armed) foreach (var h in go.GetComponentsInChildren<WeaponHolder>(true)) h.slingArms = true;   // in camp the guns hang on their slings
                var ctrl = civilian ? AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root + "/Animations/Civilian.controller")
                         : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root + (hp0 && hp0.faction == Faction.German ? "/Animations/Soldier_German.overrideController" : "/Animations/Soldier_Allied.overrideController"));
                if (anim) { anim.runtimeAnimatorController = ctrl ? ctrl : (armed ? anim.runtimeAnimatorController : stroll); anim.applyRootMotion = false; }
                var wd = go.AddComponent<Wanderer>();
                wd.area = myArea; wd.keepOut = keepOut.ToArray(); wd.armsDown = !armed && !ctrl;
                wd.actions = civilian ? AnimActions.CivilianAtEase : AnimActions.SoldierAtEase;
                wd.label = Label(go.transform, NiceName(go.name), go.transform.position + Vector3.up * 2.15f, 0.05f, new Color(1f, 0.92f, 0.7f), Quaternion.identity);   // over his head (a world position)
            }

            DressBuildings(root, "ProvingGround");
            string scenePath = $"{SceneDir}/{ProvingScene}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == scenePath)) list.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"Stan's Game: Proving Ground built at {scenePath} - {slots.Count} things on the tables, 8 vehicles, {all.Count} characters strolling");
        }

        // ------------------------------------------------------------------ the lie of the land

        // the working area (station, range, vehicle park, the strollers' field) stays level; beyond it the ground rolls
        static readonly Rect PgFlat = new Rect(-138f, -60f, 276f, 184f);   // x -138..138 (the camps either side), z -60..124
        const float PgSize = 640f, PgHeight = 30f;

        /// <summary>How high the ground is at x, z: 0 on the level working area, gentle rolling farmland round it
        /// (a few metres), and a long wooded rise behind the stop butt that also catches stray rounds.</summary>
        static float PgHeightAt(float x, float z)
        {
            float dx = Mathf.Max(PgFlat.xMin - x, 0f, x - PgFlat.xMax), dz = Mathf.Max(PgFlat.yMin - z, 0f, z - PgFlat.yMax);
            float away = Mathf.Sqrt(dx * dx + dz * dz);
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 45f, away));
            float roll = 2.2f * (Mathf.PerlinNoise(x * 0.012f + 31f, z * 0.012f + 7f) - 0.3f)
                       + 1.6f * Mathf.PerlinNoise(x * 0.035f + 3f, z * 0.035f + 19f)
                       + 0.5f * Mathf.PerlinNoise(x * 0.09f, z * 0.09f);
            float ridge = 7f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PgFlat.yMax + 5f, PgFlat.yMax + 70f, z)) * Mathf.Clamp01(1f - Mathf.Abs(x) / 220f);
            return Mathf.Max(0f, rise * (roll * 2f + ridge));
        }

        static void ProvingTerrain(Transform env, Texture2D turfTex)
        {
            const int res = 257;
            string dataPath = $"{Root}/Terrain/ProvingGround_Terrain.asset", layerPath = $"{Root}/Terrain/ProvingGround_Turf.terrainlayer";
            if (!AssetDatabase.IsValidFolder($"{Root}/Terrain")) AssetDatabase.CreateFolder(Root, "Terrain");
            AssetDatabase.DeleteAsset(dataPath);
            var data = new TerrainData { heightmapResolution = res };
            data.size = new Vector3(PgSize, PgHeight, PgSize);
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (!layer) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, layerPath); }
            layer.diffuseTexture = turfTex; layer.tileSize = new Vector2(4f, 4f);
            if (!turfTex) layer.diffuseRemapMax = new Vector4(0.33f, 0.42f, 0.22f, 1f);
            EditorUtility.SetDirty(layer);
            data.terrainLayers = new[] { layer };
            AssetDatabase.CreateAsset(data, dataPath);
            var h = new float[res, res];
            for (int iz = 0; iz < res; iz++)
                for (int ix = 0; ix < res; ix++)
                {
                    float x = -PgSize / 2f + PgSize * ix / (res - 1f), z = -PgSize / 2f + PgSize * iz / (res - 1f);
                    h[iz, ix] = PgHeightAt(x, z) / PgHeight;
                }
            data.SetHeights(0, 0, h);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            var t = Terrain.CreateTerrainGameObject(data);
            t.name = "Terrain (rolling farmland)";
            t.transform.SetParent(env);
            t.transform.position = new Vector3(-PgSize / 2f, 0f, -PgSize / 2f);
            var terrain = t.GetComponent<Terrain>();
            terrain.basemapDistance = 600f; terrain.heightmapPixelError = 3f;
            terrainRef = terrain;
        }

        /// <summary>Woods on the rise behind the range, scattered trees and bushes over the farmland, hedges along
        /// two field edges, and a few bushes round the edge of the level area (never in the working spaces).</summary>
        static void ProvingTrees(Transform env, List<Bounds> keepOut)
        {
            bool wasFlat = flatGround; flatGround = false;   // trees stand on the rolling ground
            try
            {
                var trees = Group("Trees and bushes", env);
                var bark = Mat("Bark", new Color(0.20f, 0.17f, 0.13f));
                var poplarBark = Mat("Bark_Poplar", new Color(0.30f, 0.29f, 0.26f));
                var leaves = Mat("Poplar_Leaves", new Color(0.10f, 0.16f, 0.08f));
                var broad = Mat("Broadleaf_Leaves", new Color(0.08f, 0.14f, 0.07f));
                var apple = Mat("Orchard_Leaves", new Color(0.11f, 0.17f, 0.09f));
                Random.InitState(6644);
                bool Clear(Vector3 p, float margin)
                {
                    if (PgFlat.Contains(new Vector2(p.x, p.z)) && margin > 0f)
                    {
                        // inside the level area only at its very edge
                        float edge = Mathf.Min(p.x - PgFlat.xMin, PgFlat.xMax - p.x, p.z - PgFlat.yMin, PgFlat.yMax - p.z);
                        if (edge > margin) return false;
                    }
                    foreach (var b in keepOut) { var e = b; e.Expand(6f); if (e.Contains(new Vector3(p.x, e.center.y, p.z))) return false; }
                    return Mathf.Abs(p.x) > 22f || p.z < -8f || p.z > 130f;   // never in the line of fire
                }
                Vector3 OnGround(float x, float z) => new Vector3(x, GroundAt(new Vector3(x, 0f, z)), z);
                // the wood on the rise behind the stop butt
                for (int i = 0; i < 70; i++)
                {
                    var p = OnGround(Random.Range(-150f, 150f), Random.Range(140f, 260f));
                    if (Random.value < 0.6f) Broadleaf(trees, p, bark, broad); else TallPoplar(trees, p, poplarBark, leaves);
                }
                // trees and bushes scattered over the fields round about
                int placed = 0;
                for (int i = 0; i < 400 && placed < 90; i++)
                {
                    float a = Random.value * Mathf.PI * 2f, r = Random.Range(70f, 280f);
                    var p = OnGround(Mathf.Cos(a) * r, 35f + Mathf.Sin(a) * r);
                    if (!Clear(p, 0f) || PgFlat.Contains(new Vector2(p.x, p.z))) continue;
                    float k = Random.value;
                    if (k < 0.25f) Broadleaf(trees, p, bark, broad);
                    else if (k < 0.4f) TallPoplar(trees, p, poplarBark, leaves);
                    else if (k < 0.55f) AppleTree(trees, p, bark, apple);
                    else { var b = new GameObject("Bush"); b.transform.SetParent(trees); b.transform.position = p; AddSmallTree(b, 4, Random.Range(1.6f, 3f)); }
                    placed++;
                }
                // a few bushes and small trees along the edge of the level area, between the working spaces
                placed = 0;
                for (int i = 0; i < 300 && placed < 26; i++)
                {
                    float x = Random.Range(PgFlat.xMin - 6f, PgFlat.xMax + 6f), z = Random.Range(PgFlat.yMin - 6f, PgFlat.yMax + 6f);
                    var p = OnGround(x, z);
                    if (!Clear(p, 7f)) continue;
                    var b = new GameObject(Random.value < 0.75f ? "Bush" : "Small tree"); b.transform.SetParent(trees); b.transform.position = p;
                    if (b.name == "Bush") AddSmallTree(b, 4, Random.Range(1.4f, 2.6f)); else AddSmallTree(b, 3, Random.Range(4f, 6f));
                    placed++;
                }
                // two hedgerows along the field boundaries, west and south
                Hedgerow(trees, OnGround(-152f, -80f), OnGround(-152f, 120f));   // (outside the camps)
                Hedgerow(trees, OnGround(-140f, -76f), OnGround(140f, -76f));
            }
            finally { flatGround = wasFlat; }
        }

        /// <summary>A complete weapon for the armoury table (model, muzzle, flash, sound, its stats), saved as
        /// Prefabs/Armoury/&lt;name&gt;.prefab: the Weapon_* prefabs are only the models the soldiers' guns are built from,
        /// so laid on the table they could not be picked up (Joan, 22 Nov).</summary>
        static GameObject ArmouryWeapon(string name, float dmg, float rpm, bool auto, int mag)
        {
            string dir = PrefabDir + "/Armoury";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(PrefabDir, "Armoury");
            var owner = new GameObject("_armoury");
            Weapon w = name == "M1911" || name.StartsWith("Luger") ? Pistol(owner.transform, name, dmg) : AddWeapon(owner.transform, name, dmg, rpm, auto, mag);
            if (name == "PIAT" || name.StartsWith("Bazooka"))
            {
                w.range = name == "PIAT" ? 120f : 150f; w.hipSpread = 2f; w.aimSpread = 0.5f; w.projectile = piatBomb;
                if (name.StartsWith("Bazooka")) w.displayName = "Bazooka M1A1 (2.36-in)";
            }
            w.transform.SetParent(null, false);
            w.transform.localPosition = Vector3.zero; w.transform.localRotation = Quaternion.identity;
            var prefab = PrefabUtility.SaveAsPrefabAsset(w.gameObject, $"{dir}/{Safe(name)}.prefab");
            Object.DestroyImmediate(w.gameObject); Object.DestroyImmediate(owner);
            return prefab;
        }

        static string Safe(string n) { foreach (char c in System.IO.Path.GetInvalidFileNameChars()) n = n.Replace(c, '_'); return n.Replace(' ', '_'); }

        const float RangeHalfWidth = 24f, WireFrom = 8f, WireTo = 116f;

        /// <summary>
        /// The range as a 1944 training range: the firing point with a sandbag parapet, two machine-gun pits (Vickers and
        /// Browning .30) and two anti-tank guns (6-pounder and PaK 38) along it; barbed wire in a continuous line down
        /// both sides with a sandbag wall behind it; mortar pits outside the wire; the red flag on the stop butt, anti-glider
        /// poles on the ridge, the range hut, and an old lorry down range as the anti-tank target.
        /// </summary>
        static void DressRange(Transform range, Material wood, Material earth)
        {
            // the firing point: a waist-high sandbag parapet with a gap in the middle
            SandbagWall(range, new Vector3(-7.4f, 0f, 2.6f), new Vector3(-1.3f, 0f, 2.6f), 0.6f);
            SandbagWall(range, new Vector3(1.3f, 0f, 2.6f), new Vector3(7.4f, 0f, 2.6f), 0.6f);
            // barbed wire: knife rests end to end in one line down each side, a sandbag wall in front of it (inside the range)
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * RangeHalfWidth, z = WireFrom, step = 0f;
                while (z < WireTo)
                {
                    var kr = KnifeRest(range, new Vector3(x, 0f, z), 90f);
                    if (step <= 0f) { var kb = WorldBounds(kr); step = Mathf.Max(1.5f, kb.size.z * 0.97f); kr.transform.position += Vector3.forward * (step * 0.5f); }
                    else kr.transform.position += Vector3.forward * (step * 0.5f);
                    z += step;
                }
                SandbagWall(range, new Vector3(x - side * 1.6f, 0f, WireFrom), new Vector3(x - side * 1.6f, 0f, WireTo), 0.6f);   // inside the wire
                WireRolls(range, new Vector3(x, 0f, WireTo + 2f), 0f);   // spare coils at the far end
                // mortar pits outside the wire, near and far, on each side
                foreach (float mz in new[] { 40f, 95f })
                {
                    var c = new Vector3(side * (RangeHalfWidth + 6.5f), 0f, mz);
                    SandbagRing(range, c, 1.9f, 0.7f, Vector3.right * side, 1);
                    SmeProp(mz < 50f ? "Mortar81" : "Mortar60", range, c + Vector3.forward * 0.3f, 0f, 0f, mz < 50f ? "81 mm mortar" : "60 mm mortar");
                    SmeProp("AmmoBox1", range, c + new Vector3(side * -1.1f, 0f, -0.9f), 20f);
                }
            }
            // along the firing point: the machine guns inside, the anti-tank guns outside, ammunition by each
            MountedMg(range, new Vector3(-11f, 0f, 3.6f), 0f, true);
            MountedMg(range, new Vector3(11f, 0f, 3.6f), 0f, false);
            var pak = SmeProp("Pak38", range, new Vector3(-17.5f, 0f, 4f), 0f, 0f, "5 cm PaK 38 (model)");
            if (pak) { var fg = RigFieldGun(pak); if (fg) { fg.traverseLimit = 32f; } }
            SandbagRing(range, new Vector3(-17.5f, 0f, 4f), 2.8f, 0.7f, Vector3.back, 1);
            SixPounder(range, new Vector3(17.5f, 0f, 4f), 0f);
            SandbagRing(range, new Vector3(17.5f, 0f, 4f), 2.8f, 0.7f, Vector3.back, 1);
            foreach (var (n, p, y) in new[] { ("AmmoBox2", new Vector3(-14.2f, 0f, 1.2f), 80f), ("AmmoBox3", new Vector3(-8.8f, 0f, 1.4f), 10f),
                                              ("AmmoBox1", new Vector3(8.8f, 0f, 1.4f), -15f), ("AmmoBox2", new Vector3(14.2f, 0f, 1.2f), 70f),
                                              ("CrateWide", new Vector3(-20.5f, 0f, 1.5f), 5f), ("CrateWide", new Vector3(20.5f, 0f, 1.5f), -5f) })
                SmeProp(n, range, p, y);
            // the red range flag on the stop butt
            var red = Mat("PG_RangeFlag", new Color(0.72f, 0.08f, 0.06f), 0.1f);
            Prim(PrimitiveType.Cylinder, "Range flag pole", range, new Vector3(-26f, 6.5f, 120f), Vector3.zero, new Vector3(0.08f, 3f, 0.08f), wood, true);
            Prim(PrimitiveType.Cube, "Range flag", range, new Vector3(-25.35f, 8.9f, 120f), Vector3.zero, new Vector3(1.2f, 0.75f, 0.02f), red);
            // Rommel's asparagus on the rise behind the butt
            for (int i = 0; i < 16; i++)
            {
                var pos = new Vector3(-42f + i * 5.5f + Random.Range(-1f, 1f), 0f, 130f + Random.Range(-2f, 4f));
                SmeProp(i % 4 == 3 ? "AntiGliderPoleLying" : "AntiGliderPole", range, pos, Random.Range(0f, 360f));
            }
            // the range hut behind the firing point, left, with its stores
            var hut = SmeProp("GardenShed", range, new Vector3(-21.5f, 0f, -5f), 90f, 0f, "Range hut");
            if (hut) hut.transform.localScale *= 1.6f;   // a proper hut a man walks into, not a garden shed
            SmeProp("CrateStackA", range, new Vector3(-17.2f, 0f, -2.6f), 15f);
            SmeProp("Barrel", range, new Vector3(-17.4f, 0f, -7.2f), 0f);
            // the anti-tank target: an old lorry at about 80 m, right of centre
            var wreck = SmeProp("OpelBlitzOpen", range, new Vector3(14f, 0f, 80f), 70f, 0f, "Anti-tank target (old Opel lorry)");
            if (wreck) Label(range, "A/T target", new Vector3(14f, 3.6f, 80f), 0.06f, Color.white, Quaternion.identity);
            Prim(PrimitiveType.Cube, "Shell scrape", range, new Vector3(14f, 0.02f, 77.5f), new Vector3(0f, 30f, 0f), new Vector3(3f, 0.04f, 2f), earth);
        }

        /// <summary>A wooden target on two stakes: planks nailed side by side, a smaller paper face (bullseye, or a
        /// man's head and shoulders for a figure target), and the distance painted on a peg in front.</summary>
        static void WoodTarget(Transform parent, Vector3 at, float yaw, float dist, bool figure, Material stakeWood)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var t = new GameObject($"{(figure ? "Figure" : "Bullseye")} target {dist:0} m").transform;
            t.SetParent(parent); t.SetPositionAndRotation(at, rot);
            var planks = new[] { Mat("PG_Plank_A", new Color(0.50f, 0.37f, 0.22f), 0.15f), Mat("PG_Plank_B", new Color(0.45f, 0.33f, 0.20f), 0.15f),
                                 Mat("PG_Plank_C", new Color(0.55f, 0.41f, 0.25f), 0.15f) };
            var paper = Mat("PG_Target_Paper", new Color(0.86f, 0.83f, 0.72f), 0.05f);
            var black = Mat("PG_Target_Black", new Color(0.08f, 0.08f, 0.08f), 0.1f);
            float w = figure ? 0.8f : 1.1f, h = figure ? 1.1f : 1.2f, bottom = figure ? 0.45f : 0.6f;
            int n = figure ? 3 : 4;
            for (int i = 0; i < n; i++)
            {
                float pw = w / n;
                Prim(PrimitiveType.Cube, "Plank", t, new Vector3(-w / 2f + pw * (i + 0.5f), bottom + h / 2f, 0f), new Vector3(0f, 0f, Random.Range(-0.6f, 0.6f)),
                     new Vector3(pw - 0.012f, h - Random.Range(0f, 0.04f), 0.04f), planks[(i + Mathf.RoundToInt(dist)) % 3], i == 0);
            }
            Prim(PrimitiveType.Cube, "Batten", t, new Vector3(0f, bottom + 0.12f, 0.03f), Vector3.zero, new Vector3(w, 0.07f, 0.025f), planks[1]);
            Prim(PrimitiveType.Cube, "Batten", t, new Vector3(0f, bottom + h - 0.12f, 0.03f), Vector3.zero, new Vector3(w, 0.07f, 0.025f), planks[1]);
            if (figure)
            {
                Prim(PrimitiveType.Cube, "Paper (shoulders)", t, new Vector3(0f, bottom + 0.38f, -0.025f), Vector3.zero, new Vector3(0.56f, 0.5f, 0.005f), paper);
                Prim(PrimitiveType.Cylinder, "Paper (head)", t, new Vector3(0f, bottom + 0.8f, -0.025f), new Vector3(90f, 0f, 0f), new Vector3(0.26f, 0.0025f, 0.3f), paper);
                Prim(PrimitiveType.Cylinder, "Aiming mark", t, new Vector3(0f, bottom + 0.45f, -0.03f), new Vector3(90f, 0f, 0f), new Vector3(0.1f, 0.003f, 0.1f), black);
            }
            else
            {
                Prim(PrimitiveType.Cube, "Paper", t, new Vector3(0f, bottom + h / 2f, -0.025f), Vector3.zero, new Vector3(0.66f, 0.66f, 0.005f), paper);
                Prim(PrimitiveType.Cylinder, "Ring", t, new Vector3(0f, bottom + h / 2f, -0.03f), new Vector3(90f, 0f, 0f), new Vector3(0.42f, 0.002f, 0.42f), black);
                Prim(PrimitiveType.Cylinder, "Ring (paper)", t, new Vector3(0f, bottom + h / 2f, -0.033f), new Vector3(90f, 0f, 0f), new Vector3(0.3f, 0.002f, 0.3f), paper);
                Prim(PrimitiveType.Cylinder, "Bull", t, new Vector3(0f, bottom + h / 2f, -0.036f), new Vector3(90f, 0f, 0f), new Vector3(0.14f, 0.002f, 0.14f), black);
            }
            foreach (float x in new[] { -w / 2f + 0.08f, w / 2f - 0.08f })
                Prim(PrimitiveType.Cube, "Stake", t, new Vector3(x, (bottom + 0.3f) / 2f, 0.05f), Vector3.zero, new Vector3(0.07f, bottom + 0.3f, 0.07f), stakeWood, true);
            // the distance on a little painted peg in front
            Prim(PrimitiveType.Cube, "Distance peg", t, new Vector3(0f, 0.22f, -0.9f), Vector3.zero, new Vector3(0.4f, 0.24f, 0.03f), paper);
            Label(t, $"{dist:0}", at + rot * new Vector3(0f, 0.22f, -0.92f), 0.035f, new Color(0.1f, 0.1f, 0.1f), rot);
        }

        /// <summary>
        /// A machine gun on its tripod in a sandbag pit, manned with E like the field guns (FieldGun with a gun): a mock-up
        /// of the British Vickers Mk I (.303, water-cooled, high tripod, spade grips) or the American Browning M1919A4
        /// (.30, air-cooled perforated jacket, low tripod), until Joan finds the models.
        /// </summary>
        static FieldGun MountedMg(Transform parent, Vector3 pos, float yaw, bool vickers)
        {
            var root = new GameObject(vickers ? "Vickers MG Mk I (.303)" : "Browning M1919A4 (.30) - mock-up").transform;
            root.SetParent(parent); root.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            // Joan's Vickers (vickers-machine-gun): tripod, ammunition box and the gun with its belt on the tripod head
            var vm = vickers ? PgModel($"{PgProps}/VickersMG.fbx", root, pos, yaw, $"{PgProps}/Textures/VickersMG", "Vickers model") : null;
            Transform vGun = vm ? FindDeep(vm.transform, "Vickers_Gun") : null, vMuzzle = vm ? FindDeep(vm.transform, "Vickers_Muzzle") : null;
            if (vm && (!vGun || !vMuzzle)) { Object.DestroyImmediate(vm); vm = null; }
            if (vm && root.InverseTransformPoint(vMuzzle.position).z < 0f) vm.transform.rotation = Quaternion.AngleAxis(180f, Vector3.up) * vm.transform.rotation;
            var steel = Mat("PG_GunSteel", new Color(0.12f, 0.12f, 0.12f), 0.45f, 0.7f);
            var brass = Mat("PG_Brass", new Color(0.55f, 0.43f, 0.2f), 0.6f, 0.8f);
            var olive = Mat("PG_Olive", new Color(0.27f, 0.28f, 0.19f), 0.2f, 0.1f);
            float head = vm ? root.InverseTransformPoint(vGun.position).y : vickers ? 0.62f : 0.38f;
            // tripod: two splayed front legs and a long rear leg
            if (!vm) foreach (var (lx, lz) in new[] { (-0.55f, 0.45f), (0.55f, 0.45f), (0f, -0.9f) })
            {
                var foot = new Vector3(lx, 0f, lz); var top = new Vector3(0f, head, 0f);
                var mid = (foot + top) / 2f; float len = Vector3.Distance(foot, top);
                var leg = Prim(PrimitiveType.Cylinder, "Tripod leg", root, mid, Quaternion.FromToRotation(Vector3.up, top - foot).eulerAngles, new Vector3(0.04f, len / 2f, 0.04f), steel);
            }
            var trav = new GameObject("Traverse").transform; trav.SetParent(root, false); trav.localPosition = vm ? root.InverseTransformPoint(vGun.position) : new Vector3(0f, head, 0f);
            var elev = new GameObject("Elevation").transform; elev.SetParent(trav, false); elev.localPosition = new Vector3(0f, vm ? 0f : 0.08f, 0f);
            var gunT = new GameObject("Gun").transform; gunT.SetParent(elev, false);
            float muzzleZ;
            if (vm)
            {
                vGun.SetParent(gunT, true);   // it traverses and elevates with the mount
                muzzleZ = gunT.InverseTransformPoint(vMuzzle.position).z;
            }
            else if (vickers)
            {
                Prim(PrimitiveType.Cylinder, "Water jacket", gunT, new Vector3(0f, 0f, 0.38f), new Vector3(90f, 0f, 0f), new Vector3(0.115f, 0.36f, 0.115f), brass);
                Prim(PrimitiveType.Cylinder, "Muzzle booster", gunT, new Vector3(0f, 0f, 0.8f), new Vector3(90f, 0f, 0f), new Vector3(0.05f, 0.07f, 0.05f), steel);
                Prim(PrimitiveType.Cube, "Receiver", gunT, new Vector3(0f, 0f, -0.12f), Vector3.zero, new Vector3(0.12f, 0.14f, 0.42f), steel);
                Prim(PrimitiveType.Cylinder, "Steam tube", gunT, new Vector3(0.07f, -0.05f, 0.3f), new Vector3(90f, 0f, 0f), new Vector3(0.02f, 0.25f, 0.02f), steel);
                foreach (float gx in new[] { -0.07f, 0.07f })
                    Prim(PrimitiveType.Cylinder, "Spade grip", gunT, new Vector3(gx, 0f, -0.38f), Vector3.zero, new Vector3(0.03f, 0.06f, 0.03f), Mat("PG_GripWood", new Color(0.3f, 0.19f, 0.1f), 0.2f));
                Prim(PrimitiveType.Cube, "Ammunition box", root, new Vector3(0.45f, 0.1f, 0.15f), new Vector3(0f, 10f, 0f), new Vector3(0.12f, 0.2f, 0.28f), olive, true);
                muzzleZ = 0.86f;
            }
            else
            {
                Prim(PrimitiveType.Cylinder, "Barrel jacket (perforated)", gunT, new Vector3(0f, 0f, 0.32f), new Vector3(90f, 0f, 0f), new Vector3(0.06f, 0.3f, 0.06f), steel);
                Prim(PrimitiveType.Cylinder, "Barrel", gunT, new Vector3(0f, 0f, 0.66f), new Vector3(90f, 0f, 0f), new Vector3(0.025f, 0.06f, 0.025f), steel);
                Prim(PrimitiveType.Cube, "Receiver", gunT, new Vector3(0f, 0.01f, -0.13f), Vector3.zero, new Vector3(0.09f, 0.13f, 0.46f), steel);
                Prim(PrimitiveType.Cube, "Pistol grip", gunT, new Vector3(0f, -0.07f, -0.33f), new Vector3(-15f, 0f, 0f), new Vector3(0.035f, 0.1f, 0.04f), Mat("PG_GripWood", new Color(0.3f, 0.19f, 0.1f), 0.2f));
                Prim(PrimitiveType.Cube, "Ammunition can", root, new Vector3(-0.35f, 0.09f, 0.1f), new Vector3(0f, -8f, 0f), new Vector3(0.1f, 0.18f, 0.27f), olive, true);
                muzzleZ = 0.72f;
            }
            // the working gun: rate of fire, damage, sound, tracer (Weapon), and a belt that never runs out
            var w = AddWeapon(elev, vickers ? "Vickers MG (.303)" : "Browning M1919 MG (.30)", vickers ? 45f : 40f, vickers ? 500f : 450f, true, 250);
            w.transform.localPosition = Vector3.zero; w.transform.localRotation = Quaternion.identity;
            foreach (Transform c in w.transform.Cast<Transform>().ToList()) if (c.name == "Gun") Object.DestroyImmediate(c.gameObject);
            w.muzzle.localPosition = new Vector3(0f, 0f, muzzleZ);
            w.range = 800f; w.hipSpread = 0.6f; w.aimSpread = 0.35f;
            var sight = new GameObject("Sight").transform; sight.SetParent(elev, false); sight.localPosition = new Vector3(0f, 0.1f, -0.5f);
            var fg = root.gameObject.AddComponent<FieldGun>();
            fg.displayName = vickers ? "Vickers MG (.303)" : "Browning M1919A4 (.30)";
            fg.traverse = trav; fg.elevate = elev; fg.barrel = gunT; fg.muzzle = w.muzzle; fg.sight = sight; fg.gun = w;
            fg.traverseLimit = 40f; fg.minElevation = -8f; fg.maxElevation = 18f; fg.traverseSpeed = 140f; fg.elevateSpeed = 90f;
            fg.recoilLength = 0.015f; fg.eyeOffset = new Vector3(-0.3f, 0.75f, -1.6f);
            SandbagRing(parent, pos, 1.7f, 0.55f, Quaternion.Euler(0f, yaw, 0f) * Vector3.back, 1);
            return fg;
        }

        /// <summary>
        /// The Ordnance QF 6-pounder, the anti-tank gun the Horsas brought in for the airborne (and, as the 57 mm M1, the
        /// Americans' too): a mock-up of shapes - split trail, two wheels, the double shield, the long barrel - laid and
        /// fired like the PaK 38 (FieldGun) until a model is found.
        /// </summary>
        static FieldGun SixPounder(Transform parent, Vector3 pos, float yaw)
        {
            var root = new GameObject("Ordnance QF 6-pounder - mock-up").transform;
            root.SetParent(parent); root.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var green = Mat("PG_SixPdr_Green", new Color(0.24f, 0.27f, 0.18f), 0.25f, 0.2f);
            var steel = Mat("PG_GunSteel", new Color(0.12f, 0.12f, 0.12f), 0.45f, 0.7f);
            var tyre = Mat("PG_Tyre", new Color(0.06f, 0.06f, 0.06f), 0.1f);
            // carriage: axle, wheels, split trail opened out behind
            Prim(PrimitiveType.Cylinder, "Axle", root, new Vector3(0f, 0.38f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.09f, 0.85f, 0.09f), green, true);
            foreach (float x in new[] { -0.82f, 0.82f })
            {
                Prim(PrimitiveType.Cylinder, "Wheel", root, new Vector3(x, 0.38f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.76f, 0.09f, 0.76f), tyre, true);
                Prim(PrimitiveType.Cylinder, "Hub", root, new Vector3(x * 1.1f, 0.38f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.3f, 0.03f, 0.3f), green);
            }
            foreach (float s in new[] { -1f, 1f })
            {
                var a = new Vector3(s * 0.25f, 0.38f, -0.1f); var b = new Vector3(s * 0.85f, 0.05f, -2.5f);
                Prim(PrimitiveType.Cube, "Trail leg", root, (a + b) / 2f, Quaternion.LookRotation(b - a).eulerAngles, new Vector3(0.12f, 0.14f, Vector3.Distance(a, b)), green, true);
                Prim(PrimitiveType.Cube, "Spade", root, b + new Vector3(0f, 0.05f, -0.05f), new Vector3(20f, 0f, 0f), new Vector3(0.3f, 0.25f, 0.04f), steel);
            }
            var trav = new GameObject("Traverse").transform; trav.SetParent(root, false); trav.localPosition = new Vector3(0f, 0.55f, 0.05f);
            // the double shield (two plates with a gap for the sight) and the lower apron
            Prim(PrimitiveType.Cube, "Shield", trav, new Vector3(0f, 0.45f, 0.35f), new Vector3(-10f, 0f, 0f), new Vector3(1.65f, 0.95f, 0.02f), green, true);
            Prim(PrimitiveType.Cube, "Shield (front plate)", trav, new Vector3(0f, 0.4f, 0.45f), new Vector3(-10f, 0f, 0f), new Vector3(1.55f, 0.75f, 0.02f), green);
            Prim(PrimitiveType.Cube, "Apron", trav, new Vector3(0f, -0.25f, 0.4f), Vector3.zero, new Vector3(1.2f, 0.35f, 0.02f), green);
            Prim(PrimitiveType.Cube, "Cradle", trav, new Vector3(0f, 0.1f, 0f), Vector3.zero, new Vector3(0.28f, 0.22f, 1.0f), green);
            var elev = new GameObject("Elevation").transform; elev.SetParent(trav, false); elev.localPosition = new Vector3(0f, 0.28f, 0f);
            var barrel = new GameObject("Barrel").transform; barrel.SetParent(elev, false);
            Prim(PrimitiveType.Cylinder, "Barrel", barrel, new Vector3(0f, 0f, 1.2f), new Vector3(90f, 0f, 0f), new Vector3(0.1f, 1.25f, 0.1f), green);
            Prim(PrimitiveType.Cylinder, "Muzzle", barrel, new Vector3(0f, 0f, 2.47f), new Vector3(90f, 0f, 0f), new Vector3(0.13f, 0.04f, 0.13f), steel);
            Prim(PrimitiveType.Cube, "Breech", barrel, new Vector3(0f, 0f, -0.35f), Vector3.zero, new Vector3(0.26f, 0.26f, 0.45f), steel);
            Prim(PrimitiveType.Cylinder, "Recoil cylinder", barrel, new Vector3(0f, -0.13f, 0.25f), new Vector3(90f, 0f, 0f), new Vector3(0.09f, 0.45f, 0.09f), green);
            var muzzle = new GameObject("Muzzle point").transform; muzzle.SetParent(barrel, false); muzzle.localPosition = new Vector3(0f, 0f, 2.52f);
            var sight = new GameObject("Sight").transform; sight.SetParent(elev, false); sight.localPosition = new Vector3(-0.28f, 0.12f, -0.1f);
            var fg = root.gameObject.AddComponent<FieldGun>();
            fg.displayName = "QF 6-pounder (57 mm)";
            fg.traverse = trav; fg.elevate = elev; fg.barrel = barrel; fg.muzzle = muzzle; fg.sight = sight;
            fg.shellPrefab = pak38Shell; fg.shellSpeed = 853f; fg.reloadTime = 3f;
            fg.traverseLimit = 45f; fg.minElevation = -5f; fg.maxElevation = 15f; fg.recoilLength = 0.4f;
            return fg;
        }

        // the town sign's plaque (TownSign.fbx: 1.17 m wide, 0.67 m tall, its top at 1.455 m)
        const float PlaqueW = 1.12f, PlaqueH = 0.62f, PlaqueY = 1.12f;

        /// <summary>A label sized to fit a panel (width and height in metres): as large as it can be up to the normal
        /// sign lettering, on two lines when one would be too small to read.</summary>
        static TextMesh FitLabel(Transform parent, string text, Vector3 worldPos, float width, float height, Color c, Quaternion rot)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            const int px = 64;
            float Adv(string t)
            {
                if (!font) return t.Length * 34f;
                font.RequestCharactersInTexture(t, px);
                float a = 0f; foreach (char ch in t) if (font.GetCharacterInfo(ch, out var ci, px)) a += ci.advance;
                return a;
            }
            // world width = advance (pixels) x characterSize x 0.1; a line is about px x characterSize x 0.115 tall
            float Fit(string[] lines)
            {
                float w = 0f; foreach (var l in lines) w = Mathf.Max(w, Adv(l));
                return Mathf.Min(0.035f, width / (w * 0.1f), height / (lines.Length * px * 0.115f));
            }
            string[] best = { text }; float size = Fit(best);
            if (size < 0.026f)
            {
                // two lines, broken at the space nearest the middle
                int mid = text.Length / 2, cut = -1;
                for (int d = 0; d < mid && cut < 0; d++)
                {
                    if (mid + d < text.Length && text[mid + d] == ' ') cut = mid + d;
                    else if (mid - d > 0 && text[mid - d] == ' ') cut = mid - d;
                }
                if (cut > 0)
                {
                    var two = new[] { text.Substring(0, cut), text.Substring(cut + 1) };
                    float s2 = Fit(two);
                    if (s2 > size) { best = two; size = s2; }
                }
            }
            var tm = Label(parent, string.Join("\n", best), worldPos, size, c, rot);
            tm.lineSpacing = 0.95f;
            return tm;
        }

        static string NiceName(string n) => n.Replace("_Heer_", " (Heer ").Replace("_", " ").Replace("(Heer A", "(Heer A)").Replace("(Heer B", "(Heer B)").Replace("(Heer C", "(Heer C)");

        /// <summary>A 3D text label (signs, the gun names, the name tags).</summary>
        static TextMesh Label(Transform parent, string text, Vector3 worldPos, float size, Color c, Quaternion rot)
        {
            var go = new GameObject("Label: " + text);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(worldPos, rot);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text; tm.characterSize = size; tm.fontSize = 64; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = c;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font) { tm.font = font; go.GetComponent<MeshRenderer>().sharedMaterial = WorldTextMat(font); go.AddComponent<WorldTextFont>(); }
            return tm;
        }

        /// <summary>The font on a material that is hidden behind things and seen from the front only (no mirror
        /// writing from behind a sign, no names showing through walls).</summary>
        static Material WorldTextMat(Font font)
        {
            string path = Root + "/Materials/WorldText.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("StansGame/WorldText");
            if (!sh) return font.material;
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh; m.mainTexture = font.material.mainTexture; m.color = Color.white;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 3f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Stand and walk for the characters without a rifle (an IK pass lets their arms hang).</summary>
        static RuntimeAnimatorController StrollController()
        {
            string path = Root + "/Animations/Stroll.controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing) return existing;
            AnimationClip Clip(string n)
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{Root}/Animations/Hero/Hero@{n}.fbx"))
                    if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
                return null;
            }
            var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ac.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            var layers = ac.layers; layers[0].iKPass = true; ac.layers = layers;
            var st = ac.layers[0].stateMachine.AddState("Stroll");
            var tree = new BlendTree { name = "Stand-Walk-Run", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, ac);
            if (Clip("Idle")) tree.AddChild(Clip("Idle"), 0f);
            if (Clip("WalkForward")) tree.AddChild(Clip("WalkForward"), Locomotion.Walk);
            if (Clip("RunForward")) tree.AddChild(Clip("RunForward"), Locomotion.Run);
            st.motion = tree;
            ac.layers[0].stateMachine.defaultState = st;
            EditorUtility.SetDirty(ac); AssetDatabase.SaveAssets();
            return ac;
        }
    }
}
