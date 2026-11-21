using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Materials made while the game runs (tracers, flak rounds, parachutes). Shader.Find only finds shaders the
    /// build includes, and a material made from a missing shader throws - so these come from template materials in
    /// Resources/RuntimeMaterials (which pulls their shaders into the build), with a chain of fallbacks, and never
    /// throw: at worst you get null and the effect is skipped.
    /// </summary>
    public static class RuntimeMaterials
    {
        static readonly string[] UnlitShaders = { "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit", "Sprites/Default", "Unlit/Color" };
        static readonly string[] LitShaders = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard", "Sprites/Default" };

        /// <summary>A new unlit (glowing) material in this colour, or null.</summary>
        public static Material Unlit(Color c) => Make("RuntimeMaterials/Unlit", UnlitShaders, c);

        /// <summary>A new lit material in this colour, or null.</summary>
        public static Material Lit(Color c) => Make("RuntimeMaterials/Lit", LitShaders, c);

        static Material Make(string resource, string[] shaders, Color c)
        {
            Material m = null;
            try
            {
                var template = Resources.Load<Material>(resource);
                if (template) m = new Material(template);
                else
                    foreach (var name in shaders)
                    {
                        var s = Shader.Find(name);
                        if (s && s.isSupported) { m = new Material(s); break; }
                    }
                if (!m) { Debug.LogWarning($"Stan's Game: no shader for {resource}"); return null; }
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            }
            catch (System.Exception e) { Debug.LogWarning("Stan's Game: runtime material failed - " + e.Message); }
            return m;
        }
    }
}
