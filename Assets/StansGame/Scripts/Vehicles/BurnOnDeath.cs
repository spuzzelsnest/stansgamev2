using UnityEngine;

namespace StansGame
{
    /// <summary>A knocked-out vehicle brews up: flickering fire light, smoke, and the odd round cooking off.</summary>
    [RequireComponent(typeof(Health))]
    public class BurnOnDeath : MonoBehaviour
    {
        public Light fireLight;
        public ParticleSystem smoke;
        public float cookOffInterval = 4f;
        bool burning;
        float nextCook;

        void Awake() => GetComponent<Health>().Died += (h, i) => Ignite();

        void Ignite()
        {
            burning = true;
            if (smoke) smoke.Play();
            var rb = GetComponent<Rigidbody>();
            if (rb) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }
            nextCook = Time.time + 1f;
        }

        void Update()
        {
            if (!burning) return;
            if (fireLight) fireLight.intensity = 6f + Mathf.PerlinNoise(Time.time * 6f, 0f) * 6f;
            if (Time.time > nextCook)
            {
                nextCook = Time.time + cookOffInterval * Random.Range(0.5f, 1.8f);
                NoiseEvents.Report(transform.position, 200f, gameObject);
                if (fireLight) fireLight.intensity = 30f;
            }
        }
    }
}
