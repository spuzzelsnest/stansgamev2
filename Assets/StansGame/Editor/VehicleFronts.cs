using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Debug > Check Vehicle Fronts.
    /// Every lorry, car and tank in the open level is photographed from its right-hand side with a red arrow above it
    /// along its forward direction (+Z, the way W drives it), all on one contact sheet: Logs/vehicle-fronts.png.
    /// A model that faces the arrow is right; one that faces away is turned round. The report
    /// (Logs/vehicle-fronts.txt) lists which wheels steer and where the gun points. Nothing in the scene is changed.
    /// </summary>
    public static class VehicleFronts
    {
        const int W = 480, H = 270, Cols = 3;

        [MenuItem("Stan's Game/Debug/Check: Vehicle Fronts (open level)", priority = 206)]
        public static void Check()
        {
            var roots = new List<Transform>();
            foreach (var t in Object.FindObjectsByType<TruckController>(FindObjectsInactive.Include)) roots.Add(t.transform);
            foreach (var t in Object.FindObjectsByType<TankController>(FindObjectsInactive.Include)) roots.Add(t.transform);
            // one of each model is enough
            var seen = new HashSet<string>(); var pick = new List<Transform>();
            foreach (var r in roots)
            {
                string key = r.GetComponent<TankController>() ? r.name : (r.GetComponent<TruckController>().displayName + "|" + MeshKey(r));
                if (seen.Add(key)) pick.Add(r);
                if (pick.Count >= 15) break;
            }
            var report = new StringBuilder($"Vehicle fronts - {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} - {System.DateTime.Now}\n");
            report.AppendLine("Red arrow = the vehicle's forward (+Z): W drives that way, the camera sits behind it.\n");
            int rows = Mathf.Max(1, (pick.Count + Cols - 1) / Cols);
            var sheet = new Texture2D(W * Cols, H * rows, TextureFormat.RGB24, false);
            var fill = new Color[sheet.width * sheet.height]; for (int i = 0; i < fill.Length; i++) fill[i] = Color.black; sheet.SetPixels(fill);

            var sunGo = new GameObject("_FrontsSun") { hideFlags = HideFlags.HideAndDontSave };
            var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.3f; sunGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            var camGo = new GameObject("_FrontsCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.74f, 0.88f);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f; cam.fieldOfView = 40f;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var red = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")) { color = Color.red };
            red.SetColor("_BaseColor", Color.red);
            try
            {
                for (int i = 0; i < pick.Count; i++)
                {
                    var v = pick[i];
                    bool wasActive = v.gameObject.activeSelf; v.gameObject.SetActive(true);
                    var b = Bounds(v);
                    float len = Mathf.Max(3f, b.size.z, b.size.x);
                    // the arrow, above the roof, from the back to the front
                    var arrow = new GameObject("_Arrow") { hideFlags = HideFlags.HideAndDontSave };
                    Vector3 top = b.center + Vector3.up * (b.extents.y + 0.6f);
                    Vector3 f = v.forward; f.y = 0f; f.Normalize();
                    var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube); shaft.transform.SetParent(arrow.transform);
                    shaft.transform.SetPositionAndRotation(top, Quaternion.LookRotation(f)); shaft.transform.localScale = new Vector3(0.15f, 0.15f, len * 0.7f);
                    var head = GameObject.CreatePrimitive(PrimitiveType.Cube); head.transform.SetParent(arrow.transform);
                    head.transform.SetPositionAndRotation(top + f * len * 0.38f, Quaternion.LookRotation(f) * Quaternion.Euler(0f, 45f, 0f)); head.transform.localScale = new Vector3(0.6f, 0.15f, 0.6f);
                    foreach (var r in arrow.GetComponentsInChildren<Renderer>()) r.sharedMaterial = red;
                    // from the right-hand side, a little above and a little ahead
                    Vector3 right = Vector3.Cross(Vector3.up, f);
                    cam.transform.position = b.center + right * len * 1.6f + f * len * 0.35f + Vector3.up * len * 0.45f;
                    cam.transform.LookAt(b.center + Vector3.up * 0.3f);
                    cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
                    int cx = (i % Cols) * W, cy = (rows - 1 - i / Cols) * H;
                    sheet.SetPixels(cx, cy, W, H, tex.GetPixels());
                    Object.DestroyImmediate(arrow);
                    v.gameObject.SetActive(wasActive);
                    report.AppendLine($"{i + 1}. {FullName(v)}");
                    report.AppendLine("   " + Detail(v));
                }
                sheet.Apply();
                string logs = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs");
                Directory.CreateDirectory(logs);
                File.WriteAllBytes(System.IO.Path.Combine(logs, $"vehicle-fronts-{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}.png"), sheet.EncodeToPNG());
                File.WriteAllText(System.IO.Path.Combine(logs, $"vehicle-fronts-{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}.txt"), report.ToString());
                Debug.Log($"Stan's Game: {pick.Count} vehicles photographed - Logs/vehicle-fronts.png (numbered left to right, top to bottom) and vehicle-fronts.txt");
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(sunGo); Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex); Object.DestroyImmediate(sheet); Object.DestroyImmediate(red);
            }
        }

        static string MeshKey(Transform t)
        {
            var mf = t.GetComponentInChildren<MeshFilter>(true);
            return mf && mf.sharedMesh ? mf.sharedMesh.name : "";
        }

        static string FullName(Transform t) { string p = t.name; while (t.parent) { t = t.parent; p = t.name + "/" + p; } return p; }

        static Bounds Bounds(Transform v)
        {
            var rs = v.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(v.position, Vector3.one * 3f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static string Detail(Transform v)
        {
            var sb = new StringBuilder();
            var tc = v.GetComponent<TruckController>();
            if (tc && tc.wheels != null && tc.wheels.Length > 0)
            {
                float zMin = float.MaxValue, zMax = float.MinValue;
                foreach (var w in tc.wheels) if (w) { float z = v.InverseTransformPoint(w.position).z; zMin = Mathf.Min(zMin, z); zMax = Mathf.Max(zMax, z); }
                float mid = (zMin + zMax) * 0.5f;
                var steer = new List<string>();
                foreach (var w in tc.wheels) if (w && v.InverseTransformPoint(w.position).z > mid) steer.Add(w.name);
                sb.Append($"steering wheels (the +Z pair): {string.Join(", ", steer)}; ");
                foreach (var w in tc.wheels)
                {
                    if (!w) continue;
                    var mf = w.GetComponentInChildren<MeshFilter>();
                    if (!mf || !mf.sharedMesh) continue;
                    Vector3 hub = mf.transform.TransformPoint(mf.sharedMesh.bounds.center);
                    float off = Vector3.Distance(hub, w.position);
                    if (off > 0.05f) sb.Append($"{w.name} pivot {off:0.00} m off its hub; ");
                }
            }
            var tank = v.GetComponent<TankController>();
            if (tank && tank.muzzle) sb.Append($"muzzle at local z {v.InverseTransformPoint(tank.muzzle.position).z:0.0} (should be +); ");
            var b = Bounds(v);
            Vector3 c = v.InverseTransformPoint(b.center);
            sb.Append($"model centre offset x {c.x:0.00} z {c.z:0.00}");
            return sb.ToString();
        }
    }
}
