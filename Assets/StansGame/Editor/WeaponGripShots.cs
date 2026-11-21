using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Debug > Capture Weapon Grips.
    /// Photographs every weapon prefab square from its right side (orthographic, 5 cm grid, 10 cm lines darker) into
    /// _downloads/previews/grip_*.png, with markers: white = the gun's pivot, red = where the right palm goes (handOffset),
    /// blue = where the left palm goes (supportHand), yellow = the magazine point. To check and tune Weapon.ApplySpecs.
    /// </summary>
    public static class WeaponGripShots
    {
        [MenuItem("Stan's Game/Debug/Capture Weapon Grips", priority = 55)]
        static void Capture()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_downloads", "previews");
            Directory.CreateDirectory(dir);
            var root = new GameObject("GripShots");
            root.transform.position = new Vector3(0f, -500f, 0f);
            var camGo = new GameObject("cam"); camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 0.55f; cam.aspect = 2f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.78f, 0.8f, 0.82f);
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 10f;
            var lightGo = new GameObject("light"); lightGo.transform.SetParent(root.transform, false);
            var lt = lightGo.AddComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.6f;
            lightGo.transform.rotation = Quaternion.Euler(30f, -60f, 0f);
            var rt = new RenderTexture(1400, 700, 24); cam.targetTexture = rt;
            var tex = new Texture2D(1400, 700, TextureFormat.RGB24, false);

            var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material M(Color c) { var m = new Material(unlit); m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); return m; }
            Material gridA = M(new Color(0.62f, 0.64f, 0.66f)), gridB = M(new Color(0.45f, 0.47f, 0.5f)), axis = M(new Color(0.2f, 0.2f, 0.2f));
            Material white = M(Color.white), red = M(new Color(0.9f, 0.1f, 0.1f)), blue = M(new Color(0.1f, 0.3f, 1f)), yellow = M(new Color(1f, 0.85f, 0f));

            GameObject Box(Transform parent, Vector3 pos, Vector3 size, Material m)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(g.GetComponent<Collider>());
                g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = size;
                g.GetComponent<Renderer>().sharedMaterial = m; return g;
            }
            GameObject Dot(Transform parent, Vector3 pos, Material m, float d = 0.022f)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(g.GetComponent<Collider>());
                g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = Vector3.one * d;
                g.GetComponent<Renderer>().sharedMaterial = m; return g;
            }

            // the grid behind the gun (in the gun's y-z plane, a little to its left so it is behind it from the right)
            var grid = new GameObject("grid").transform; grid.SetParent(root.transform, false);
            for (int i = -12; i <= 30; i++)
                Box(grid, new Vector3(-0.3f, 0f, i * 0.05f), new Vector3(0.001f, 1.2f, i == 0 ? 0.004f : 0.0018f), i == 0 ? axis : i % 2 == 0 ? gridB : gridA);
            for (int i = -10; i <= 10; i++)
                Box(grid, new Vector3(-0.3f, i * 0.05f, 0.45f), new Vector3(0.001f, i == 0 ? 0.004f : 0.0018f, 2.2f), i == 0 ? axis : i % 2 == 0 ? gridB : gridA);

            // looking at the gun's right side: from +x towards -x
            cam.transform.localPosition = new Vector3(2f, 0f, 0.35f);
            cam.transform.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);

            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/StansGame/Prefabs" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (!prefab || !prefab.name.StartsWith("Weapon_")) continue;
                // made under a switched-off parent, so the Weapon can be named before it sets itself up
                var parked = new GameObject("parked"); parked.SetActive(false);
                var go = Object.Instantiate(prefab, parked.transform);
                var w = go.GetComponentInChildren<Weapon>(true);
                if (!w) { w = go.AddComponent<Weapon>(); w.displayName = prefab.name.Substring(7); }   // the soldiers add it to their copy
                else if (w.transform != go.transform) Debug.Log($"{prefab.name}: the Weapon sits on a child, {w.transform.localPosition} from the root");
                w.Init();   // applies the hold (support point, magazine point, palm point) by name
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
                Object.DestroyImmediate(parked);
                var marks = new GameObject("marks").transform; marks.SetParent(go.transform, false);
                Dot(marks, Vector3.zero, white, 0.016f);
                Dot(marks, w.handOffset, red);
                Dot(marks, w.supportHand, blue);
                Dot(marks, w.magazinePoint, yellow, 0.016f);
                if (w.muzzle) Dot(marks, go.transform.InverseTransformPoint(w.muzzle.position), axis, 0.02f);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // the marks in front of the gun so they show
                marks.localPosition = new Vector3(0.25f, 0f, 0f);
                cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1400, 700), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"grip_{prefab.name}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(go);
                n++;
            }
            cam.targetTexture = null; rt.Release();
            Object.DestroyImmediate(root);
            Debug.Log($"Stan's Game: {n} weapons photographed for their grips into {dir} (grid 5 cm; origin = pivot; view from the right, muzzle to the right)");
        }
    }
}
