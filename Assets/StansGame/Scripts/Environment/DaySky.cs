using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// The weather on a daytime level that has the plain procedural sky (the Proving Ground): the night levels' NightSky
    /// does this for the 1944 sky. As the Weather clouds over, the blue goes grey and dim, the sun weakens and its
    /// shadows soften, the ambient light turns flat and the far distance closes in; in the rain everything is grey.
    /// Works on a copy of the sky material, and puts the scene back as it was built when the game stops.
    /// </summary>
    [DisallowMultipleComponent]
    public class DaySky : MonoBehaviour
    {
        public Light sun;

        Material built, own;
        Color tint, aSky, aEq, aGround, fogColour; float exposure, atmosphere, sunSize, sunI, shadow, fogStart, fogEnd;
        bool have;
        static readonly Color Overcast = new Color(0.56f, 0.58f, 0.61f);

        void OnEnable()
        {
            if (!sun) sun = RenderSettings.sun;
            built = RenderSettings.skybox;
            if (built && built.HasProperty("_SkyTint"))
            {
                own = new Material(built) { name = built.name + " (weather)" };
                RenderSettings.skybox = own;
                tint = own.GetColor("_SkyTint"); exposure = own.GetFloat("_Exposure");
                atmosphere = own.GetFloat("_AtmosphereThickness"); sunSize = own.GetFloat("_SunSize");
            }
            if (sun) { sunI = sun.intensity; shadow = sun.shadowStrength; }
            aSky = RenderSettings.ambientSkyColor; aEq = RenderSettings.ambientEquatorColor; aGround = RenderSettings.ambientGroundColor;
            fogColour = RenderSettings.fogColor; fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance;
            have = true;
        }

        void OnDisable()
        {
            if (!have) return;
            if (built) RenderSettings.skybox = built;
            if (sun) { sun.intensity = sunI; sun.shadowStrength = shadow; }
            RenderSettings.ambientSkyColor = aSky; RenderSettings.ambientEquatorColor = aEq; RenderSettings.ambientGroundColor = aGround;
            RenderSettings.fogColor = fogColour; RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
            have = false;
        }

        void Update()
        {
            if (!have || !Weather.Instance) return;
            float over = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.95f, Mathf.Max(0f, Weather.Coverage)));
            float wet = Mathf.Clamp01(Weather.Rain + Weather.Snow);
            if (own)
            {
                own.SetColor("_SkyTint", Color.Lerp(tint, Overcast, over));
                own.SetFloat("_Exposure", exposure * Mathf.Lerp(1f, 0.6f, over) * Mathf.Lerp(1f, 0.8f, wet));
                own.SetFloat("_AtmosphereThickness", Mathf.Lerp(atmosphere, 2.6f, over));
                own.SetFloat("_SunSize", Mathf.Lerp(sunSize, 0f, over));
            }
            if (sun)
            {
                sun.intensity = sunI * Weather.LightFactor;
                sun.shadowStrength = shadow * Mathf.Lerp(1f, 0.25f, over);
            }
            float a = Weather.AmbientFactor;
            RenderSettings.ambientSkyColor = Color.Lerp(aSky, Overcast * 0.9f, over) * a;
            RenderSettings.ambientEquatorColor = Color.Lerp(aEq, Overcast * 0.75f, over) * a;
            RenderSettings.ambientGroundColor = aGround * a;
            float f = Mathf.Max(0.5f, Weather.FogFactor);
            RenderSettings.fogStartDistance = fogStart / f;
            RenderSettings.fogEndDistance = fogEnd / f;
            RenderSettings.fogColor = Color.Lerp(fogColour, Overcast * 0.95f, Mathf.Max(over, wet));
        }
    }
}
