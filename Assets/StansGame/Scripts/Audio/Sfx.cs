using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// All the game's sound. Recorded sounds come from Stan's Game v1 (Resources/Audio: footsteps, reload, impacts,
    /// grunts, and the Detonator explosions); everything else is synthesised here at start-up, so there are no
    /// licences to worry about: each weapon's report (Sten, Bren, Lee-Enfield, Kar 98k, MP 40, MG 42, PIAT, tank guns),
    /// tank engines, the wind over the glider, the crash of the landing, the night (crickets and wind) and a pipe march
    /// on the bagpipes for Lord Lovat's commandos.
    /// </summary>
    public static class Sfx
    {
        const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static AudioClip[] footsteps, grunts, explosionsNear, explosionsFar;
        static AudioClip reload, impact, bodyHit;
        static bool loaded;
        static System.Random rng = new System.Random(1944);

        // ------------------------------------------------------------------ start-up

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            Load();
            var cam = Camera.main;
            if (cam && !Object.FindAnyObjectByType<AudioListener>()) cam.gameObject.AddComponent<AudioListener>();
            // the night: crickets in the grass and the wind over the canal
            // (only near the ground: AmbienceMixer fades it out with height and brings in the rush of air instead)
            var amb = new GameObject("Night ambience").AddComponent<AudioSource>();
            amb.clip = Ambience(); amb.loop = true; amb.volume = 0f; amb.spatialBlend = 0f; amb.Play();
            amb.gameObject.AddComponent<AmbienceMixer>().ground = amb;
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            footsteps = LoadAll("human_footstep_01", "human_footstep_02", "human_footstep_03", "human_footstep_04");
            grunts = LoadAll("hit01", "hit02", "hit03", "hit04", "hit05", "hit06", "hit07", "hit08", "hit09", "hit10", "hit11", "hit12");
            explosionsNear = LoadAll("NearExplosionA", "NearExplosionB", "explosion_near_c");
            explosionsFar = LoadAll("FarExplosionA", "FarExplosionB", "explosion_far_c");
            reload = Resources.Load<AudioClip>("Audio/human_reload");
            impact = Resources.Load<AudioClip>("Audio/env_hit");
            bodyHit = Resources.Load<AudioClip>("Audio/human_hit");
        }

        static AudioClip[] LoadAll(params string[] names)
        {
            var list = new List<AudioClip>();
            foreach (var n in names) { var c = Resources.Load<AudioClip>("Audio/" + n); if (c) list.Add(c); }
            return list.ToArray();
        }

        static AudioClip Pick(AudioClip[] a) => a != null && a.Length > 0 ? a[rng.Next(a.Length)] : null;

        // ------------------------------------------------------------------ playing

        /// <summary>A one-off 3D sound at a point (a pooled, self-destroying source).</summary>
        public static void At(AudioClip clip, Vector3 pos, float volume = 1f, float pitch = 1f, float maxDistance = 400f)
        {
            if (!clip) return;
            var go = new GameObject("Sfx " + clip.name);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip; src.volume = volume; src.pitch = pitch;
            src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.minDistance = 4f; src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.2f);
        }

        public static void Shot(string weapon, Vector3 pos) { Load(); At(GunClip(weapon), pos, 1f, 0.94f + (float)rng.NextDouble() * 0.12f, 900f); }

        public static void Explosion(Vector3 pos, bool big)
        {
            Load();
            var near = Pick(explosionsNear);
            At(near ? near : Synth("explosion", MakeExplosion), pos, big ? 1f : 0.8f, big ? 0.85f : 1.1f, 1500f);
            var far = Pick(explosionsFar);
            if (far && big) At(far, pos, 0.6f, 0.9f, 2500f);
        }

        public static void Footstep(Vector3 pos, float volume = 0.35f)
        {
            Load();
            // footsteps by surface (level template 3f): grass on the terrain, wet steps on hard ground after rain,
            // the old recordings (boots on hard ground) everywhere else
            AudioClip clip = null; float gain = 0.22f;
            if (Physics.Raycast(pos + Vector3.up * 0.4f, Vector3.down, out var hit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                bool soft = hit.collider is TerrainCollider;
                if (soft) { clip = PickRecorded("footstep_grass_", 2); gain = 0.5f; }
                else if (Weather.Wetness > 0.3f) { clip = PickRecorded("footstep_wet_", 4); gain = 0.3f; }
            }
            if (!clip) clip = Pick(footsteps);
            At(clip, pos, volume * gain, 0.9f + (float)rng.NextDouble() * 0.2f, 30f);   // the old recordings are normalised (+14 dB): kept as soft as before
        }
        static AudioClip PickRecorded(string prefix, int n) => Recorded(prefix + (1 + rng.Next(n)));
        public static void Reload(Vector3 pos) { Load(); At(reload, pos, 0.6f, 1f, 20f); }
        public static void Impact(Vector3 pos, bool person)
        {
            Load();
            float pitch = 0.9f + (float)rng.NextDouble() * 0.2f;
            if (person) { At(bodyHit ? bodyHit : impact, pos, 0.5f, pitch, 40f); return; }
            // what the round struck: earth thuds, anything hard cracks (Joan's recordings; the old one if missing)
            bool earth = false;
            foreach (var c in Physics.OverlapSphere(pos, 0.15f, ~0, QueryTriggerInteraction.Ignore)) if (c is TerrainCollider) { earth = true; break; }
            var rec = Recorded(earth ? "impact_ground" : "impact_bullet");
            At(rec ? rec : impact, pos, earth ? 0.45f : 0.4f, pitch, 45f);
        }
        public static void Hurt(Vector3 pos) { Load(); At(Pick(grunts), pos, 0.7f, 1f, 25f); }
        /// <summary>A vehicle brewing up: the big blast, close and far.</summary>
        public static void VehicleExplosion(Vector3 pos)
        {
            Load();
            var big = Recorded("explosion_big");
            At(big ? big : Pick(explosionsNear), pos, 1f, 0.9f + (float)rng.NextDouble() * 0.1f, 1800f);
            var far = Pick(explosionsFar);
            if (far) At(far, pos, 0.5f, 0.85f, 3000f);
        }
        public static void Crash(Vector3 pos) => At(Synth("crash", MakeCrash), pos, 1f, 1f, 800f);

        // ------------------------------------------------------------------ clips for components

        public static AudioClip GunClip(string weapon)
        {
            string w = (weapon ?? "").ToLowerInvariant();
            // real recordings (Joan's downloads, cut by Claude) where we have them
            AudioClip rec = null;
            if (w.Contains("garand")) rec = Recorded("garand_shot");
            else if (w.Contains("carbine")) rec = Recorded("carbine_shot");
            else if (w.Contains("kar")) rec = Recorded("kar98_shot");                       // the shot and the bolt worked
            else if (w.Contains("lee") || w.Contains("enfield")) rec = Recorded("enfield_shot");
            else if (w.Contains("thompson")) rec = Recorded("thompson_shot");
            else if (w.Contains("browning") || w.Contains("50cal") || w.Contains(".50") || w.Contains("m2hb")) rec = Recorded("m2_shot");   // the .50 M2HB
            else if (w.Contains("mg42") || w.Contains("mg 42") || w.Contains("mg")) rec = Recorded("mg42_shot");   // one round of the "buzz saw"
            else if (w.Contains("bren")) rec = Recorded("bren_shot");
            if (rec) return rec;
            if (w.Contains("mg")) return Synth("mg42", () => MakeShot(0.06f, 2600f, 0.35f, 120f));
            if (w.Contains("bren")) return Synth("bren", () => MakeShot(0.16f, 1800f, 0.55f, 95f));
            if (w.Contains("lee") || w.Contains("enfield") || w.Contains("kar") || w.Contains("rifle") || w.Contains("garand") || w.Contains("carbine"))
                return Synth("rifle", () => MakeShot(0.38f, 2400f, 0.9f, 80f));
            if (w.Contains("piat") || w.Contains("bazooka")) return Synth("piat", MakePiat);
            if (w.Contains("tank") || w.Contains("kwk") || w.Contains("pak") || w.Contains("stuk") || w.Contains("pounder"))
            { var cannon = Recorded("cannon_shot"); if (cannon) return cannon; }
            if (w.Contains("tank") || w.Contains("kwk") || w.Contains("pak") || w.Contains("stuk")) return Synth("tankgun", () => MakeShot(1.3f, 900f, 1f, 45f));
            return Synth("smg", () => MakeShot(0.11f, 1500f, 0.5f, 110f));   // Sten, MP 40
        }

        /// <summary>The weapon's own reload recording, or null (then the old generic one).</summary>
        public static AudioClip ReloadClip(string weapon)
        {
            string w = (weapon ?? "").ToLowerInvariant();
            if (w.Contains("pak") || w.Contains("pounder") || w.Contains("atgun") || w.Contains("anti-tank")) return Recorded("atgun_reload");
            if (w.Contains("mortar")) return Recorded("mortar_load");
            if (w.Contains("carbine")) return Recorded("carbine_reload");
            if (w.Contains("mg")) return Recorded("mg42_reload");
            if (w.Contains("bren")) return Recorded("bren_reload");
            if (w.Contains("kar")) return Recorded("kar98_reload");
            if (w.Contains("1911") || w.Contains("pistol") || w.Contains("colt")) return Recorded("pistol_reload");
            return null;
        }

        public static AudioClip Synthesised(string what) => what == "crash" ? Synth("crash", MakeCrash) : Synth("explosion", MakeExplosion);
        public static AudioClip Engine => Synth("engine", MakeEngine);
        public static AudioClip Wind => Synth("wind", MakeWind);
        public static AudioClip AircraftDrone => Synth("drone", MakeDrone);
        public static AudioClip Bagpipes => Synth("bagpipes", MakeBagpipes);

        static readonly System.Collections.Generic.Dictionary<string, AudioClip> recorded = new System.Collections.Generic.Dictionary<string, AudioClip>();
        /// <summary>A recording from Resources/Audio (Joan's downloads, cut by Claude), or null.</summary>
        public static AudioClip Recorded(string name)
        {
            if (!recorded.TryGetValue(name, out var c)) recorded[name] = c = Resources.Load<AudioClip>("Audio/" + name);
            return c;
        }
        /// <summary>An old lorry's engine (a seamless 6 s loop from "old truck passing by"); the synthesised engine if missing.</summary>
        public static AudioClip TruckEngine => Recorded("truck_engine") ?? Engine;
        /// <summary>A small air-cooled car engine (the Kübelwagen: a 1.5 s loop from "motor engine").</summary>
        public static AudioClip CarEngine => Recorded("car_engine") ?? TruckEngine;
        public static AudioClip TruckHorn => Recorded("truck_horn");
        /// <summary>A small vehicle coming up and passing close (12 s, one shot).</summary>
        public static AudioClip CarPassing => Recorded("car_passing");

        static AudioClip halifax; static bool halifaxLoaded;
        /// <summary>A real recording inside a Halifax heavy bomber in flight (a seamless 60 s loop); the synthesised drone if it is missing.</summary>
        public static AudioClip HalifaxDrone
        {
            get
            {
                if (!halifaxLoaded) { halifax = Resources.Load<AudioClip>("Audio/halifax_interior"); halifaxLoaded = true; }
                return halifax ? halifax : AircraftDrone;
            }
        }
        /// <summary>The hiss of a smoke grenade burning (a loop).</summary>
        public static AudioClip SmokeHiss => Synth("smokehiss", MakeHiss);
        /// <summary>The soft pop of a smoke grenade going off.</summary>
        public static void SmokePop(Vector3 pos)
        {
            // the recorded grenade (pop and the first rush of smoke, Resources/Audio/smoke_grenade), or a made-up pop
            if (!smokeLoaded) { smokeGrenade = Resources.Load<AudioClip>("Audio/smoke_grenade"); smokeLoaded = true; }
            if (smokeGrenade) At(smokeGrenade, pos, 1f, Random.Range(0.94f, 1.06f), 150f);
            else At(Synth("smokepop", MakePop), pos, 0.8f, 1f, 150f);
        }
        static AudioClip smokeGrenade; static bool smokeLoaded;

        /// <summary>Gives a weapon its report, reload sound and a 3D source if it has none.</summary>
        public static void SetupWeapon(Weapon w)
        {
            Load();
            if (!w.audioSource) w.audioSource = w.gameObject.AddComponent<AudioSource>();
            var s = w.audioSource;
            s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = 3f; s.maxDistance = 900f; s.playOnAwake = false; s.dopplerLevel = 0f;
            if (!w.fireClip) w.fireClip = GunClip(w.displayName);
            if (!w.reloadClip) { var own = ReloadClip(w.displayName); w.reloadClip = own ? own : reload; }
            if (!w.lastRoundClip && (w.displayName ?? "").ToLowerInvariant().Contains("garand")) w.lastRoundClip = Recorded("garand_ping");   // the en-bloc clip flying out
        }

        static AudioClip Synth(string key, System.Func<AudioClip> make)
        {
            if (clips.TryGetValue(key, out var c) && c) return c;
            c = make(); c.name = key; clips[key] = c; return c;
        }

        // ------------------------------------------------------------------ synthesis

        static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        static AudioClip Make(string name, float[] data, bool loop = false)
        {
            float peak = 0.0001f; foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < data.Length; i++) data[i] = data[i] / peak * 0.9f;
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A gun report: a sharp crack, a noisy blast that dies away and a low thump.</summary>
        static AudioClip MakeShot(float decay, float brightness, float tail, float thumpHz)
        {
            int n = (int)(Rate * (decay * 4f + tail));
            var d = new float[n];
            float lp = 0f, lp2 = 0f, a = Mathf.Clamp01(brightness / Rate * 6.28f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t / decay);
                float crack = t < 0.003f ? Noise() * 1.5f : 0f;
                lp += (Noise() - lp) * a;                   // bright blast
                lp2 += (Noise() - lp2) * a * 0.15f;         // the echo off the buildings
                float echo = lp2 * Mathf.Exp(-t / (tail * 0.5f + 0.01f)) * 0.5f;
                float thump = Mathf.Sin(6.2832f * thumpHz * t) * Mathf.Exp(-t / (decay * 0.8f)) * 0.8f;
                d[i] = crack + lp * env * 1.4f + echo + thump;
            }
            return Make("shot", d);
        }

        static AudioClip MakePiat()
        {
            int n = (int)(Rate * 0.9f); var d = new float[n]; float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.08f;
                float spring = Mathf.Sin(6.2832f * 60f * t) * Mathf.Exp(-t / 0.06f);      // the big spring slamming forward
                float whoosh = lp * Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-t / 0.35f);
                d[i] = spring * 1.2f + whoosh;
            }
            return Make("piat", d);
        }

        static AudioClip MakeExplosion()
        {
            int n = Rate * 3; var d = new float[n]; float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.05f; lp2 += (lp - lp2) * 0.05f;
                d[i] = (lp2 * 6f + Mathf.Sin(6.2832f * 38f * t) * 0.6f) * Mathf.Exp(-t / 0.7f) + (t < 0.01f ? Noise() : 0f);
            }
            return Make("explosion", d);
        }

        static AudioClip MakeCrash()
        {
            int n = Rate * 3; var d = new float[n]; float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.12f;
                // plywood splintering, wire twanging, the scrape of the skid
                float crunch = (Mathf.PerlinNoise(t * 30f, 0f) > 0.62f ? Noise() : 0f) * Mathf.Exp(-t / 0.8f);
                float scrape = lp * Mathf.Exp(-t / 1.4f) * 0.6f;
                float twang = Mathf.Sin(6.2832f * 210f * t) * Mathf.Exp(-(t - 0.4f) * (t - 0.4f) * 60f) * 0.4f;
                d[i] = crunch + scrape + twang;
            }
            return Make("crash", d);
        }

        /// <summary>Maybach engine idle, looping (whole cycles in 2 s so it loops cleanly).</summary>
        static AudioClip MakeEngine()
        {
            int n = Rate * 2; var d = new float[n]; float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.02f;
                float f = 31f;   // firing frequency
                float s = Mathf.Sin(6.2832f * f * t) + 0.6f * Mathf.Sin(6.2832f * f * 2f * t + 1f) + 0.35f * Mathf.Sin(6.2832f * f * 3f * t + 2f)
                          + 0.25f * Mathf.Sign(Mathf.Sin(6.2832f * f * 0.5f * t)) ;
                d[i] = s * 0.5f + lp * 2f;
            }
            return MakeLoop("engine", d);
        }

        static AudioClip MakeWind()
        {
            int n = Rate * 6; var d = new float[n]; float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.04f; lp2 += (lp - lp2) * 0.3f;
                float gust = 0.6f + 0.4f * Mathf.Sin(6.2832f * t / 6f) * Mathf.Sin(6.2832f * t / 2f);
                d[i] = lp2 * gust;
            }
            return MakeLoop("wind", d);
        }

        static AudioClip MakeDrone()
        {
            // four Bristol Hercules engines of the Halifax tugs, heard through the glider's plywood
            int n = Rate * 4; var d = new float[n]; float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.03f;
                float s = 0f;
                foreach (float f in new[] { 54.5f, 55.25f, 55.75f, 56.5f })   // slightly out of sync: the throb
                    s += Mathf.Sin(6.2832f * f * t) + 0.5f * Mathf.Sin(6.2832f * f * 2f * t);
                d[i] = s * 0.25f + lp * 1.5f;
            }
            return MakeLoop("drone", d);
        }

        static AudioClip Ambience()
        {
            int n = Rate * 8; var d = new float[n]; float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.01f;
                // crickets: 4.4 kHz chirps in little groups
                float group = Mathf.Repeat(t * 1.3f, 1f);
                float chirp = group < 0.25f ? Mathf.Sin(6.2832f * 4400f * t) * Mathf.Max(0f, Mathf.Sin(6.2832f * 28f * t)) : 0f;
                float group2 = Mathf.Repeat(t * 0.9f + 0.37f, 1f);
                float chirp2 = group2 < 0.2f ? Mathf.Sin(6.2832f * 3900f * t) * Mathf.Max(0f, Mathf.Sin(6.2832f * 24f * t)) : 0f;
                d[i] = lp * 3f + chirp * 0.08f + chirp2 * 0.05f;
            }
            return MakeLoop("night", d);
        }

        /// <summary>Highland pipes: drones on A plus a chanter playing a simple 2/4 pipe march (an original tune).</summary>
        static AudioClip MakeBagpipes()
        {
            // chanter notes (Highland scale, roughly A = 466 Hz as real pipes are pitched sharp)
            var scale = new Dictionary<char, float> { { 'G', 415f }, { 'a', 466f }, { 'b', 523f }, { 'c', 587f }, { 'd', 622f }, { 'e', 698f }, { 'f', 784f }, { 'g', 831f }, { 'A', 932f } };
            string tune = "aace eAfe ceaa bcbG aace eAfe cebc aaa- " +
                          "AAfe Afec AAfe cebG AAfe Afec ceaa aaa-";
            float beat = 0.3f;
            var notes = new List<char>(); foreach (var ch in tune) if (ch != ' ') notes.Add(ch);
            int n = (int)(Rate * (notes.Count * beat + 1f));
            var d = new float[n];
            float phase = 0f, dp1 = 0f, dp2 = 0f, dp3 = 0f;
            char held = 'a';
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                int k = Mathf.Min((int)(t / beat), notes.Count - 1);
                char note = notes[k] == '-' ? held : notes[k]; held = note;
                float f = scale.ContainsKey(note) ? scale[note] : 466f;
                float inNote = t - k * beat;
                if (inNote < 0.025f && notes[k] != '-') f = scale['g'];   // the pipers' G grace note before each note
                phase += f / Rate;
                // reedy, buzzing tone: a band-limited sawtooth with a bit of square
                float p = phase - Mathf.Floor(phase);
                float chanter = (2f * p - 1f) * 0.6f + (p < 0.4f ? 0.3f : -0.3f);
                dp1 += 116.5f / Rate; dp2 += 116.5f / Rate * 1.001f; dp3 += 233f / Rate;   // two tenor drones and the bass
                float drones = (2f * (dp1 - Mathf.Floor(dp1)) - 1f) * 0.25f + (2f * (dp2 - Mathf.Floor(dp2)) - 1f) * 0.25f + (2f * (dp3 - Mathf.Floor(dp3)) - 1f) * 0.2f;
                float fadeIn = Mathf.Clamp01(t / 0.4f), fadeOut = Mathf.Clamp01((n / (float)Rate - t) / 0.6f);
                d[i] = (chanter * 0.7f + drones) * fadeIn * fadeOut;
            }
            return Make("bagpipes", d);
        }

        static AudioClip MakeHiss()
        {
            int n = Rate * 4; var d = new float[n]; float lp = 0f, prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, w = Noise();
                lp += (w - lp) * 0.5f;
                float hp = lp - prev; prev = lp;                         // a bright hiss, wavering as it burns
                d[i] = hp * (0.7f + 0.3f * Mathf.PerlinNoise(t * 3f, 1.7f));
            }
            return MakeLoop("smokehiss", d);
        }

        static AudioClip MakePop()
        {
            int n = Rate / 2; var d = new float[n]; float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.25f;
                d[i] = lp * Mathf.Exp(-t / 0.05f) + Mathf.Sin(6.2832f * 140f * t) * Mathf.Exp(-t / 0.03f) * 0.5f;
            }
            return Make("smokepop", d);
        }

        static AudioClip MakeLoop(string name, float[] d)
        {
            // crossfade the last 0.3 s into the start so it loops without a click
            int x = Rate * 3 / 10;
            for (int i = 0; i < x; i++) { float k = i / (float)x; d[i] = d[i] * k + d[d.Length - x + i] * (1f - k); }
            var trimmed = new float[d.Length - x]; System.Array.Copy(d, trimmed, trimmed.Length);
            return Make(name, trimmed, true);
        }
    }
}
