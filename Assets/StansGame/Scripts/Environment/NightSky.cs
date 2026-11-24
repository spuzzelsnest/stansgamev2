using UnityEngine;
using UnityEngine.Rendering;

namespace StansGame
{
    /// <summary>
    /// Drives the 1944 sky (StansGame/NightSky skybox) by the mission clock. The sun and the moon stand where they
    /// really stood over the level at that minute of 6 June 1944 (SkyEphemeris1944, from PyEphem): the nearly full moon
    /// sinks into the south-west and sets just before six, the sky pales from about four, the sun comes up in the
    /// north-east at 06:01 and is high in the south by the time the commandos arrive at one. The stars turn round the
    /// pole as the hours go by. Moonlight, sunlight, the ambient light and the fog follow; and the moonlight still dims
    /// while a cloud passes over the moon - the paratroopers' "moon breaking through the clouds".
    /// When the clock jumps (cut scenes, "two hours later") the sky goes over to the new hour in a few seconds.
    /// </summary>
    [ExecuteAlways]
    public class NightSky : MonoBehaviour
    {
        public Light moon;
        public Material sky;
        [Tooltip("The same cloud field as the skybox uses (Read/Write enabled), to know when the moon is behind a cloud.")]
        public Texture2D clouds;
        [Tooltip("How much moonlight gets through a thick cloud.")]
        [Range(0f, 1f)] public float throughCloud = 0.35f;
        [Tooltip("The sunlight (made when the game starts if empty).")]
        public Light sun;
        [Tooltip("Minutes after midnight (BDST) to show while not playing, and before a mission clock is running. -1: the hour the star map was made for.")]
        public float editorMinutes = -1f;

        float fullIntensity = -1f, shade = 1f;
        float shownMinutes = -1f;
        Color nightAmbient, nightFog; float nightFogDensity; bool haveNight;

        bool Sme => SainteMereMission.Instance || gameObject.scene.name.Contains("Sainte");
        float[] Table => Sme ? SkyEphemeris1944.SainteMere : SkyEphemeris1944.Pegasus;
        float Latitude => Sme ? 49.41f : 49.24f;
        /// <summary>The hour the level's star map (Textures/Sky/NightSky_*.png) was drawn for, minutes after midnight.</summary>
        float StarMapMinutes => Sme ? 90f : 16f;
        /// <summary>The true bearing of the game's +Z: the levels are laid out along their streets and canal, not
        /// north (Pegasus: up the canal, 28 deg; Sainte-Mere-Eglise: up the rue General de Gaulle, 340 deg).</summary>
        float GridBearing => Sme ? 340f : 28f;

        void OnEnable()
        {
            if (moon && Application.isPlaying) fullIntensity = moon.intensity;
            if (sky && RenderSettings.skybox != sky) RenderSettings.skybox = sky;
            if (!haveNight)
            {
                nightAmbient = RenderSettings.ambientLight; nightFog = RenderSettings.fogColor; nightFogDensity = RenderSettings.fogDensity;
                haveNight = true;
            }
            shownMinutes = -1f;
        }

        void OnDisable()
        {
            // leave the scene as it was built (night) when the game stops
            if (!haveNight) return;
            RenderSettings.ambientLight = nightAmbient; RenderSettings.fogColor = nightFog; RenderSettings.fogDensity = nightFogDensity;
            if (moon) RenderSettings.sun = moon;
        }

        /// <summary>Minutes after midnight on the mission clock, or -1 if no mission is running.</summary>
        static float ClockMinutes()
        {
            var sme = SainteMereMission.Instance;
            if (sme) return sme.startHour * 60 + sme.startMinute + sme.Minutes;
            var ben = BenouvilleMission.Instance;
            if (ben) return ben.startHour * 60 + ben.startMinute + ben.Minutes;
            var md = MissionDirector.Instance;
            if (md) return md.startHour * 60 + md.startMinute + md.Minutes;
            return -1f;
        }

        /// <summary>Sun and moon (azimuth, altitude in degrees) at a time, interpolated from the table.</summary>
        void Ephemeris(float minutes, out float sunAz, out float sunAlt, out float moonAz, out float moonAlt)
        {
            var t = Table;
            float f = Mathf.Clamp(minutes / SkyEphemeris1944.StepMinutes, 0f, SkyEphemeris1944.Steps - 1.001f);
            int i = (int)f; float k = f - i;
            int a = i * 4, b = (i + 1) * 4;
            sunAz = Mathf.LerpAngle(t[a], t[b], k); sunAlt = Mathf.Lerp(t[a + 1], t[b + 1], k);
            moonAz = Mathf.LerpAngle(t[a + 2], t[b + 2], k); moonAlt = Mathf.Lerp(t[a + 3], t[b + 3], k);
        }

        /// <summary>World direction towards a body at this true azimuth (from north towards east) and altitude.</summary>
        Vector3 Dir(float az, float alt)
        {
            float a = (az - GridBearing) * Mathf.Deg2Rad, h = alt * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * Mathf.Cos(h), Mathf.Sin(h), Mathf.Cos(a) * Mathf.Cos(h));
        }

