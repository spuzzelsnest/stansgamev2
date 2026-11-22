using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Keeps the background sound where the listener is. Crickets and the night breeze belong to the grass: they
    /// fade in over the last 25 m above the ground and are gone higher up (in a glider or a C-47, under a canopy).
    /// Up there you get the rush of air instead, louder the faster you fall.
    /// </summary>
    public class AmbienceMixer : MonoBehaviour
    {
        public AudioSource ground;          // crickets and breeze (set by Sfx)
        public float groundVolume = 0.35f;
        public float windVolume = 0.5f;

        AudioSource air;
        float height = 999f, nextProbe, lastY;
        bool haveLastY;
        static readonly RaycastHit[] hits = new RaycastHit[16];
        float fallSpeed;

        void Start()
        {
            air = gameObject.AddComponent<AudioSource>();
            air.clip = Sfx.Wind; air.loop = true; air.spatialBlend = 0f; air.volume = 0f; air.Play();
            if (ground) ground.volume = 0f;
        }

        void Update()
        {
            var listener = Camera.main ? Camera.main.transform : null;
            if (!listener)
            {
                var al = FindAnyObjectByType<AudioListener>();
                if (al) listener = al.transform;
            }
            if (!listener) return;

            Vector3 p = listener.position;
            if (Time.time >= nextProbe)
            {
                nextProbe = Time.time + 0.2f;
                // height above whatever is below (ground, roof, deck); nothing within 400 m = high in the air
                // (the aircraft you're riding in - C-47 or glider - doesn't count as ground)
                height = 400f; bool hitSomething = false;
                int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.5f, Vector3.down, hits, 400f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var c = hits[i].collider;
                    if (c.GetComponentInParent<C47Flight>() || c.GetComponentInParent<GliderApproach>()) continue;
                    height = Mathf.Min(height, Mathf.Max(0f, hits[i].distance - 0.5f));
                    hitSomething = true;
                }
                // nothing with a collider below (a mesh without one, an unfinished corner, a cinematic camera):
                // don't take that as "high in the air", or the rush of wind plays on and on at ground level
                if (!hitSomething)
                {
                    var terrain = Terrain.activeTerrain;
                    height = terrain ? Mathf.Max(0f, p.y - terrain.SampleHeight(p) - terrain.transform.position.y)
                                     : (p.y > 80f ? 400f : 0f);
                }
            }
            if (haveLastY && Time.deltaTime > 0f) fallSpeed = Mathf.Lerp(fallSpeed, Mathf.Max(0f, (lastY - p.y) / Time.deltaTime), Time.deltaTime * 2f);
            lastY = p.y; haveLastY = true;

            float onGround = 1f - Mathf.InverseLerp(4f, 25f, height);            // 1 near the grass, 0 from 25 m up
            float aloft = Mathf.InverseLerp(6f, 30f, height);
            float rush = Mathf.Clamp01(0.35f + fallSpeed / 12f);
            float k = Time.deltaTime * 1.5f;
            if (ground) ground.volume = Mathf.Lerp(ground.volume, groundVolume * onGround, k);
            if (air)
            {
                air.volume = Mathf.Lerp(air.volume, windVolume * aloft * rush, k);
                air.pitch = Mathf.Lerp(air.pitch, 0.8f + 0.5f * Mathf.Clamp01(fallSpeed / 15f), k);
            }
        }
    }
}
