using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Bénouville wakes up: once the shooting starts, lamps come on behind the windows one after another (the blackout
    /// curtains pulled aside to see what is going on), each with a little warm light spilling onto the street.
    /// The order and the delays come from a fixed seed, so in multiplayer every machine lights the same windows at
    /// the same moments after the alarm (which the host shares).
    /// </summary>
    public class WindowLights : MonoBehaviour
    {
        public Renderer[] windows;
        public Light[] spill;          // one per window (may be null)
        public Material lit;
        public float firstAfter = 4f, lastAfter = 75f;
        public int seed = 6644;
        [Tooltip("Share of the windows already lit when the level starts (the town is up: the fire, the bell); the rest come on after the alarm.")]
        [Range(0f, 1f)] public float litAtStart = 0f;

        float[] at;
        bool started, opened;
        bool[] done;

        void Awake()
        {
            if (spill != null) foreach (var l in spill) if (l) l.enabled = false;
        }

        void Update()
        {
            if (windows == null || windows.Length == 0) { enabled = false; return; }
            if (!opened)
            {
                // the same order on every machine: the first share lit already, the others waiting for the alarm
                opened = true;
                var rng = new System.Random(seed);
                at = new float[windows.Length]; done = new bool[windows.Length];
                for (int i = 0; i < at.Length; i++) at[i] = firstAfter + (float)rng.NextDouble() * (lastAfter - firstAfter);
                int early = Mathf.RoundToInt(litAtStart * windows.Length);
                for (int i = 0; i < windows.Length; i++)
                    if ((i * 7919) % windows.Length < early) LightUp(i);
            }
            if (!started)
            {
                if (!SoldierAI.Alarm) return;
                started = true;
                for (int i = 0; i < at.Length; i++) at[i] += Time.time;
            }
            bool all = true;
            for (int i = 0; i < windows.Length; i++)
            {
                if (done[i]) continue;
                all = false;
                if (Time.time < at[i]) continue;
                LightUp(i);
            }
            if (all) enabled = false;
        }

        void LightUp(int i)
        {
            done[i] = true;
            if (windows[i]) windows[i].sharedMaterial = lit;
            if (spill != null && i < spill.Length && spill[i]) spill[i].enabled = true;
        }
    }
}
