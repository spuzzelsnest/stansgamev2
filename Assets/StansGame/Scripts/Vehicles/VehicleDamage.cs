using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StansGame
{
    /// <summary>
    /// How a vehicle shows its damage, the way most shooters do it: in stages, not in one jump.
    ///   under 60% - thin grey smoke from the engine deck;
    ///   under 30% - thick black smoke and a small fire on the engine deck;
    ///   destroyed - the blast (flash, fireball, the big bang), the paint scorched black, a column of black smoke and
    ///               flames for minutes; on a turreted tank the ammunition may cook off and lift the turret off its ring
    ///               (it happened often enough to German and Allied tanks in Normandy). The wreck stays where it died.
    /// Added by itself, when a scene loads, to every tank and lorry that can be hurt (it has a Health). Clients follow
    /// the host's hit points, so every player sees the same wreck. repairAfter > 0 puts it right again after that many
    /// seconds (the Proving Ground's practice targets).
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleDamage : MonoBehaviour
    {
        [Tooltip("Seconds after being knocked out before it is as good as new (0 = stays a wreck).")]
        public float repairAfter = 0f;
        [Tooltip("Chance that a turreted tank's ammunition cooks off and blows the turret off.")]
        [Range(0f, 1f)] public float turretBlowChance = 0.35f;

        Health hp;
        ParticleSystem smoke, fire, column;
        Light fireLight;
        Vector3 engineLocal;
        float size = 3f;
        int stage = -1;
        float deadAt = -1f, nextCheck;
        readonly List<(Renderer r, MaterialPropertyBlock before)> scorched = new List<(Renderer, MaterialPropertyBlock)>();
        TankController owner; Transform blownTurret; Transform turretParent; Vector3 turretPos; Quaternion turretRot;

        static Material puffMat;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        // ------------------------------------------------------------------ fitted by itself

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneLoaded += OnLoaded;
            FitAll();
        }
        static void OnLoaded(Scene s, LoadSceneMode m) => FitAll();

        static void FitAll()
        {
            foreach (var t in Object.FindObjectsByType<TankController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Fit(t.gameObject);
            foreach (var t in Object.FindObjectsByType<TruckController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Fit(t.gameObject);
        }
        static void Fit(GameObject go)
        {
            if (!go.GetComponent<Health>() || go.GetComponent<VehicleDamage>()) return;
            go.AddComponent<VehicleDamage>();
        }

        // ------------------------------------------------------------------ set up

        void Start()
        {
            hp = GetComponent<Health>();
            if (!hp) { enabled = false; return; }
            hp.Died += (h, i) => KnockOut();

            // the size of it, and where the engine is: at the back of a tank, under the bonnet of a lorry
            var b = new Bounds(transform.position, Vector3.zero); bool any = false;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled || r.GetComponent<TextMesh>()) continue;   // (not the name boards over it)
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            Vector3 lc = transform.InverseTransformPoint(b.center);
            Vector3 ext = new Vector3(b.extents.x, b.extents.y, b.extents.z);
            size = Mathf.Clamp(Mathf.Max(ext.x, ext.z) * 2f, 2f, 9f);
            bool tank = GetComponent<TankController>();
            float along = Mathf.Max(ext.x, ext.z) * 0.55f;
            engineLocal = lc + new Vector3(0f, ext.y * 0.6f, tank ? -along : along);

            if (!puffMat) puffMat = MakePuffMaterial();
            smoke = Emitter("Damage smoke", 0.35f, 1.2f, 0.6f, 5f, new Color(0.55f, 0.55f, 0.55f, 0.35f), 1.8f, false);
            fire = Emitter("Damage fire", 0.25f, 0.5f, 1.2f, 0.8f, new Color(1f, 0.55f, 0.2f, 0.9f), 0.4f, true);
            column = Emitter("Wreck smoke", 0.6f, size * 0.45f, 1.5f, 9f, new Color(0.08f, 0.08f, 0.08f, 0.55f), 2.4f, false);
            var lg = new GameObject("Fire light"); lg.transform.SetParent(transform, false); lg.transform.localPosition = engineLocal + Vector3.up * 0.5f;
            fireLight = lg.AddComponent<Light>(); fireLight.type = LightType.Point; fireLight.color = new Color(1f, 0.55f, 0.25f);
            fireLight.range = size * 2.5f; fireLight.intensity = 0f; fireLight.shadows = LightShadows.None; fireLight.enabled = false;

            if (hp.IsDead) KnockOut(true);
        }

        ParticleSystem Emitter(string name, float startSize, float endSize, float speed, float life, Color colour, float rise, bool additiveLook)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.transform.localPosition = engineLocal;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // up
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(startSize * 0.8f, startSize * 1.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = colour;
            main.maxParticles = additiveLook ? 60 : 200;
            main.gravityModifier = -0.02f * rise;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = additiveLook ? 8f : 14f; shape.radius = Mathf.Min(0.8f, size * 0.12f);
            var em = ps.emission; em.rateOverTime = 0f;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, endSize / Mathf.Max(0.01f, startSize)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            if (additiveLook)
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.1f), 0.6f), new GradientColorKey(new Color(0.2f, 0.2f, 0.2f), 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            else
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            // the smoke leans with the wind
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            Vector3 w = Weather.WindTo.normalized * (0.6f + Weather.WindStrength * 2.2f);
            vel.x = new ParticleSystem.MinMaxCurve(w.x * 0.5f, w.x); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(w.z * 0.5f, w.z);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = puffMat; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        static Material MakePuffMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!sh) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "Vehicle smoke (runtime)" };
            const int N = 64; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a) * (0.7f + 0.3f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        // ------------------------------------------------------------------ the stages

        void Update()
        {
            if (!hp) return;
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.4f;
                int want = hp.IsDead ? 3 : hp.Current < hp.maxHealth * 0.3f ? 2 : hp.Current < hp.maxHealth * 0.6f ? 1 : 0;
                if (want != stage && want < 3) SetStage(want);
                if (hp.IsDead && repairAfter > 0f && deadAt > 0f && Time.time - deadAt > repairAfter) Repair();
            }
            if (fireLight && fireLight.enabled)
            {
                float flick = 0.6f + Mathf.PerlinNoise(Time.time * 7f, 0.3f) * 0.8f;
                float baseI = stage >= 3 ? 9f : 3f;
                if (stage >= 3 && deadAt > 0f) baseI *= Mathf.Clamp01(1.2f - (Time.time - deadAt) / 300f);   // burns down over five minutes
                fireLight.intensity = Mathf.Max(0f, baseI * flick);
            }
        }

        void SetStage(int s)
        {
            stage = s;
            Rate(smoke, s == 1 ? 6f : s == 2 ? 14f : 0f);
            if (s == 2) { var m = smoke.main; m.startColor = new Color(0.12f, 0.12f, 0.12f, 0.5f); }
            else if (s == 1) { var m = smoke.main; m.startColor = new Color(0.55f, 0.55f, 0.55f, 0.35f); }
            Rate(fire, s == 2 ? 10f : 0f);
            Rate(column, 0f);
            fireLight.enabled = s == 2;
        }

        static void Rate(ParticleSystem ps, float r)
        {
            if (!ps) return;
            var em = ps.emission; em.rateOverTime = r;
            if (r > 0f && !ps.isPlaying) ps.Play();
        }

        void KnockOut(bool quiet = false)
        {
            if (stage == 3 || !smoke) return;
            stage = 3; deadAt = Time.time;
            Vector3 engine = transform.TransformPoint(engineLocal);
            if (!quiet)
            {
                Sfx.VehicleExplosion(engine);
                NoiseEvents.Report(engine, 400f, gameObject);
                Fireball(engine);
            }
            Rate(smoke, 0f);
            Rate(fire, 22f);
            Rate(column, 10f);
            fireLight.enabled = true;
            Scorch();
            // the ammunition cooks off: the turret comes off its ring (the same for every player: seeded by where it is)
            var tc = GetComponent<TankController>();
            var rnd = new System.Random(Mathf.RoundToInt(transform.position.x * 13f + transform.position.z * 7f));
            if (!quiet && tc && tc.turret && tc.turret != transform && rnd.NextDouble() < turretBlowChance) { owner = tc; BlowTurret(tc.turret, rnd); tc.turret = null; }
        }

        void Fireball(Vector3 at)
        {
            var go = new GameObject("Fireball"); go.transform.position = at;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = size * 6f; l.intensity = 40f; l.color = new Color(1f, 0.65f, 0.3f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.duration = 0.3f; main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f); main.startSize = new ParticleSystem.MinMaxCurve(size * 0.4f, size * 0.8f);
            main.startColor = new Color(1f, 0.6f, 0.25f, 0.9f); main.gravityModifier = -0.15f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = size * 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(0.9f, 0.3f, 0.05f), 0.4f), new GradientColorKey(new Color(0.1f, 0.1f, 0.1f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = puffMat;
            ps.Play();
            go.AddComponent<FireballFade>().lamp = l;
            Destroy(go, 3f);
        }

        /// <summary>Burnt paint: everything darkened to scorched grey-black (no new textures needed).</summary>
        void Scorch()
        {
            if (scorched.Count > 0) return;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r.GetComponent<TextMesh>()) continue;
                var before = new MaterialPropertyBlock(); r.GetPropertyBlock(before);
                var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb);
                var sm = r.sharedMaterial;
                Color c = sm && sm.HasProperty(BaseColorId) ? sm.GetColor(BaseColorId) : sm && sm.HasProperty(ColorId) ? sm.GetColor(ColorId) : Color.white;
                Color burnt = new Color(c.r * 0.22f + 0.03f, c.g * 0.2f + 0.025f, c.b * 0.18f + 0.02f, c.a);
                mpb.SetColor(BaseColorId, burnt); mpb.SetColor(ColorId, burnt);
                r.SetPropertyBlock(mpb);
                scorched.Add((r, before));
            }
        }

        void BlowTurret(Transform turret, System.Random rnd)
        {
            blownTurret = turret; turretParent = turret.parent; turretPos = turret.localPosition; turretRot = turret.localRotation;
            turret.SetParent(null, true);
            var rb = turret.gameObject.AddComponent<Rigidbody>();
            rb.mass = 2500f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (!turret.GetComponentInChildren<Collider>())
            {
                var bc = turret.gameObject.AddComponent<BoxCollider>();
                var rs = turret.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    bc.center = turret.InverseTransformPoint(b.center);
                    bc.size = new Vector3(b.size.x / Mathf.Max(0.01f, turret.lossyScale.x), b.size.y / Mathf.Max(0.01f, turret.lossyScale.y), b.size.z / Mathf.Max(0.01f, turret.lossyScale.z)) * 0.9f;
                }
            }
            Vector3 side = new Vector3((float)rnd.NextDouble() - 0.5f, 0f, (float)rnd.NextDouble() - 0.5f);
            rb.AddForce((Vector3.up * 9f + side * 4f) * rb.mass, ForceMode.Impulse);
            rb.AddTorque(new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * rb.mass * 3f, ForceMode.Impulse);
        }

        /// <summary>As good as new (practice targets).</summary>
        void Repair()
        {
            foreach (var (r, before) in scorched) if (r) r.SetPropertyBlock(before);
            scorched.Clear();
            if (blownTurret)
            {
                var rb = blownTurret.GetComponent<Rigidbody>(); if (rb) Destroy(rb);
                blownTurret.SetParent(turretParent, false); blownTurret.localPosition = turretPos; blownTurret.localRotation = turretRot;
                if (owner) owner.turret = blownTurret;
                blownTurret = null;
            }
            Rate(fire, 0f); Rate(column, 0f); Rate(smoke, 0f);
            fireLight.enabled = false;
            deadAt = -1f; stage = -1;
            hp.Revive();
        }

        class FireballFade : MonoBehaviour
        {
            public Light lamp; float t0;
            void Start() => t0 = Time.time;
            void Update() { if (lamp) lamp.intensity = 40f * Mathf.Clamp01(1f - (Time.time - t0) / 0.8f); }
        }
    }
}