        void Update()
        {
            if (!moon) return;
            float t = Application.isPlaying ? Time.time : 0f;
            Shader.SetGlobalFloat("_SkyTime", t);

            // ---- the hour
            float target = Application.isPlaying ? ClockMinutes() : -1f;
            if (target < 0f) target = editorMinutes >= 0f ? editorMinutes : StarMapMinutes;
            if (shownMinutes < 0f || !Application.isPlaying) shownMinutes = target;
            else
            {
                // follow the clock; a jump of hours is crossed in a few seconds rather than in one frame
                float gap = target - shownMinutes;
                shownMinutes = Mathf.Abs(gap) < 2f ? target : shownMinutes + gap * (1f - Mathf.Exp(-0.8f * Time.deltaTime));
            }
            Ephemeris(shownMinutes, out float sunAz, out float sunAlt, out float moonAz, out float moonAlt);
            Vector3 toSun = Dir(sunAz, sunAlt), toMoon = Dir(moonAz, moonAlt);

            // ---- the sky: where the moon and sun are, how light it is, and how far the stars have turned
            float day = Smooth(-6f, 8f, sunAlt);                                   // civil twilight into morning
            float twilight = Smooth(-15f, -4f, sunAlt) * (1f - Smooth(4f, 16f, sunAlt));
            Shader.SetGlobalVector("_MoonDir", toMoon);
            Shader.SetGlobalVector("_SunDir", toSun);
            Shader.SetGlobalFloat("_SkyDay", day);
            Shader.SetGlobalFloat("_SkyTwilight", twilight);
            // the stars wheel round the celestial pole (north, as high as the latitude), 15 degrees an hour
            float lat = Latitude * Mathf.Deg2Rad;
            Vector3 pole = new Vector3(0f, Mathf.Sin(lat), Mathf.Cos(lat));
            float turned = (shownMinutes - StarMapMinutes) / 60f * 15.041f;
            // (the star map is drawn with north at +Z: turn the game's directions to true ones first)
            Shader.SetGlobalMatrix("_SkyRot", Matrix4x4.Rotate(Quaternion.AngleAxis(-turned, pole) * Quaternion.Euler(0f, GridBearing, 0f)));

            // ---- the lights
            Quaternion moonRot = Quaternion.LookRotation(-toMoon);
            if (Quaternion.Angle(moon.transform.rotation, moonRot) > 0.01f) moon.transform.rotation = moonRot;   // (not every frame in the editor: that would mark the scene changed)
            if (!Application.isPlaying) return;
            if (fullIntensity < 0f) fullIntensity = moon.intensity;
            // the weather sets the cloud cover (on a copy of the sky material, so the asset is not changed)
            if (Weather.Coverage >= 0f && sky)
            {
                if (!skyCopied) { sky = new Material(sky); RenderSettings.skybox = sky; skyCopied = true; }
                sky.SetFloat("_Coverage", Weather.Coverage);
            }
            if (!sun) MakeSun();

            // is there cloud in front of the moon? (the same sum as the shader)
            float cover = 0f;
            if (sky && clouds && toMoon.y > 0.01f)
            {
                float scale = sky.GetFloat("_CloudScale"), coverage = sky.GetFloat("_Coverage");
                Vector4 wind = sky.GetVector("_Wind");
                Vector2 uv = new Vector2(toMoon.x, toMoon.z) / toMoon.y * scale + new Vector2(wind.x, wind.y) * t;
                float n = clouds.GetPixelBilinear(uv.x, uv.y).r;
                float lo = 1f - coverage;
                cover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lo, lo + 0.22f, n));
            }
            shade = Mathf.Lerp(shade, Mathf.Lerp(1f, throughCloud, cover), 1f - Mathf.Exp(-1.5f * Time.deltaTime));
            // the moon gives less light low down, none once set, and is lost in the dawn
            float moonUp = Smooth(-1f, 8f, moonAlt);
            moon.intensity = fullIntensity * shade * moonUp * (1f - day) * Weather.LightFactor;
            moon.enabled = moon.intensity > 0.002f;

            // the sun: red as it rises, white once it is up
            sun.transform.rotation = Quaternion.LookRotation(-toSun);
            sun.intensity = 1.25f * Smooth(-1f, 12f, sunAlt) * Weather.LightFactor;
            sun.color = Color.Lerp(new Color(1f, 0.55f, 0.32f), new Color(1f, 0.96f, 0.9f), Smooth(2f, 25f, sunAlt));
            sun.enabled = sun.intensity > 0.002f;
            RenderSettings.sun = sun.intensity > moon.intensity ? sun : moon;

            // the ambient light and the mist go from moonlit night through the grey-pink dawn to day
            Color warm = new Color(0.10f, 0.05f, 0.05f);
            RenderSettings.ambientLight = Color.Lerp(nightAmbient, new Color(0.42f, 0.45f, 0.50f), day) + warm * (twilight * 0.6f);
            RenderSettings.fogColor = Color.Lerp(nightFog, new Color(0.58f, 0.62f, 0.68f), day) + warm * twilight;
            RenderSettings.fogDensity = Mathf.Lerp(nightFogDensity, 0.0035f, day);
            // the weather: grey light and thicker air under cloud, rain or fog
            RenderSettings.ambientLight *= Weather.AmbientFactor;
            RenderSettings.fogDensity *= Weather.FogFactor;
            if (Weather.Rain > 0.01f || Weather.FogFactor > 1.5f)
                RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, RenderSettings.ambientLight * 1.4f, 0.35f * Mathf.Clamp01(Weather.Rain + (Weather.FogFactor - 1.5f)));
        }
        bool skyCopied;

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        void MakeSun()
        {
            var go = new GameObject("Sun (6 June 1944)");
            go.transform.SetParent(transform.parent, false);
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 0f;
            sun.enabled = false;
        }

        /// <summary>The moonlight right now compared to a clear moon (1 = clear, lower = behind a cloud).</summary>
        public float MoonShade => shade;
    }
}
