using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Capture Preview Shots.
    /// Renders the open level from fixed viewpoints in temporary daylight (so details are visible) and saves
    /// PNGs to _downloads/previews/. The viewpoints match Joan's reference photos, so model and photo can be
    /// compared side by side. Nothing in the scene is changed permanently.
    /// </summary>
    public static class PreviewShots
    {
        struct Shot
        {
            public string name, around; public Vector3 pos, look; public float fov;
            public Shot(string n, Vector3 p, Vector3 l, float f = 60f, string aroundObject = null) { name = n; pos = p; look = l; fov = f; around = aroundObject; }
        }

        static readonly Shot[] Shots =
        {
            new Shot("01_cafe_from_road (like IMG_8815)", new Vector3(-33f, 1.7f, 16f), new Vector3(-38f, 4f, -6f)),
            new Shot("02_cafe_from_east_bank (like IMG_2885)", new Vector3(24f, 1.7f, -34f), new Vector3(-36f, 3f, -8f), 55f),
            new Shot("03_bridge_from_canal (like IMG_8802)", new Vector3(12f, -1.0f, -45f), new Vector3(0f, 5f, 0f), 65f),
            new Shot("04_overview_from_southeast", new Vector3(260f, 160f, -360f), new Vector3(0f, 0f, 0f), 50f),
            new Shot("05_lz_looking_at_bridge", new Vector3(70f, 2.2f, -160f), new Vector3(0f, 2f, 0f), 60f),
            new Shot("26_lz_from_above (like the RAF vertical, June 1944)", new Vector3(55f, 115f, -58f), new Vector3(55f, 0f, -57.5f), 62f),
            new Shot("27_gliders_low_oblique", new Vector3(10f, 22f, -20f), new Vector3(70f, 0f, -95f), 55f),
            new Shot("07_gliders_and_tree_line (like pb01)", new Vector3(-30f, 7f, 8f), new Vector3(80f, 3f, -60f), 55f),
            new Shot("08_horsa_glider_1", new Vector3(-12f, 2.5f, 16f), new Vector3(0f, 2f, 0f), 55f, "Horsa glider 1"),
            new Shot("09_horsa_glider_2_rear", new Vector3(14f, 4f, -18f), new Vector3(0f, 2f, 2f), 55f, "Horsa glider 2"),
            new Shot("10_lead_vehicle_hotchkiss", new Vector3(-7f, 2.2f, 8f), new Vector3(0f, 1f, 0f), 50f, "Hotchkiss"),
            new Shot("12_pillbox_sandbags", new Vector3(23f, 2.6f, 2f), new Vector3(33f, 1f, 13f), 55f),
            new Shot("13_at_gun_pit", new Vector3(24f, 3.2f, -2f), new Vector3(31f, 0.3f, -10f), 55f),
            new Shot("14_horsa_side (like Horsa glider.png)", new Vector3(-24f, 3f, 0f), new Vector3(0f, 3f, 0f), 55f, "Horsa glider 1"),
            new Shot("15_horsa_from_above", new Vector3(0.01f, 34f, -0.5f), new Vector3(0f, 2f, 0f), 55f, "Horsa glider 1"),
            new Shot("16_bridge_west_end_barrier", new Vector3(-25f, 1.8f, -7f), new Vector3(-12f, 1.6f, 0f), 60f),
            new Shot("17_trenches_and_spoil", new Vector3(31f, 3.5f, 30f), new Vector3(48f, 0f, 18f), 55f),
            new Shot("18_wire_by_glider_1", new Vector3(31f, 2.4f, -36f), new Vector3(56f, 0.6f, -22f), 55f),
            new Shot("19_brushwood_like_pb11", new Vector3(58f, 2.4f, -100f), new Vector3(10f, 2f, -40f), 60f),
            new Shot("20_wire_rolls_like_pb01", new Vector3(-31f, 8f, -16f), new Vector3(15f, 0f, 12f), 60f),
            new Shot("21_trees_west_canal_road", new Vector3(-27f, 2f, -40f), new Vector3(-31f, 9f, -110f), 60f),
            new Shot("22_bridge_east_end_ladders", new Vector3(26f, 2.2f, -10f), new Vector3(16f, 1.6f, -3f), 55f),
            new Shot("23_bridge_west_end_ladders", new Vector3(-24f, 2.2f, 11f), new Vector3(-6f, 3.5f, 0f), 60f),
            new Shot("24_bridge_side_from_quay", new Vector3(-13.5f, 1.7f, -15f), new Vector3(2f, 1.2f, 0f), 65f),
            new Shot("35_pillbox_from_the_bridge_side", new Vector3(26.5f, 1.7f, 19.5f), new Vector3(33f, 1.0f, 14f), 60f),
            new Shot("36_pillbox_back", new Vector3(40f, 2.4f, 21f), new Vector3(33f, 1.2f, 14.5f), 60f),
            new Shot("37_slit_trench_close", new Vector3(39.8f, 2.0f, 21.2f), new Vector3(37f, -0.8f, 18f), 62f),
            new Shot("33_horsa_bridge_side", new Vector3(-30f, 3f, -34f), new Vector3(0f, -2f, 0f), 60f, "Horsa Bridge span"),
            new Shot("34_horsa_bridge_road", new Vector3(-38f, 1.8f, 0.5f), new Vector3(0f, 1f, 0f), 60f, "Horsa Bridge span"),
            new Shot("28_bridge_raised_from_west_quay", new Vector3(-17f, 1.7f, -24f), new Vector3(5f, 6f, 0f), 62f),
            new Shot("29_bridge_from_south_bank", new Vector3(3f, 1.7f, -32f), new Vector3(3f, 4f, 0f), 66f),
            new Shot("30_bridge_keepers_hut", new Vector3(19.5f, 1.7f, 13.5f), new Vector3(25f, 1.0f, 8f), 62f),
            new Shot("31_hut_controller_inside", new Vector3(24.8f, 1.65f, 7.2f), new Vector3(23.4f, 1.0f, 8.1f), 70f),
            new Shot("32_counterweight_pit_raised", new Vector3(26f, 4.5f, -9f), new Vector3(15f, 0f, 0f), 62f),
            new Shot("25_hedgerow_along_LZ", new Vector3(78f, 2.2f, -34f), new Vector3(100f, 1.2f, -16f), 60f),
            new Shot("06_benouville_side_from_west_end", new Vector3(-14f, 1.7f, 2f), new Vector3(-200f, 0f, -60f), 60f),
        };

        [MenuItem("Stan's Game/Debug/Screenshots: Capture Preview Shots", priority = 302)]
        public static void Capture()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_downloads", "previews");
            Directory.CreateDirectory(dir);

            // temporary daylight
            bool fog = RenderSettings.fog; var amb = RenderSettings.ambientLight; var ambMode = RenderSettings.ambientMode;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            var sunGo = new GameObject("_PreviewSun") { hideFlags = HideFlags.HideAndDontSave };
            var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.3f; sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(45f, 200f, 0f);

            var camGo = new GameObject("_PreviewCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.74f, 0.88f);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 2500f;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            try
            {
                foreach (var s0 in Shots)
                {
                    var s = s0;
                    if (!string.IsNullOrEmpty(s.around))
                    {
                        // positions relative to an object (in its own frame), e.g. a glider wherever it rests
                        GameObject target = null;
                        foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                            if (go.name.StartsWith(s.around)) { target = go.gameObject; break; }
                        if (!target) continue;
                        var tt = target.transform;
                        Vector3 flatFwd = Vector3.ProjectOnPlane(tt.forward, Vector3.up).normalized;
                        var rot = Quaternion.LookRotation(flatFwd);
                        s.pos = tt.position + rot * s.pos; s.look = tt.position + rot * s.look;
                        cam.fieldOfView = s.fov; cam.transform.position = s.pos; cam.transform.LookAt(s.look);
                        cam.Render();
                        var prev0 = RenderTexture.active; RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                        RenderTexture.active = prev0;
                        File.WriteAllBytes(Path.Combine(dir, s.name + ".png"), tex.EncodeToPNG());
                        continue;
                    }
                    var lift = s.name.Contains("_raised") ? Object.FindAnyObjectByType<BridgeLift>() : null;
                    if (lift) lift.PoseNow(lift.maxAngle);
                    cam.fieldOfView = s.fov;
                    // heights in the list are above the ground (eye level); the look-at points too
                    var terrain = Terrain.activeTerrain;
                    float g(Vector3 p) => terrain ? terrain.SampleHeight(p) + terrain.transform.position.y : 0f;
                    // shots from the canal (negative y) stay at water level
                    cam.transform.position = s.pos.y < 0f ? s.pos : s.pos + Vector3.up * g(s.pos);
                    cam.transform.LookAt(s.look + Vector3.up * g(s.look));
                    cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, s.name + ".png"), tex.EncodeToPNG());
                    if (lift) lift.PoseNow(0f);
                }
                Debug.Log($"Stan's Game: {Shots.Length} preview shots saved to {dir}");
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(sunGo);
                Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = amb;
            }
        }
    }
}
