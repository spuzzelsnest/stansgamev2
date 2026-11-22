using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Stan's Game → Debug → List Playing Sounds (in Play mode): every AudioSource playing right now, loudest first,
    /// with its clip, whether it loops, how loud it is where the listener stands, and the object it's on. For tracking
    /// down a sound that won't stop.
    /// </summary>
    public static class SoundDebug
    {
        [MenuItem("Stan's Game/Debug/List Playing Sounds")]
        static void List()
        {
            if (!Application.isPlaying) { Debug.Log("Stan's Game: press Play first - this lists the sounds playing in the game."); return; }
            var listener = Object.FindAnyObjectByType<AudioListener>();
            Vector3 ear = listener ? listener.transform.position : Vector3.zero;
            var rows = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                .Where(a => a.isPlaying)
                .Select(a =>
                {
                    float d = Vector3.Distance(a.transform.position, ear);
                    float att = a.spatialBlend < 0.01f ? 1f : Mathf.Lerp(1f, d <= a.minDistance ? 1f : d >= a.maxDistance ? 0f : a.minDistance / d, a.spatialBlend);
                    return (a, d, heard: a.volume * att * (a.ignoreListenerVolume ? 1f : AudioListener.volume));
                })
                .OrderByDescending(r => r.heard).ToList();
            var sb = new StringBuilder($"Stan's Game: {rows.Count} sounds playing (listener volume {AudioListener.volume:0.00}):\n");
            foreach (var (a, d, heard) in rows)
                sb.AppendLine($"  {heard:0.00} heard  ·  {(a.clip ? a.clip.name : "(no clip)")}{(a.loop ? "  LOOP" : "")}  ·  vol {a.volume:0.00} pitch {a.pitch:0.00}  ·  {(a.spatialBlend < 0.01f ? "2D (everywhere)" : $"{d:0} m away")}  ·  {Path(a.transform)}");
            Debug.Log(sb.ToString());
        }

        [MenuItem("Stan's Game/Debug/List Pink Materials")]
        static void Pink()
        {
            var sb = new StringBuilder("Stan's Game: materials that draw pink (shader missing or not URP):\n");
            var seen = new System.Collections.Generic.HashSet<Material>();
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || seen.Contains(m)) continue;
                    seen.Add(m);
                    bool bad = !m.shader || !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader" || !(m.shader.name.StartsWith("Universal Render Pipeline") || m.shader.name.StartsWith("Shader Graphs") || m.shader.name.StartsWith("StansGame") || m.shader.name.StartsWith("Sprites") || m.shader.name.StartsWith("Skybox") || m.shader.name.StartsWith("UI/"));
                    if (!bad) continue;
                    n++;
                    sb.AppendLine($"  {m.name}  ·  shader {(m.shader ? m.shader.name : "none")}  ·  {AssetDatabase.GetAssetPath(m)}  ·  on {Path(r.transform)}");
                }
            Debug.Log(n == 0 ? "Stan's Game: no pink materials in the open scene" : sb.ToString());
        }

        static string Path(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 3 && t.parent; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
