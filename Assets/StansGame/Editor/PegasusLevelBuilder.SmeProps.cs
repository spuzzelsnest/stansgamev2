using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Sainte-Mère-Église dressing from Joan's third batch of downloads (Models/Props/SME, made by
    /// Models/Source~/sme_props_1944b.py): telegraph poles and their wires along the N13 and the Chef-du-Pont road,
    /// buckets from the bucket chain between the pump and the burning Pommier house, a ladder against its wall,
    /// an Opel Blitz of the garrison parked on the N13 (and the flak battery's open one on the north road), and mortars (an 81 mm M1 and a 60 mm M2 with 3/505 in the orchard,
    /// a German 8 cm GrW 34 south of town; Models/Source~/mortars_split.py, the Pindad models standing in for them).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        const string SmePropDir = Root + "/Models/Props/SME";

        /// <summary>A prop FBX (Blender X east, Y = its front, Z up) on the ground at pos (pos.y = height above the ground), turned by yaw and tipped by tilt (about X).</summary>
        static GameObject SmeProp(string file, Transform parent, Vector3 pos, float yaw, float tilt = 0f, string name = null, bool collider = true)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{SmePropDir}/{file}.fbx");
            if (!asset) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name ?? file;
            pos.y += GroundAt(pos);   // pos.y is a lift above the ground (a cask on its side, a tipped bucket)
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f) * Quaternion.Euler(0f, 180f, 0f) * asset.transform.rotation);
            if (file == "OpelBlitzCovered" || file == "OpelBlitzOpen") PaintTruck(go);
            if (collider)
            {
                var b = UsKitImporter.WorldBounds(go);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = go.transform.InverseTransformPoint(b.center);
                bc.size = go.transform.InverseTransformVector(b.size);
                bc.size = new Vector3(Mathf.Abs(bc.size.x), Mathf.Abs(bc.size.y), Mathf.Abs(bc.size.z));
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static void SmeDownloadProps(Transform town, Transform env)
        {
            if (!AssetDatabase.IsValidFolder(SmePropDir)) return;
            var g = Group("Props (downloads, batch 3)", town);

            // ---- the bucket chain: buckets by the pump and dropped along the way to the fire
            var pump = SmePump;
            SmeProp("Bucket", g, pump + new Vector3(0.7f, 0f, 1.6f), 20f, 0f, "Bucket by the trough", false);
            SmeProp("Bucket", g, pump + new Vector3(-0.6f, 0f, 1.2f), 80f, 0f, "Bucket by the trough", false);
            for (int i = 0; i < 7; i++)
            {
                float t = (i + 0.5f) / 7f;
                var p = Vector3.Lerp(pump, SmeFireHouse + new Vector3(2f, 0f, 6f), t) + new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
                bool tipped = Random.value < 0.5f;
                SmeProp("Bucket", g, p + (tipped ? Vector3.up * 0.15f : Vector3.zero), Random.Range(0f, 360f), tipped ? 90f : 0f, "Bucket (dropped when the shooting started)", false);
            }
            // a ladder against the burning house, on the side facing the square
            SmeProp("Ladder", g, SmeFireHouse + new Vector3(2.5f, 0f, 4.5f + 1.2f), 0f, -14f, "Ladder against the Pommier house");
            // wooden wheelbarrows (Joan's brouette scan): one wheeled up with the buckets, one left by the pump
            SmeProp("Wheelbarrow", g, SmeFireHouse + new Vector3(7f, 0f, 8.5f), 35f, 0f, "Wheelbarrow (brought up to the fire)");
            SmeProp("Wheelbarrow", g, pump + new Vector3(-3.2f, 0f, -2.4f), 200f, 0f, "Wheelbarrow by the pump");

            // ---- an Opel Blitz of the garrison, parked on the N13 north of the town hall, bonnet to the north
            // (Opel Blitz 3.6-36S from the World at War truck pack, dark yellow with camouflage as in 1944; the older rip stays as a fallback)
            if (!ParkedLorry("OpelBlitzCovered", g, new Vector3(SmeN13X + 5.2f, 0f, 8f), 2f, "Opel Blitz (garrison truck, canvas tilt)"))
                SmeProp("OpelBlitz", g, new Vector3(SmeN13X + 5.2f, 0f, 8f), 2f, 0f, "Opel Blitz (garrison truck)");
            // ---- the flak battery's open Opel Blitz, on the verge of the north road below the gun in the field at (130, 115)
            ParkedLorry("OpelBlitzOpen", g, new Vector3(87.5f, 0f, 112f), 6f, "Opel Blitz (flak ammunition truck)");

            // ---- 81 mm mortars: 3/505 setting up in the orchard, and a German one south of town covering the square
            SmeProp("Mortar81", g, SmeRally + new Vector3(8f, 0f, 6f), 95f, 0f, "81 mm mortar M1 (3/505)");
            SmeProp("Mortar60", g, SmeRally + new Vector3(8f, 0f, 1f), 85f, 0f, "60 mm mortar M2 (3/505)");
            SmeProp("Mortar81", g, new Vector3(SmeN13X + 22f, 0f, -300f), 175f, 0f, "8 cm Granatwerfer 34 (German, Carentan road)");

            // ---- batch 4: the town sign at the three ways into town (photo 18), crates, casks and ammunition boxes
            SmeProp("TownSign", g, new Vector3(SmeN13X + 6.5f, 0f, -280f), 180f + Random.Range(-4f, 4f), Random.Range(-3f, 3f), "Town sign (N13, from Carentan)");
            SmeProp("TownSign", g, new Vector3(SmeN13X - 6.5f, 0f, 280f), Random.Range(-4f, 4f), Random.Range(-3f, 3f), "Town sign (N13, from Neuville)");
            SmeProp("TownSign", g, new Vector3(-200f, 0f, 52f), 90f + Random.Range(-4f, 4f), Random.Range(-3f, 3f), "Town sign (road from La Fiere)");
            // the garrison's stores, being loaded behind the Opel Blitz
            SmeProp("CrateStackA", g, new Vector3(SmeN13X + 6f, 0f, 1.2f), 90f, 0f, "Crates (garrison stores)");
            SmeProp("CrateWide", g, new Vector3(SmeN13X + 3.9f, 0f, 3.2f), 8f, 0f, "Crate");
            SmeProp("SupplyStack", g, new Vector3(SmeTownHall.x - 7.5f, 0f, SmeTownHall.z + 9f), 90f, 0f, "Stores under a tarpaulin (town hall)");
            SmeProp("Barrel", g, new Vector3(SmeTownHall.x - 6.8f, 0f, SmeTownHall.z - 7.5f), 0f, 0f, "Cider cask");
            SmeProp("Barrel", g, new Vector3(SmeTownHall.x - 6.0f, 0f, SmeTownHall.z - 8.3f), 40f, 0f, "Cider cask");
            SmeProp("Barrel", g, new Vector3(SmeTownHall.x - 6.4f, 0.37f, SmeTownHall.z - 6.6f), 70f, 90f, "Cider cask (on its side)");
            // by the pump: what the bucket chain stood on
            SmeProp("CrateSmall", g, pump + new Vector3(-2.2f, 0f, -1.2f), 25f, 0f, "Crate by the pump");
            SmeProp("CrateTall", g, pump + new Vector3(-2.6f, 0f, 0.3f), -10f, 0f, "Crate by the pump");
            // mortar bombs next to each mortar
            SmeProp("AmmoBox1", g, SmeRally + new Vector3(9.6f, 0f, 7.6f), 15f, 0f, "Mortar bombs (3/505)", false);
            SmeProp("AmmoBox3", g, SmeRally + new Vector3(9.4f, 0f, 2.6f), -20f, 0f, "Mortar bombs (3/505)", false);
            SmeProp("AmmoBox2", g, SmeRally + new Vector3(9.9f, 0f, 3.3f), 60f, 0f, "Mortar bombs (3/505)", false);
            SmeProp("AmmoBox1", g, new Vector3(SmeN13X + 23.6f, 0f, -301.6f), 80f, 0f, "Wurfgranaten (German mortar)", false);
            SmeProp("AmmoBox2", g, new Vector3(SmeN13X + 20.8f, 0f, -301.2f), 10f, 0f, "Wurfgranaten (German mortar)", false);

            SmeBatch6(g);
            // ---- telegraph poles and wires along the N13 and the road to Chef-du-Pont
            var wire = Mat("SME_Telegraph_Wire", new Color(0.06f, 0.06f, 0.06f), 0.3f, 0.6f);
            SmeTelegraphLine(g, new Vector3(SmeN13X + 6.5f, 0f, -470f), new Vector3(SmeN13X + 6.5f, 0f, 470f), wire);
            SmeTelegraphLine(g, new Vector3(SmeN13X - 8f, 0f, 52f), new Vector3(-500f, 0f, 52f), wire);
        }

        static void SmeTelegraphLine(Transform parent, Vector3 a, Vector3 b, Material wire)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>($"{SmePropDir}/TelegraphPole.fbx")) return;
            Physics.SyncTransforms();
            var line = new GameObject("Telegraph line").transform; line.SetParent(parent);
            Vector3 dir = (b - a); dir.y = 0f; float len = dir.magnitude; dir /= len;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            // insulator pins on the cross-arm (from the model): across the road, 8.75 m up, a little forward of the pole
            var pins = new[] { new Vector3(-1.65f, 8.75f, 0.17f), new Vector3(0.29f, 8.75f, 0.17f), new Vector3(1.78f, 8.75f, 0.17f) };
            Transform prev = null;
            for (float d = 0f; d <= len; d += 42f)
            {
                var p = a + dir * (d + Random.Range(-3f, 3f));
                p.y = GroundAt(p);
                // not through a house, a hedge or a tree
                if (Physics.CheckCapsule(p + Vector3.up * 1.5f, p + Vector3.up * 9f, 0.7f, ~0, QueryTriggerInteraction.Ignore)) { prev = null; continue; }
                // (SmeProp stands it on the ground itself: pass no height, or it is added twice and the pole sinks where the ground is low)
                var pole = SmeProp("TelegraphPole", line, new Vector3(p.x, 0f, p.z), yaw + Random.Range(-3f, 3f), Random.Range(-1.5f, 1.5f), "Telegraph pole", false);
                if (!pole) return;
                var cap = pole.AddComponent<CapsuleCollider>(); cap.direction = 1; cap.radius = 0.15f; cap.height = 9.4f;
                cap.center = pole.transform.InverseTransformPoint(p + Vector3.up * 4.7f);
                var t = pole.transform;
                if (prev)
                    for (int k = 0; k < pins.Length; k++)
                    {
                        var w = new GameObject("Wire").AddComponent<LineRenderer>();
                        w.transform.SetParent(line);
                        Vector3 s = PinWorld(prev, pins[k], yaw), e = PinWorld(t, pins[k], yaw);
                        const int n = 10;
                        w.positionCount = n + 1; w.useWorldSpace = true;
                        for (int i = 0; i <= n; i++)
                        {
                            float u = i / (float)n;
                            w.SetPosition(i, Vector3.Lerp(s, e, u) + Vector3.down * (4f * u * (1f - u) * 0.45f));   // sag
                        }
                        w.startWidth = w.endWidth = 0.025f; w.sharedMaterial = wire;
                        w.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                prev = t;
            }
        }

        static Vector3 PinWorld(Transform pole, Vector3 pin, float yaw) => pole.position + Quaternion.Euler(0f, yaw, 0f) * pin;

        /// <summary>
        /// The StuG III Ausf. G knocked out on the Neuville road (photos 14 and 17): part of the north counter-attack, so it
        /// appears with that wave. Burnt-out materials, slewed onto the verge and listing; solid, so it gives cover.
        /// </summary>
        static void SmeStugWreck(Transform parent)
        {
            var t = new GameObject("StuG III Ausf. G (knocked out on the Neuville road)").transform; t.SetParent(parent);
            var pos = new Vector3(SmeN13X + 3.4f, 0f, 300f); pos.y = GroundAt(pos);
            // facing south down the N13 towards the town, slewed towards the verge and listing into the ditch
            t.SetPositionAndRotation(pos, Quaternion.Euler(0f, 180f - 16f, 0f) * Quaternion.Euler(0f, 0f, -5f));
            var model = PlaceModel("StuG_III", t, 6.85f);
            if (model)
            {
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) if (ms[i]) ms[i] = BurntCopy(ms[i]);
                    r.sharedMaterials = ms;
                }
            }
            else
            {
                var burnt = Mat("StuG_Burnt", new Color(0.16f, 0.14f, 0.12f), 0.05f);
                Prim(PrimitiveType.Cube, "Hull", t, new Vector3(0f, 0.95f, 0f), Vector3.zero, new Vector3(2.9f, 1.3f, 5.4f), burnt);
                Prim(PrimitiveType.Cube, "Casemate", t, new Vector3(0f, 1.85f, 0.1f), Vector3.zero, new Vector3(2.2f, 0.6f, 2.6f), burnt);
            }
            var col = t.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0f, 1.05f, 0f); col.size = new Vector3(2.9f, 2.1f, 5.6f);
            // still smouldering: a dull red glow from the open hatches
            var glow = new GameObject("Smouldering").AddComponent<Light>(); glow.transform.SetParent(t, false);
            glow.transform.localPosition = new Vector3(0f, 2.4f, -0.4f);
            glow.type = LightType.Point; glow.range = 7f; glow.intensity = 1.2f; glow.color = new Color(1f, 0.38f, 0.15f);
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>A darkened, rougher copy of a material (burnt-out paint), saved next to the other generated materials.</summary>
        static Material BurntCopy(Material src)
        {
            string path = $"{MatDir}/Burnt_{src.name.Replace('/', '_')}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            m.CopyPropertiesFromMaterial(src);
            var c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.color : Color.white;
            var dark = new Color(c.r * 0.33f + 0.03f, c.g * 0.30f + 0.025f, c.b * 0.28f + 0.02f, 1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", dark);
            if (m.HasProperty("_Color")) m.SetColor("_Color", dark);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
