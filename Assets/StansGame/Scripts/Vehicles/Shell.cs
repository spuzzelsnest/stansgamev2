using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Explosive projectile: tank shells and the PIAT bomb burst on impact; hand grenades (fuse &gt; 0) bounce and
    /// roll and go off when the fuse burns down (Mills bomb ~4 s, Stielhandgranate 24 ~4.5 s).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Shell : MonoBehaviour
    {
        public string displayName = "";
        public float directDamage = 400f;
        public float blastDamage = 120f;
        public float blastRadius = 6f;
        public float blastForce = 900f;
        public GameObject explosionPrefab;
        public float maxLifetime = 8f;
        [Tooltip("Hand grenades: seconds from throwing to the bang. 0 = explodes on impact.")]
        public float fuse = 0f;
        [Tooltip("Grenades: what it looks like lying on the ground to be picked up (the model without the physics).")]
        public GameObject pickupVisual;
        [Tooltip("Multiplayer copy shown on other machines: explodes but does no damage (the host's copy does).")]
        public bool visualOnly;
        [Tooltip("Small-calibre rounds (20 mm, .50 cal): a spark and a thud where they strike, not a shell burst.")]
        public bool small;

        [Header("Smoke grenades")]
        [Tooltip("A smoke grenade: when the fuse burns down it makes a smoke screen instead of a bang.")]
        public bool smoke;
        public Material smokeMaterial;
        public Color smokeColour = new Color(0.78f, 0.78f, 0.76f, 0.6f);
        public float smokeRadius = 7f, smokeDuration = 60f;

        Rigidbody rb;
        GameObject owner;
        bool done;
        float launchedAt;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Destroy(gameObject, Mathf.Max(maxLifetime, fuse + 1f));
        }

        public void Launch(Vector3 velocity, GameObject shooter)
        {
            owner = shooter;
            launchedAt = Time.time;
            rb.linearVelocity = velocity;
            if (fuse > 0f) rb.angularVelocity = Random.insideUnitSphere * 8f;   // tumbling through the air
            if (shooter)
                foreach (var c in GetComponentsInChildren<Collider>())
                    foreach (var oc in shooter.GetComponentsInChildren<Collider>())
                        Physics.IgnoreCollision(c, oc);
        }

        void Update()
        {
            if (fuse > 0f && !done && Time.time - launchedAt >= fuse) Explode(transform.position, null);
        }

        void OnCollisionEnter(Collision c)
        {
            if (done) return;
            if (fuse > 0f || smoke)
            {
                if (c.relativeVelocity.magnitude > 2f) Sfx.Impact(transform.position, false);   // clatter as it lands
                return;
            }
            Explode(c.GetContact(0).point, c.collider);
        }

        void Explode(Vector3 p, Collider hitCollider)
        {
            done = true;
            if (smoke)
            {
                // no bang: the smoke pours out where it lies (every machine makes its own cloud)
                SmokeCloud.Spawn(p, smokeMaterial, smokeColour, smokeRadius, smokeDuration, owner);
                Destroy(gameObject);
                return;
            }
            if (small) Sfx.Impact(p, false);
            else { Sfx.Explosion(p, fuse <= 0f); Flash(p); }
            if (visualOnly)
            {
                if (explosionPrefab) Destroy(Instantiate(explosionPrefab, p, Quaternion.identity), 8f);
                Destroy(gameObject);
                return;
            }
            var direct = hitCollider ? hitCollider.GetComponentInParent<Health>() : null;
            if (direct) direct.TakeDamage(new DamageInfo { amount = directDamage, point = p, direction = rb.linearVelocity, force = blastForce, source = owner });

            foreach (var col in Physics.OverlapSphere(p, blastRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (h && h != direct)
                {
                    // something solid between the bang and the man (a wall, sandbags) takes most of it
                    Vector3 to = col.ClosestPoint(p) - p;
                    float cover = Physics.Raycast(p + Vector3.up * 0.2f, (col.bounds.center - p - Vector3.up * 0.2f).normalized, out RaycastHit block,
                        to.magnitude, ~0, QueryTriggerInteraction.Ignore) && !block.collider.transform.IsChildOf(h.transform) ? 0.25f : 1f;
                    float falloff = 1f - Mathf.Clamp01(to.magnitude / blastRadius);
                    h.TakeDamage(new DamageInfo { amount = blastDamage * falloff * cover, point = p, direction = col.transform.position - p, force = blastForce * falloff, source = owner });
                }
                if (col.attachedRigidbody && !col.attachedRigidbody.isKinematic && col.attachedRigidbody != rb)
                    col.attachedRigidbody.AddExplosionForce(blastForce, p, blastRadius, 0.5f, ForceMode.Impulse);
            }
            if (explosionPrefab) Destroy(Instantiate(explosionPrefab, p, Quaternion.identity), 8f);
            NoiseEvents.Report(p, small ? 80f : 300f, owner);
            Destroy(gameObject);
        }

        static void Flash(Vector3 p)
        {
            var go = new GameObject("Explosion flash");
            go.transform.position = p + Vector3.up * 0.5f;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 14f; l.intensity = 10f; l.color = new Color(1f, 0.7f, 0.4f);
            Destroy(go, 0.12f);
        }
    }
}
