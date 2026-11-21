using System.IO;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Stan's Game > Debug > Map Shot (Ctrl+Shift+Alt+M): a top-down picture of the open level, lit as by day, to check
    /// the layout - _downloads/previews/map_whole.png (the whole map) and map_town.png (the centre, 360 m).
    /// Game +Z is up in the picture.
    /// </summary>
    public static class MapShot
    {
        [MenuItem("Stan's Game/Debug/Map Shot %#&m", priority = 220)]
        public static void Shoot()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_downloads", "previews");
            Directory.CreateDirectory(dir);
            bool fog = RenderSettings.fog; var amb = RenderSettings.ambientLight; var mode = RenderSettings.ambientMode;
            RenderSettings.fog = false; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
            var lightGo = new GameObject("MapShot sun"); var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional; l.intensity = 1.3f; l.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(55f, 150f, 0f);
            var camGo = new GameObject("MapShot camera"); var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.nearClipPlane = 1f; cam.farClipPlane = 600f;
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            try
            {
                foreach (var (name, centre, half) in new[] { ("map_whole", Vector3.zero, 512f), ("map_town", new Vector3(-20f, 0f, 0f), 180f) })
                {
                    camGo.transform.position = centre + Vector3.up * 300f;
                    cam.orthographicSize = half;
                    var rt = new RenderTexture(2048, 2048, 24); cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(2048, 2048, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 2048, 2048), 0, 0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                    RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                }
                Debug.Log("Stan's Game: map shots written to " + dir);
            }
            finally
            {
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
                RenderSettings.fog = fog; RenderSettings.ambientLight = amb; RenderSettings.ambientMode = mode;
            }
        }
    }
}
