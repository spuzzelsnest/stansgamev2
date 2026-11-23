using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Colours for the Opel Blitz lorries (all levels) and the window glass.
    /// The paint schemes (Models/Props/SME/Textures/OpelBlitzPaint, made by Models/Source~/opel_blitz_paint.py from the
    /// World at War texture): the 1944 Dunkelgelb with Olivgrün and Rotbraun camouflage, plain Dunkelgelb, an olive one
    /// and an older Panzergrau lorry that was never repainted. Each lorry built takes the next scheme.
    /// The glass is after Joan's glass-window download (a dark, smooth, almost mirror-like pane).
    /// </summary>
    public static partial class PegasusLevelBuilder
    {
        static readonly string[] TruckPaints = { "Camo", "Dunkelgelb", "Camo", "Olive" };   // Normandy 1944: mostly the three-tone camouflage (no Panzergrau)
        static int truckPaint;
        const string TruckPaintDir = Root + "/Models/Props/SME/Textures/OpelBlitzPaint";

        static void PaintTruck(GameObject body, string paint = null)
        {
            paint = paint ?? TruckPaints[truckPaint++ % TruckPaints.Length];
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TruckPaintDir}/OpelBlitz_Paint_{paint}.jpg");
            if (!tex) { Debug.LogWarning($"Stan's Game: OpelBlitz_Paint_{paint}.jpg not found - lorry left as imported"); return; }
            var m = Mat($"OpelBlitz_Paint_{paint}", Color.white, 0.22f, 0.1f);
            m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            EditorUtility.SetDirty(m);
            var glass = VehicleGlassMat();
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i] ? ms[i].name : "";
                    if (n.Contains("Glass")) ms[i] = glass;
                    else if (n.Contains("Body")) ms[i] = m;
                }
                r.sharedMaterials = ms;
            }
        }

        static Material GlassMat() => Mat("Glass_Pane", new Color(0.10f, 0.12f, 0.13f), 0.93f, 0.85f);

        /// <summary>A windscreen you can see through (who is at the wheel): URP Lit, transparent, a faint tint and a shine.</summary>
        static Material VehicleGlassMat()
        {
            var m = Mat("Glass_Vehicle", new Color(0.55f, 0.62f, 0.66f, 0.18f), 0.95f, 0.1f);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);   // both faces: the pane is seen from inside too
            m.SetColor("_BaseColor", new Color(0.55f, 0.62f, 0.66f, 0.18f));
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
