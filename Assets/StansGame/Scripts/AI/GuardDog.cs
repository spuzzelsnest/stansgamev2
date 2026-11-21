using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// A German army guard dog (Diensthund) on the leash of a sentry: walks at his left side on his rounds. When the
    /// shooting starts it goes for the nearest British soldier within reach, barking, and bites; if its handler falls it
    /// stays by him and barks. It can be shot (it has a Health of its own and counts as German).
    /// Animation: the Idle / Walk / Trot loops of GermanShepherd.fbx, chosen by speed.
    /// Multiplayer: the host runs the dog (where it goes, whom it bites); it is a network object, and the other
    /// machines pick its walk or trot from how fast it moves and bark on their own once the alarm is up.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class GuardDog : MonoBehaviour
    {
        public Transform handler;
        public Animator animator;
        public float walkClipSpeed = 1.3f;      // ground speed the Walk loop is keyed for, m/s
        public float trotClipSpeed = 3.6f;
        public float chargeSpeed = 6.5f;
        public float attackRange = 30f;
        public float biteDamage = 12f;

        Health self, handlerHealth, prey;
        string state = "";
        float speedNow, nextBite, nextBark, nextScan;
        AudioSource voice;
        static AudioClip bark;

        void Awake()
        {
            self = GetComponent<Health>();
            self.faction = Faction.German;
            self.Died += (h, d) => Die();
            if (handler) handlerHealth = handler.GetComponent<Health>();
            voice = gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 1f; voice.minDistance = 4f; voice.maxDistance = 120f;
            if (!bark) bark = MakeBark();
        }

        Vector3 lastPos;
        float seenSpeed;

        void Update()
        {
            if (self.IsDead) return;
            if (!Net.Simulates)
            {
                // a remote machine: the host moves the dog; animate and bark from what we see
                Vector3 moved = Flat(transform.position - lastPos); lastPos = transform.position;
                if (Time.deltaTime > 0f) seenSpeed = Mathf.Lerp(seenSpeed, Mathf.Min(moved.magnitude / Time.deltaTime, 12f), 8f * Time.deltaTime);
                speedNow = seenSpeed;
                if (SoldierAI.Alarm) Bark();
                Animate();
                return;
            }
            bool handlerDown = !handler || (handlerHealth && handlerHealth.IsDead);
            Vector3 goal; float want;
            if (SoldierAI.Alarm || handlerDown)
            {
                if (Time.time > nextScan) { nextScan = Time.time + 0.5f; prey = handlerDown ? null : Nearest(); }
                if (prey && !prey.IsDead)
                {
                    goal = prey.transform.position; want = chargeSpeed;
                    float d = Flat(goal - transform.position).magnitude;
                    if (d < 1.3f)
                    {
                        want = 0f;
                        if (Time.time > nextBite)
                        {
                            nextBite = Time.time + 1.1f;
                            prey.TakeDamage(new DamageInfo { amount = biteDamage, point = prey.transform.position + Vector3.up, direction = Flat(prey.transform.position - transform.position).normalized, source = gameObject });
                        }
                    }
                }
                else
                {
                    // nobody to go for: stay by the handler (or by his body)
                    goal = handler ? handler.position + handler.right * -1.0f : transform.position; want = 0f;
                    if (Flat(goal - transform.position).magnitude > 2f) want = trotClipSpeed;
                }
                Bark();
            }
            else
            {
                // at heel: on the handler's left, a little ahead
                goal = handler.position + handler.right * -1.0f + handler.forward * 0.4f;
                float d = Flat(goal - transform.position).magnitude;
                want = d < 0.25f ? 0f : Mathf.Clamp(d * 2.2f, 0.6f, 4.5f);
            }

            speedNow = Mathf.MoveTowards(speedNow, want, 9f * Time.deltaTime);
            Vector3 to = Flat(goal - transform.position);
            if (to.magnitude > 0.05f && speedNow > 0.05f)
            {
                Vector3 step = to.normalized * Mathf.Min(speedNow * Time.deltaTime, to.magnitude);
                Vector3 p = transform.position + step;
                p.y = GroundY(p);
                transform.position = p;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to.normalized), 360f * Time.deltaTime);
            }
            else if (handler && !SoldierAI.Alarm)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(Flat(handler.forward).normalized), 90f * Time.deltaTime);
            }
            Animate();
        }

        void Bark()
        {
            if (Time.time > nextBark) { nextBark = Time.time + Random.Range(0.7f, 2.2f); voice.pitch = Random.Range(0.9f, 1.1f); voice.PlayOneShot(bark, 0.9f); }
        }

        void Animate()
        {
            if (!animator) return;
            string want = speedNow < 0.2f ? "Idle" : speedNow < 2.2f ? "Walk" : "Trot";
            if (want != state) { animator.CrossFade(want, 0.2f); state = want; }
            animator.speed = want == "Walk" ? speedNow / walkClipSpeed : want == "Trot" ? Mathf.Max(0.7f, speedNow / trotClipSpeed) : 1f;
        }

        Health Nearest()
        {
            Health best = null; float bd = attackRange;
            foreach (var h in Health.All)
            {
                if (!h || h.IsDead || h.faction != Faction.British || !h.GetComponent<CharacterController>()) continue;
                float d = Vector3.Distance(h.transform.position, transform.position);
                if (d < bd) { bd = d; best = h; }
            }
            return best;
        }

        void Die()
        {
            if (animator) animator.enabled = false;
            voice.Stop();
            // down on its side
            transform.rotation = Quaternion.LookRotation(Flat(transform.forward).normalized) * Quaternion.Euler(0f, 0f, 85f);
            if (Net.Simulates) transform.position += Vector3.up * 0.2f;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            enabled = false;
        }

        float GroundY(Vector3 p)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, transform.position.y + 1.5f, p.z), Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || h.collider is CharacterController) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? p.y : best;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>A gruff double bark: two short bursts of a falling, noisy tone.</summary>
        static AudioClip MakeBark()
        {
            int rate = 22050, n = (int)(rate * 0.42f);
            var d = new float[n]; var rng = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = 0f, f0 = 0f;
                if (t < 0.14f) { env = Mathf.Sin(Mathf.PI * t / 0.14f); f0 = 520f - 1400f * t; }
                else if (t > 0.22f && t < 0.38f) { float u = t - 0.22f; env = 0.8f * Mathf.Sin(Mathf.PI * u / 0.16f); f0 = 480f - 1200f * u; }
                float ph = 2f * Mathf.PI * f0 * t;
                float v = 0.55f * Mathf.Sin(ph) + 0.25f * Mathf.Sin(2f * ph) + 0.12f * Mathf.Sin(3f * ph) + 0.25f * (float)(rng.NextDouble() * 2 - 1);
                d[i] = Mathf.Clamp(v * env * 0.9f, -1f, 1f);
            }
            var c = AudioClip.Create("Bark", n, 1, rate, false); c.SetData(d, 0); return c;
        }
    }
}
