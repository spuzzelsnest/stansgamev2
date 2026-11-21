using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// The fire in Madame Pommier's house on the square (it broke out around 23:00 on 5 June): flickering firelight
    /// that lit up the square and the paratroopers coming down into it, glowing embers and a column of smoke.
    /// </summary>
    public class HouseFire : MonoBehaviour
    {
        public float intensity = 9f, range = 55f;
        public float dangerRadius = 4f;            // anyone who comes down in it
        Light glow;
        ParticleSystem flames, smoke;

        void Start()
        {
            glow = new GameObject("Firelight").AddComponent<Light>();
            glow.transform.SetParent(transform, false); glow.transform.localPosition = new Vector3(0f, 6f, 0f);
            glow.type = LightType.Point; glow.range = range; glow.color = new Color(1f, 0.55f, 0.2f); glow.shadows = LightShadows.Soft;
            flames = MakeParticles("Flames", new Color(1f, 0.6f, 0.15f, 0.9f), new Color(0.9f, 0.2f, 0.05f, 0f), 2.5f, 1.4f, 3.5f, 60f);
            smoke = MakeParticles("Smoke", new Color(0.12f, 0.11f, 0.10f, 0.6f), new Color(0.2f, 0.2f, 0.2f, 0f), 7f, 6f, 2.2f, 12f);
            smoke.transform.localPosition = new Vector3(0f, 6f, 0f);
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = Sfx.Wind; src.loop = true; src.spatialBlend = 1f; src.maxDistance = 60f; src.volume = 0.5f; src.pitch = 0.5f; src.Play();
        }

        ParticleSystem MakeParticles(string name, Color a, Color b, float life, float size, float speed, float rate)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false); go.transform.localPosition = new Vector3(0f, 3f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = life; main.startSize = size; main.startSpeed = speed; main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 3f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) }, new[] { new GradientAlphaKey(a.a, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
            r.sharedMaterial.color = a;
            return ps;
        }

        void Update()
        {
            if (glow) glow.intensity = intensity * (0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 6f, 0.3f));
            // anyone who lands in the blaze does not get out
            for (int i = Health.All.Count - 1; i >= 0; i--)
            {
                if (i >= Health.All.Count) continue;   // a death can take others off the list
                var h = Health.All[i];
                if (!h || h.IsDead) continue;
                Vector3 d = h.transform.position - transform.position; d.y = 0f;
                if (d.sqrMagnitude < dangerRadius * dangerRadius && h.transform.position.y < transform.position.y + 9f)
                    h.TakeDamage(new DamageInfo { amount = 60f * Time.deltaTime, point = h.transform.position, direction = Vector3.up, source = gameObject });
            }
        }
    }
}
