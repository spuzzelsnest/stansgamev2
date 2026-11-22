using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>
    /// F9, anywhere in the game (the built demo too): a list on screen, for 12 seconds, of every sound playing right
    /// now - loudest first, what it is, whether it loops, how far away and on what. For catching a sound that won't
    /// stop: press F9 while you hear it and take a printscreen. Also written to the log.
    /// </summary>
    public class SoundSpy : MonoBehaviour
    {
        static SoundSpy instance;
        readonly List<string> lines = new List<string>();
        float showUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance) return;
            var go = new GameObject("Sound spy (F9)");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SoundSpy>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.f9Key.wasPressedThisFrame) return;
            lines.Clear();
            var listener = FindAnyObjectByType<AudioListener>();
            Vector3 ear = listener ? listener.transform.position : Vector3.zero;
            var playing = FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(a => a.isPlaying).Select(a =>
            {
                float d = Vector3.Distance(a.transform.position, ear);
                float att = a.spatialBlend < 0.01f ? 1f : Mathf.Lerp(1f, d <= a.minDistance ? 1f : d >= a.maxDistance ? 0f : a.minDistance / d, a.spatialBlend);
                return (a, d, heard: a.volume * att * (a.ignoreListenerVolume ? 1f : AudioListener.volume));
            }).OrderByDescending(r => r.heard).ToList();
            lines.Add($"{playing.Count} sounds playing   (game volume {AudioListener.volume:0.00})");
            foreach (var (a, d, heard) in playing)
            {
                string where = a.spatialBlend < 0.01f ? "everywhere" : $"{d:0} m";
                string what = a.clip ? a.clip.name : "one-shot";
                lines.Add($"{heard:0.00}   {what}{(a.loop ? "  (loop)" : "")}   {where}   {a.transform.root.name} / {a.gameObject.name}");
            }
            Debug.Log("Stan's Game sounds:\n" + string.Join("\n", lines));
            showUntil = Time.unscaledTime + 12f;
        }

        void OnGUI()
        {
            if (Time.unscaledTime > showUntil || lines.Count == 0) return;
            GUI.depth = -100;
            float h = 20f * Mathf.Min(lines.Count, 30) + 16f;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(10, 60, 760, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            for (int i = 0; i < lines.Count && i < 30; i++) GUI.Label(new Rect(18, 66 + i * 20, 750, 20), lines[i], st);
        }
    }
}
