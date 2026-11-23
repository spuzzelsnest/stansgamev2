using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
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
    /// Pegasus Bridge, a little more like GTA (Joan, Nov 2026): a punchier colour grade, and a place that lives before
    /// the gliders come in - two-man patrols walking their rounds on both banks, German lorries driving the
    /// Bénouville-Ranville road over the bridge, and Bénouville's windows lighting up one by one once the shooting starts.
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static void PegasusAmbientLife(Transform root, Transform env, Transform defences, Transform actors)
        {
            ColourGrade(root, "PegasusGrade");
            PegasusPatrols(defences);
            PegasusTraffic(actors);
            PegasusWindowLights(env);
            PegasusHalifaxes(actors);
            PegasusWheelbarrows(env);
        }

        /// <summary>
        /// Multiplayer: the host runs this piece of scenery and the others see it where the host has it (a network
        /// object with a NetworkTransform; with hit points shared too when it can be shot).
        /// </summary>
        static void NetScenery(GameObject go, bool hasHealth)
        {
#if STANS_NET
            var no = go.GetComponent<NetworkObject>(); if (!no) no = go.AddComponent<NetworkObject>();
            no.AutoObjectParentSync = false;
            if (!go.GetComponent<NetworkTransform>())
            {
                var nt = go.AddComponent<NetworkTransform>();
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            }
            if (hasHealth && !go.GetComponent<NetEntity>()) go.AddComponent<NetEntity>();
#endif
        }

        // ---------------------------------------------------------------- colour

        /// <summary>
        /// Warmer lights, cooler shadows, more saturation and contrast, a soft bloom round every lamp and muzzle flash:
        /// still the night of 5/6 June, but with the colour turned up. The film scenes' black-and-white look sits above it.
        /// </summary>
        static void ColourGrade(Transform root, string assetName, float warmth = 12f)
        {
            string dir = Root + "/Settings";
            Directory.CreateDirectory(dir);
            string path = $"{dir}/{assetName}.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.45f); ca.contrast.Override(24f); ca.saturation.Override(14f);
            // the moonlight in this level is already very blue: pull it back towards neutral so the warm lights stand out
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(warmth); wb.tint.Override(4f);
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(1f, 1f, 1.04f, -0.02f));        // deep, slightly cool blacks
            smh.midtones.Override(new Vector4(1.03f, 1f, 0.97f, 0f));
            smh.highlights.Override(new Vector4(1.12f, 1.0f, 0.85f, 0.03f));  // lamps, fires and flashes warmer
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.85f); bloom.intensity.Override(0.9f); bloom.scatter.Override(0.72f); bloom.tint.Override(new Color(1f, 0.86f, 0.72f));
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.22f); vig.smoothness.Override(0.45f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();

            var go = new GameObject("Colour grade (GTA look)");
            go.transform.SetParent(root);
            var vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 1f; vol.weight = 1f; vol.sharedProfile = profile;
        }

        // ---------------------------------------------------------------- patrols

        static Vector3 OnGround(float x, float z) { var p = new Vector3(x, 0f, z); p.y = GroundAt(p); return p; }

        static void PegasusPatrols(Transform defences)
        {
            var g = Group("Patrols", defences);
            // west bank: along the canal road in front of the café, up the Bénouville road a little, and back
            var west = new[] { OnGround(WestBankRoadX, -60f), OnGround(WestBankRoadX, 50f), OnGround(WestBankRoadX, 5f), OnGround(-75f, 2.5f), OnGround(WestBankRoadX - 1f, -3f) };
            // east bank: down the road along the canal towards the landing field, and back up to the Ranville road
            var east = new[] { OnGround(26.5f, -3f), OnGround(20f, -45f), OnGround(18f, -85f), OnGround(20f, -45f), OnGround(28.5f, 0f), OnGround(30f, 40f) };
            var lead = Patrol(g, "Sentry patrol 1 (west bank)", "Sentry patrol 2 (west bank)", west);
            if (lead) GuardDogFor(lead.transform, g);   // the west-bank patrol has a dog
            Patrol(g, "Sentry patrol 3 (east bank)", "Sentry patrol 4 (east bank)", east);
        }

        static Health Patrol(Transform g, string lead, string second, Vector3[] route)
        {
            Health first = null;
            Vector3 dir = route[1] - route[0]; dir.y = 0f; dir.Normalize();
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = route[0] - dir * (i * 3f) + Vector3.Cross(Vector3.up, dir) * (i * 0.8f);
                p.y = GroundAt(p);
                var h = Soldier(i == 0 ? lead : second, g, p, yaw);
                if (i == 0) first = h;
                var ai = h ? h.GetComponent<SoldierAI>() : null;
                if (!ai) continue;
                // the second man walks the same round three paces behind (same pauses, so they stay together)
                var r = new Vector3[route.Length];
                for (int k = 0; k < route.Length; k++) r[k] = k == 0 ? route[k] - dir * (i * 3f) : route[k];   // the same path, so he never cuts through a bunker or sandbags
                ai.patrolRoute = r; ai.patrolPause = 2.5f; ai.walkSpeed = 1.25f;
                ai.sleepsUntilAlarm = false; ai.combatRoam = 10f;
            }
            return first;
        }

        // ---------------------------------------------------------------- traffic

        static void PegasusTraffic(Transform actors)
        {
            var g = Group("Traffic", actors);
            // the road from Bénouville over the bridge to Ranville (game x runs west -> east)
            // The level starts as the gliders come down, so the lorries are already on the road: the first one comes up
            // from Bénouville towards the bridge in the first seconds, the other from Ranville a little later.
            var road = new List<Vector3> { new Vector3(-200f, 0f, 7f), BenouvilleBend, new Vector3(-30f, 0f, 0.3f) };
            foreach (var r in RanvilleRoad) if (r.x < 330f) road.Add(new Vector3(r.x, 0f, r.y));
            road.Add(new Vector3(330f, 0f, -11.5f));
            var eastbound = Lane(road, 1.7f);
            var back = new List<Vector3> { new Vector3(260f, 0f, -14f) };
            foreach (var r in RanvilleRoad) if (r.x < 250f) back.Insert(1, new Vector3(r.x, 0f, r.y));
            back.Add(new Vector3(-30f, 0f, 0.3f)); back.Add(BenouvilleBend); back.Add(new Vector3(BenouvilleCrossroads.x, 0f, BenouvilleCrossroads.z)); back.Add(new Vector3(-330f, 0f, 12f));
            var westbound = Lane(back, 1.7f);
            AmbientLorry(g, "OpelBlitzCovered", "Opel Blitz (to Ranville)", eastbound, 2f, 150f);
            AmbientLorry(g, "OpelBlitzOpen", "Opel Blitz (to Bénouville)", westbound, 14f, 170f);
        }

        /// <summary>The right-hand lane of a centre line (they drove on the right), set on the ground.</summary>
        static Vector3[] Lane(List<Vector3> centre, float offset)
        {
            var o = new Vector3[centre.Count];
            for (int i = 0; i < centre.Count; i++)
            {
                Vector3 d = (i < centre.Count - 1 ? centre[i + 1] - centre[i] : centre[i] - centre[i - 1]); d.y = 0f; d.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, d);
                Vector3 p = centre[i] + right * offset; p.y = GroundAt(p);
                o[i] = p;
            }
            return o;
        }

        static void AmbientLorry(Transform parent, string model, string name, Vector3[] route, float first, float every, bool stopOnAlarm = true, int maxRuns = 0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/Props/SME/{model}.fbx");
            if (!prefab) { Debug.LogWarning($"Stan's Game: {model} not found - no traffic"); return; }
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(route[0], Quaternion.identity);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
            body.name = "Body";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * prefab.transform.localRotation;   // front to +Z
            PaintTruck(body);   // Wehrmacht colours (PegasusLevelBuilder.Paint.cs)
            var rs = body.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = b.center - go.transform.position + new Vector3(0f, 0.15f, 0f); box.size = b.size - new Vector3(0f, 0.3f, 0f);
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            var wheels = new List<Transform>();
            foreach (var t in body.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("Wheel_")) wheels.Add(t);
            // the Notek blackout lamp on the left wing: a dim, low, slit beam
            var lamp = new GameObject("Notek blackout lamp").AddComponent<Light>();
            lamp.transform.SetParent(go.transform, false);
            lamp.transform.localPosition = new Vector3(-0.6f, 1.0f, b.max.z - go.transform.position.z + 0.05f);
            lamp.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
            lamp.type = LightType.Spot; lamp.spotAngle = 75f; lamp.range = 24f; lamp.intensity = 4f;
            lamp.color = new Color(0.78f, 0.82f, 1f); lamp.shadows = LightShadows.None;
            var av = go.AddComponent<AmbientVehicle>();
            av.route = route; av.speed = 8f; av.firstDeparture = first; av.repeatEvery = every;
            av.wheels = wheels.ToArray(); av.wheelRadius = 0.44f; av.lamps = new[] { lamp };
            av.stopOnAlarm = stopOnAlarm; av.maxRuns = maxRuns;
            Vector3 d0 = route[1] - route[0]; d0.y = 0f;
            go.transform.rotation = Quaternion.LookRotation(d0.normalized, Vector3.up);
            go.transform.position = route[0] + Vector3.down * AmbientVehicle.HiddenDepth;   // parked off the map until it sets off
            // and the players can take it: walk up, E, drive it in third person (front wheels steer)
            var tc = go.AddComponent<TruckController>();
            tc.displayName = "Opel Blitz";
            tc.maxForwardSpeed = 21f; tc.maxReverseSpeed = 3.5f; tc.acceleration = 2.2f;   // 3.6-36S: ~75 km/h
            tc.wheels = av.wheels; tc.wheelRadius = av.wheelRadius; tc.lamps = av.lamps;
            NetScenery(go, false);
#if STANS_NET
            if (!go.GetComponent<NetTruck>()) go.AddComponent<NetTruck>();
#endif
        }

        // ---------------------------------------------------------------- windows

        static void PegasusWindowLights(Transform env)
        {
            var bld = env.Find("Buildings");
            if (bld) WindowLightsFor(bld, 6644, 0.12f, 0f, Vector3.zero, 16);   // the blackout: only a few curtained windows show a glow
        }

        /// <summary>
        /// Lamps behind a share of the "Window" panes of the houses under a group: lit one by one after the alarm
        /// (some already at the start, if litAtStart > 0), with a warm spill of light in front of one window per house
        /// for the houses nearest the centre (at most maxSpill of them: real-time lights cost).
        /// </summary>
        static void WindowLightsFor(Transform group, int seed, float share, float litAtStart, Vector3 centre, int maxSpill)
        {
            // the blackout: every window is curtained, so a lamp inside shows only as a faint glow round the edges
            var lit = Mat("Window_Lit", new Color(0.12f, 0.08f, 0.05f), 0.8f);
            lit.EnableKeyword("_EMISSION");
            lit.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.25f) * 0.35f);
            lit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(lit);
            var houses = new List<Transform>();
            foreach (Transform h in group) houses.Add(h);
            houses.Sort((p, q) => (p.position - centre).sqrMagnitude.CompareTo((q.position - centre).sqrMagnitude));
            var windows = new List<Renderer>(); var spill = new List<Light>();
            var rng = new System.Random(seed);
            int lamps = 0;
            foreach (var house in houses)
            {
                bool lamp = false;
                foreach (Transform w in house)
                {
                    if (w.name != "Window" || rng.NextDouble() > share) continue;
                    var r = w.GetComponent<Renderer>(); if (!r) continue;
                    windows.Add(r);
                    Light l = null;
                    if (!lamp && lamps < maxSpill)
                    {
                        lamp = true; lamps++;
                        Vector3 outward = house.TransformDirection(new Vector3(0f, 0f, Mathf.Sign(w.localPosition.z)));
                        l = new GameObject("Window light").AddComponent<Light>();
                        l.transform.SetParent(house, false);
                        l.transform.position = w.position + outward * 1.3f;
                        l.type = LightType.Point; l.range = 3f; l.intensity = 0.25f;   // only what slips past the curtain
                        l.color = new Color(1f, 0.72f, 0.42f); l.shadows = LightShadows.None;
                    }
                    spill.Add(l);
                }
            }
            if (windows.Count == 0) return;
            var wl = group.gameObject.AddComponent<WindowLights>();
            wl.windows = windows.ToArray(); wl.spill = spill.ToArray(); wl.lit = lit; wl.seed = seed; wl.litAtStart = litAtStart;
            Debug.Log($"Stan's Game: {windows.Count} windows under {group.name} will light up ({lamps} with a spill of light)");
        }

        // ---------------------------------------------------------------- the dog

        const string AnimalDir = Root + "/Models/Animals";

        /// <summary>
        /// The German shepherd (_downloads/german-shepherd-rig, animated in Blender by Models/Source~/dog_shepherd.py with
        /// Idle, Walk and Trot loops): generic rig, looping clips, its own little controller.
        /// </summary>
        static GameObject ShepherdModel(out RuntimeAnimatorController controller)
        {
            controller = null;
            string path = $"{AnimalDir}/GermanShepherd.fbx";
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!mi) return null;
            if (mi.animationType != ModelImporterAnimationType.Generic || mi.clipAnimations.Length == 0 || mi.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                mi.animationType = ModelImporterAnimationType.Generic;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = true;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                var clips = mi.defaultClipAnimations;
                foreach (var c in clips) { c.loopTime = true; c.name = c.name.Substring(c.name.LastIndexOf('|') + 1); }
                mi.clipAnimations = clips;
                mi.SaveAndReimport();
            }
            string cpath = Root + "/Animations/GermanShepherd.controller";
            AssetDatabase.DeleteAsset(cpath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(cpath);
            var sm = ac.layers[0].stateMachine;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview"))
                {
                    var st = sm.AddState(clip.name); st.motion = clip;
                    if (clip.name == "Idle") sm.defaultState = st;
                }
            controller = ac;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static void GuardDogFor(Transform handler, Transform parent)
        {
            var model = ShepherdModel(out var controller);
            if (!model) { Debug.LogWarning("Stan's Game: GermanShepherd.fbx missing - the patrol goes without its dog"); return; }
            var go = new GameObject("Guard dog (Diensthund)");
            go.transform.SetParent(parent);
            Vector3 p = handler.position - handler.right * 1.0f; p.y = GroundAt(p);
            go.transform.SetPositionAndRotation(p, handler.rotation);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, go.transform);
            inst.name = "Model";
            inst.transform.localPosition = Vector3.zero;
            var rs = inst.GetComponentsInChildren<Renderer>();
            foreach (var r in rs)
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i] ? ms[i].name.ToLowerInvariant() : "";
                    ms[i] = n.Contains("hair") ? TexMat("Dog_Hair", $"{AnimalDir}/Dog_Hair.png", Color.white, 0.1f, true)
                          : n.Contains("mouth") || n.Contains("teeth") ? TexMat("Dog_Mouth", $"{AnimalDir}/Dog_Mouth.png", 0.4f)
                          : n.Contains("head") ? TexMat("Dog_Head", $"{AnimalDir}/Dog_Head.png", 0.15f)
                          : n.Contains("eye") ? Mat("Dog_Eye", new Color(0.05f, 0.03f, 0.02f), 0.9f)
                          : TexMat("Dog_Body", $"{AnimalDir}/Dog_Body.png", 0.15f);
                }
                r.sharedMaterials = ms;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            var hair = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Dog_Hair.mat");
            if (hair) { hair.SetFloat("_Cull", 0f); hair.doubleSidedGI = true; EditorUtility.SetDirty(hair); }
            // nose to +Z (the head bone ahead of the middle), 0.80 m to the ear tips, feet on the ground
            Transform head = null;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) if (t.name == "Head") head = t;
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (head)
            {
                Vector3 d = head.position - b.center; d.y = 0f;
                if (d.sqrMagnitude > 1e-6f) inst.transform.rotation = Quaternion.FromToRotation(d.normalized, go.transform.forward) * inst.transform.rotation;
            }
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y > 0.01f) inst.transform.localScale *= 0.80f / b.size.y;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.position += new Vector3(go.transform.position.x - b.center.x, go.transform.position.y - b.min.y, go.transform.position.z - b.center.z);
            var anim = inst.GetComponent<Animator>(); if (!anim) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var col = go.AddComponent<CapsuleCollider>(); col.direction = 2; col.radius = 0.2f; col.height = 1.0f; col.center = new Vector3(0f, 0.45f, 0f);
            var hp = go.AddComponent<Health>(); hp.faction = Faction.German; hp.maxHealth = 35f;
            var dog = go.AddComponent<GuardDog>(); dog.handler = handler; dog.animator = anim;
            NetScenery(go, true);
        }

        // ---------------------------------------------------------------- aircraft

        /// <summary>
        /// The Halifaxes of 298 and 644 Squadrons towed the six Horsas over, cast them off and flew on to bomb the
        /// cement works at Caen as a diversion; more bombers droned over all night. A vic of three passes low over the
        /// bridge every couple of minutes (far lower than they really flew: the night haze would hide them any higher), south towards Caen and back north. (Halifax B.III, CC0, from airmuseums.co.uk,
        /// undercarriage removed and the D-Day stripes painted on by Models/Source~/halifax_b3.py.)
        /// </summary>
        static void PegasusHalifaxes(Transform actors)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/Aircraft/Halifax.fbx");
            if (!model) { Debug.LogWarning("Stan's Game: Halifax.fbx missing - no bombers overhead"); return; }
            var vic = new GameObject("Halifax vic (298/644 Sqn)");
            vic.transform.SetParent(actors);
            foreach (var (off, name) in new[] { (Vector3.zero, "Leader"), (new Vector3(-45f, -12f, -55f), "No. 2"), (new Vector3(45f, 8f, -60f), "No. 3") })
            {
                var a = (GameObject)PrefabUtility.InstantiatePrefab(model, vic.transform);
                a.name = $"Halifax ({name})";
                a.transform.localPosition = off;
                a.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;   // nose to +Z
            }
            vic.AddComponent<PropellerSpin>();   // nose to +Z: the airscrews turn
            var fly = vic.AddComponent<AmbientAircraft>();
            // game +Z is bearing 28: south to Caen is -Z; the passes cross the canal over Bénouville and over the bridge
            fly.passes = new[]
            {
                new AmbientAircraft.Pass { from = new Vector3(-180f, 135f, 2600f), to = new Vector3(-60f, 135f, -2600f) },
                new AmbientAircraft.Pass { from = new Vector3(260f, 155f, -2600f), to = new Vector3(40f, 155f, 2600f) },
                new AmbientAircraft.Pass { from = new Vector3(-600f, 125f, 2400f), to = new Vector3(300f, 125f, -2600f) },
            };
            fly.speed = 72f; fly.firstPassAt = 20f; fly.gap = new Vector2(70f, 140f);
            vic.transform.position = new Vector3(0f, -2000f, 0f);   // waits far below the map between passes
            NetScenery(vic, false);
        }

        // ---------------------------------------------------------------- wheelbarrows

        static void PegasusWheelbarrows(Transform env)
        {
            var b = env.Find("Buildings");
            if (!b) return;
            // an old wooden brouette in the garden of the second house by the café, and one by the Bénouville road
            GroundProp("Wheelbarrow", b, new Vector3(-CanalHalfWidth - 48f, 0f, -34f), 70f, "Wheelbarrow (garden)", true);
            Vector3 hp = Vector3.Lerp(BenouvilleBend, BenouvilleCrossroads, 0.125f);
            GroundProp("Wheelbarrow", b, new Vector3(hp.x + 6f, 0f, hp.z + 8.5f), 160f, "Wheelbarrow (Bénouville road)", true);
        }
    }
}
