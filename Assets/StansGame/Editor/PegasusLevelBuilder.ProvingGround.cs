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
            if (!EditorUtility.DisplayDialog("Build the Proving Ground",
                $"Creates a new scene {SceneDir}/{ProvingScene}.unity (overwrites it if it exists). Continue?", "Build", "Cancel")) return;
            EnsureFolders();
            mats.Clear();
            Random.InitState(1944);
            PropTextureFixer.Fix();   // the vehicles' textures (the Panzer IV's camouflage among them)
            flatGround = true;
            try { BuildProvingGroundScene(); }
            finally { flatGround = false; }
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
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera"; cam.gameObject.AddComponent<AudioListener>();
            cam.transform.SetParent(env); cam.farClipPlane = 2500f; cam.clearFlags = CameraClearFlags.Skybox;
            cam.transform.position = new Vector3(0f, 8f, -30f); cam.transform.LookAt(new Vector3(10f, 0f, 5f));

            // ------------------------------------------------ the field
            var turf = Mat("PG_Turf", Color.white, 0.12f);
            var turfTex = GroundTex("Ground_Turf_Albedo.jpg", TextureImporterType.Default, true);
            if (turfTex) { turf.SetTexture("_BaseMap", turfTex); turf.SetTextureScale("_BaseMap", new Vector2(150f, 150f)); }
            else turf.SetColor("_BaseColor", new Color(0.33f, 0.42f, 0.22f));
            Prim(PrimitiveType.Plane, "Grass field", env, Vector3.zero, Vector3.zero, new Vector3(60f, 1f, 60f), turf, true);
            var gravel = Mat("PG_Gravel", Color.white, 0.2f);
            var gravelTex = GroundTex("Ground_Gravel_Albedo.jpg", TextureImporterType.Default, true);
            if (gravelTex) { gravel.SetTexture("_BaseMap", gravelTex); gravel.SetTextureScale("_BaseMap", new Vector2(8f, 3f)); }
            else gravel.SetColor("_BaseColor", new Color(0.5f, 0.48f, 0.44f));
            Prim(PrimitiveType.Cube, "Gravel apron (training station)", env, new Vector3(0f, 0.01f, -1f), Vector3.zero, new Vector3(26f, 0.02f, 10f), gravel, true);
            Prim(PrimitiveType.Cube, "Gravel apron (vehicle park)", env, new Vector3(31f, 0.01f, 0f), Vector3.zero, new Vector3(14f, 0.02f, 80f), gravel, true);

            // ------------------------------------------------ training station: the gun table under an awning
            var station = Group("Training station", root);
            var wood = Mat("PG_Wood", new Color(0.42f, 0.3f, 0.18f), 0.2f);
            var canvas = Mat("PG_Canvas", new Color(0.46f, 0.44f, 0.32f), 0.1f);
            var white = Mat("PG_Paint_White", new Color(0.88f, 0.86f, 0.8f), 0.2f);
            Prim(PrimitiveType.Cube, "Gun table", station, new Vector3(0f, 0.86f, 0f), Vector3.zero, new Vector3(11f, 0.08f, 1.2f), wood, true);
            foreach (float x in new[] { -5.2f, 0f, 5.2f }) foreach (float z in new[] { -0.5f, 0.5f })
                Prim(PrimitiveType.Cube, "Table leg", station, new Vector3(x, 0.41f, z), Vector3.zero, new Vector3(0.08f, 0.82f, 0.08f), wood, true);
            Prim(PrimitiveType.Cube, "Grenade table", station, new Vector3(8.2f, 0.86f, 0f), Vector3.zero, new Vector3(3f, 0.08f, 1.2f), wood, true);
            foreach (float x in new[] { 6.9f, 9.5f }) foreach (float z in new[] { -0.5f, 0.5f })
                Prim(PrimitiveType.Cube, "Table leg", station, new Vector3(x, 0.41f, z), Vector3.zero, new Vector3(0.08f, 0.82f, 0.08f), wood, true);
            foreach (float x in new[] { -6.5f, 0f, 10.2f }) foreach (float z in new[] { -1.6f, 1.6f })
                Prim(PrimitiveType.Cylinder, "Awning post", station, new Vector3(x, 1.4f, z), Vector3.zero, new Vector3(0.12f, 1.4f, 0.12f), wood, true);
            Prim(PrimitiveType.Cube, "Awning (canvas)", station, new Vector3(1.85f, 2.85f, 0f), new Vector3(6f, 0f, 0f), new Vector3(17.5f, 0.05f, 3.8f), canvas);
            var board = Prim(PrimitiveType.Cube, "Sign board", station, new Vector3(0f, 3.25f, -1.75f), Vector3.zero, new Vector3(5.2f, 0.7f, 0.06f), wood);
            Label(station, "TRAINING STATION  -  ARMOURY", new Vector3(0f, 3.33f, -1.79f), 0.05f, Color.white, Quaternion.identity);
            Label(station, "E: pick up  ·  a fresh one is put back after a few seconds", new Vector3(0f, 3.1f, -1.79f), 0.02f, new Color(0.95f, 0.9f, 0.75f), Quaternion.identity);

            var table = new GameObject("Armoury (weapons on the tables)").AddComponent<ArmouryTable>();
            table.transform.SetParent(station);
            var slots = new List<ArmouryTable.Slot>();
            string[] guns = { "Weapon_LeeEnfield", "Weapon_Sten", "Weapon_Bren", "Weapon_Garand", "Weapon_Carbine", "Weapon_Thompson",
                              "Weapon_M1911", "Weapon_Bazooka", "Weapon_Kar98k", "Weapon_MP40", "Weapon_MG42", "Weapon_Luger" };
            for (int i = 0; i < guns.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{guns[i]}.prefab");
                if (!prefab) { Debug.LogWarning($"Stan's Game: Proving Ground - no {guns[i]} prefab"); continue; }
                float x = -4.95f + i * 0.9f;
                var pos = new Vector3(x, 0.95f, 0.05f);
                slots.Add(new ArmouryTable.Slot { prefab = prefab, position = pos, rotation = Quaternion.Euler(0f, 0f, 90f) });   // on its side, muzzle to the range
                var w = prefab.GetComponent<Weapon>();
                Label(station, w ? w.displayName : guns[i].Replace("Weapon_", ""), new Vector3(x, 0.905f, -0.5f), 0.03f, new Color(0.15f, 0.12f, 0.08f), Quaternion.Euler(90f, 0f, 0f));
            }
            string[] throws = { "Grenade_Mills_Pickup", "Grenade_No82_Gammon_Pickup", "Grenade_MkII_Pickup", "Grenade_Stielhandgranate_Pickup",
                                "Smoke_No77_Pickup", "Smoke_ANM8_Pickup", "Smoke_Nb39_Pickup" };
            for (int i = 0; i < throws.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{throws[i]}.prefab");
                if (!prefab) continue;
                float x = 7.0f + i * 0.4f;
                slots.Add(new ArmouryTable.Slot { prefab = prefab, position = new Vector3(x, 0.97f, 0.1f), rotation = Quaternion.identity });
                Label(station, throws[i].Replace("_Pickup", "").Replace("Grenade_", "").Replace("Smoke_", "Smoke ").Replace("_", " "),
                      new Vector3(x, 0.905f, -0.45f), 0.022f, new Color(0.15f, 0.12f, 0.08f), Quaternion.Euler(90f, 0f, 0f));
            }
            table.slots = slots.ToArray();

            // the firing range behind the table, boards at 25, 50 and 100 m and an earth bank at the back
            var range = Group("Firing range", station);
            var boardMat = Mat("PG_Target", new Color(0.85f, 0.82f, 0.7f), 0.1f);
            var black = Mat("PG_Target_Black", new Color(0.08f, 0.08f, 0.08f), 0.1f);
            foreach (float d in new[] { 25f, 50f, 100f })
            {
                var t = Prim(PrimitiveType.Cube, $"Target board {d:0} m", range, new Vector3(0f, 1.2f, d), Vector3.zero, new Vector3(1.4f, 1.6f, 0.08f), boardMat, true);
                Prim(PrimitiveType.Cylinder, "Bull", t.transform, new Vector3(0f, 0.05f, -0.6f), new Vector3(90f, 0f, 0f), new Vector3(0.35f, 0.5f, 0.35f), black);
                foreach (float x in new[] { -0.6f, 0.6f }) Prim(PrimitiveType.Cube, "Stake", range, new Vector3(x, 0.4f, d + 0.06f), Vector3.zero, new Vector3(0.08f, 0.8f, 0.08f), wood, true);
                Label(range, $"{d:0} m", new Vector3(0f, 2.25f, d - 0.05f), 0.06f, Color.white, Quaternion.identity);
            }
            var earth = Mat("PG_Earth", new Color(0.36f, 0.3f, 0.22f), 0.1f);
            Prim(PrimitiveType.Cube, "Stop butt (earth bank)", range, new Vector3(0f, 2.5f, 112f), Vector3.zero, new Vector3(30f, 5f, 6f), earth, true);

            // ------------------------------------------------ the vehicle park
            var park = Group("Vehicle park", root);
            var keepOut = new List<Bounds>
            {
                new Bounds(new Vector3(2f, 1f, 0f), new Vector3(24f, 4f, 5f)),     // the tables and the awning
                new Bounds(new Vector3(0f, 1f, 60f), new Vector3(16f, 4f, 116f)),  // the firing range
            };
            float zz = -36f;
            void Park(GameObject v, string label)
            {
                if (!v) return;
                var bs = WorldBounds(v);
                keepOut.Add(bs);
                var post = Prim(PrimitiveType.Cube, "Sign post", park, new Vector3(23.2f, 0.6f, v.transform.position.z), Vector3.zero, new Vector3(0.08f, 1.2f, 0.08f), wood, true);
                Prim(PrimitiveType.Cube, "Sign", park, new Vector3(23.2f, 1.3f, v.transform.position.z), Vector3.zero, new Vector3(0.05f, 0.35f, 1.9f), white);
                Label(park, label, new Vector3(23.15f, 1.3f, v.transform.position.z), 0.035f, new Color(0.1f, 0.1f, 0.1f), Quaternion.Euler(0f, 90f, 0f));
            }
            GameObject Slot(System.Func<Vector3, GameObject> make, string label)
            {
                var v = make(new Vector3(31f, 0f, zz)); zz += 12f;
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
            BuildGrenadePrefabs();   // the player's grenades (and the ones on the table) need their prefabs made
            var player = BuildPlayer(actors);
            player.gameObject.name = "Player";
            player.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, -6f), Quaternion.identity);
            player.cam = cam;

            // ------------------------------------------------ everyone in the game, out for a stroll
            var people = Group("Characters (strolling, peaceful)", actors);
            var area = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(90f, 20f, 90f));
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
                // spread them round the field, clear of the station, the range and the park
                Vector3 p;
                int guard = 0;
                do { p = new Vector3(Random.Range(-40f, 18f), 0f, Random.Range(-40f, 40f)); }
                while (guard++ < 60 && keepOut.Exists(b => { var e = b; e.Expand(2f); return e.Contains(new Vector3(p.x, e.center.y, p.z)); }));
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                foreach (var ai in go.GetComponentsInChildren<SoldierAI>(true)) ai.enabled = false;   // nobody fights here
                bool armed = go.GetComponentInChildren<Weapon>(true);
                var anim = go.GetComponentInChildren<Animator>(true);
                if (anim && !armed) { anim.runtimeAnimatorController = stroll; anim.applyRootMotion = false; }
                var wd = go.AddComponent<Wanderer>();
                wd.area = area; wd.keepOut = keepOut.ToArray(); wd.armsDown = !armed;
                wd.label = Label(go.transform, NiceName(go.name), new Vector3(0f, 2.15f, 0f), 0.05f, new Color(1f, 0.92f, 0.7f), Quaternion.identity);
            }

            string scenePath = $"{SceneDir}/{ProvingScene}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == scenePath)) list.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"Stan's Game: Proving Ground built at {scenePath} - {slots.Count} things on the tables, 6 vehicles, {all.Count} characters strolling");
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
