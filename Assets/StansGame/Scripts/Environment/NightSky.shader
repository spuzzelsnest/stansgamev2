// The sky of 6 June 1944 (Stan's Game), from the moonlit night to the morning: a star map made for the place and
// the hour (Models/Source~/night_sky_1944.py), turned round the pole as the hours pass; the nearly full moon with its
// glow; the dawn coming up in the north-east, the sun, and the blue day sky; and a broken layer of stratocumulus
// drifting in on the north-west wind, lit by the moon at night and by the sun by day.
// NightSky.cs sets _MoonDir, _SunDir, _SkyDay, _SkyTwilight and _SkyRot from the mission clock.
Shader "StansGame/NightSky"
{
    Properties
    {
        [NoScaleOffset] _Panorama ("Sky (RGB) and stars (A), u = azimuth from north, v = altitude", 2D) = "black" {}
        [NoScaleOffset] _MoonTex ("Moon", 2D) = "white" {}
        [NoScaleOffset] _CloudTex ("Cloud field (tileable)", 2D) = "gray" {}
        _MoonSize ("Moon radius (degrees)", Float) = 0.42
        _MoonColor ("Moon", Color) = (1.0, 0.97, 0.9, 1)
        _MoonBright ("Moon brightness", Float) = 2.2
        _GlowColor ("Moon glow", Color) = (0.20, 0.25, 0.36, 1)
        _StarBright ("Star brightness", Float) = 1.3
        _Coverage ("Cloud coverage (0 clear .. 1 overcast)", Range(0, 1)) = 0.5
        _CloudScale ("Cloud size", Float) = 0.4
        _Wind ("Cloud drift (uv per second)", Vector) = (-0.0045, 0.0032, 0, 0)
        _CloudDark ("Cloud underside", Color) = (0.05, 0.06, 0.085, 1)
        _CloudLit ("Cloud lit by the moon", Color) = (0.20, 0.22, 0.27, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _Panorama, _MoonTex, _CloudTex;
            float _MoonSize, _MoonBright, _StarBright, _Coverage, _CloudScale;
            float4 _MoonColor, _GlowColor, _Wind, _CloudDark, _CloudLit;
            float4 _MoonDir;          // set by NightSky.cs (world space, towards the moon)
            float4 _SunDir;           // world space, towards the sun (below the horizon at night)
            float _SkyDay;            // 0 night .. 1 day
            float _SkyTwilight;       // the dawn glow
            float4x4 _SkyRot;         // turns a view direction back to where that star was when the map was made
            float _SkyTime;           // set by NightSky.cs (so the CPU can follow the same clouds)

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o;
            }

            // the cloud layer seen in direction d: a flat sheet overhead, thinning out towards the horizon
            float CloudCover(float3 d, out float density)
            {
                density = 0;
                if (d.y <= 0.01) return 0;
                float2 uv = d.xz / d.y * _CloudScale + _Wind.xy * _SkyTime;
                float n = tex2Dlod(_CloudTex, float4(uv, 0, 0)).r;
                float lo = 1.0 - _Coverage;
                density = smoothstep(lo, lo + 0.22, n);
                return density * smoothstep(0.02, 0.18, d.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 m = normalize(_MoonDir.xyz);
                float3 sn = normalize(_SunDir.xyz + float3(0, 1e-5, 0));
                float day = _SkyDay, tw = _SkyTwilight;
                // the star map, turned back to the hour it was made for
                float3 ds = mul((float3x3)_SkyRot, d);
                float az = atan2(ds.x, ds.z) / (2 * UNITY_PI);        // 0 north, 0.25 east
                float u = frac(az + 1.0);
                float v = asin(clamp(ds.y, -1, 1)) / UNITY_PI + 0.5;
                float4 p = tex2Dlod(_Panorama, float4(u, v, 0, 0));
                float cosM = dot(d, m), cosS = dot(d, sn);
                float ang = degrees(acos(clamp(cosM, -1, 1)));
                // stars wash out in the moon's glare, and go out as the sky pales (nautical twilight)
                float starsOut = 1.0 - smoothstep(-0.26, -0.10, sn.y);
                float stars = p.a * _StarBright * smoothstep(4.0, 30.0, ang) * starsOut;
                float3 night = p.rgb * (1.0 - smoothstep(-0.2, 0.0, sn.y) * 0.5);
                // the day sky: pale at the horizon, deep blue overhead, brighter round the sun
                float h = saturate(d.y);
                float3 dayCol = lerp(float3(0.66, 0.72, 0.80), float3(0.24, 0.43, 0.76), sqrt(h));
                dayCol += float3(1.0, 0.92, 0.75) * (pow(saturate(cosS), 8) * 0.25 + pow(saturate(cosS), 120) * 0.6);
                // the dawn: a warm band on the horizon under the sun, cold blue-grey on the far side
                float2 hd = normalize(d.xz + 1e-5), hs = normalize(sn.xz + 1e-5);
                float towards = dot(hd, hs) * 0.5 + 0.5;
                float band = exp(-max(d.y, 0.0) / 0.14);
                float3 dawn = lerp(float3(0.10, 0.11, 0.20), float3(0.95, 0.50, 0.24), towards * towards) * band
                            + float3(0.10, 0.12, 0.22) * (1.0 - band) * 0.5;
                float3 col = lerp(night, dayCol, day) + dawn * tw + stars;
                // the moon's aureole (fainter by day)
                col += _GlowColor.rgb * (0.55 * exp(-ang / 18.0) + 0.6 * exp(-ang / 2.2)) * (1.0 - day * 0.85);
                // the moon itself: still there in the morning, pale, until it sets
                if (ang < _MoonSize * 1.05 && m.y > -0.02)
                {
                    float3 up = abs(m.y) > 0.99 ? float3(0, 0, 1) : float3(0, 1, 0);
                    float3 right = normalize(cross(up, m)); float3 mup = cross(m, right);
                    float2 muv = float2(dot(d, right), dot(d, mup)) / radians(_MoonSize) * 0.5 + 0.5;
                    float4 mt = tex2Dlod(_MoonTex, float4(muv, 0, 0));
                    float3 disc = mt.rgb * _MoonColor.rgb * lerp(_MoonBright, 1.1, day);
                    col = lerp(col, max(col, disc), mt.a * lerp(1.0, 0.55, day));
                }
                // the sun's disc
                if (sn.y > -0.03)
                {
                    float3 sunCol = lerp(float3(1.0, 0.55, 0.3), float3(1.0, 0.97, 0.9), smoothstep(0.03, 0.4, sn.y));
                    col += sunCol * (smoothstep(0.99985, 0.99992, cosS) * 6.0 + pow(saturate(cosS), 600) * 1.5);
                }
                // the clouds in front: moonlit at night (silver edges near the moon), white and grey by day, pink at dawn
                float dens;
                float c = CloudCover(d, dens);
                float silver = pow(saturate(cosM), 6) * (1.0 - dens) * 1.6 + pow(saturate(cosM), 40) * 0.8;
                float3 cloudNight = lerp(_CloudLit.rgb, _CloudDark.rgb, smoothstep(0.3, 1.0, dens) * 0.55) + _GlowColor.rgb * silver;
                float3 cloudDay = lerp(float3(0.86, 0.87, 0.89), float3(0.50, 0.53, 0.58), smoothstep(0.3, 1.0, dens) * 0.75)
                                + float3(1.0, 0.95, 0.85) * pow(saturate(cosS), 6) * (1.0 - dens) * 0.5;
                float3 cloudDawn = lerp(float3(0.16, 0.15, 0.22), float3(0.95, 0.55, 0.42), towards * towards) * (1.0 - smoothstep(0.4, 1.0, dens) * 0.5);
                float3 cloud = lerp(cloudNight, cloudDay, day) + cloudDawn * tw * 0.8;
                col = lerp(col, cloud, c * 0.97);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
