using System.Reflection;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// The game's tracers (<c>StansGame.Tracer.Spawn</c>) make their material from
    /// "Universal Render Pipeline/Particles/Unlit", which is stripped from the build. <c>new Material(null)</c>
    /// throws (the <c>ArgumentNullException</c> in every log), and the half-made tracer is left behind as a
    /// line without a material, drawn magenta at the world origin (the middle of the bridge). This hands the
    /// game a <c>Sprites/Default</c> material before the first shot, so tracers draw as warm streaks that the
    /// bloom catches, and the exception and the leftover lines are gone. With the source: include the
    /// particle shader in the build (or reference it from a material) instead.
    /// </summary>
    public static class TracerFix
    {
        public static bool Applied { get; private set; }

        public static void Apply()
        {
            if (Applied) return;
            // The special effects' Tracers class fixes the same field with a hidden material and draws its own tracers.
            if (typeof(TracerFix).Assembly.GetType("StansGame.Additions.Tracers") != null) return;
            FieldInfo f = typeof(Tracer).GetField("sharedMat", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null || f.GetValue(null) as Material != null) return;
            if (Shader.Find("Universal Render Pipeline/Particles/Unlit") != null) return;   // the game's own works
            Shader s = Shader.Find("Sprites/Default");
            if (s == null) return;
            Material m = new Material(s);
            m.name = "Tracer (Stan's Game additions)";
            m.color = new Color(2.6f, 1.75f, 0.85f, 1f);   // over 1, so the bloom catches it
            m.renderQueue = 3000;
            f.SetValue(null, m);
            Applied = true;
            Debug.Log("Stan's Game: tracer material set (the game's particle shader is stripped from the build)");
        }
    }
}
