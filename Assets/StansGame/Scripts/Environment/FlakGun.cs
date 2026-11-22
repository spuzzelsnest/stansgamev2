using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// German 2 cm Flakvierling 38 hosing tracer up at the aircraft passing over.
    ///
    /// The model (Models/Vehicles/Flakvierling38_Parts.fbx) has a fixed base, a <see cref="carriage"/> that traverses
    /// all the way round and a <see cref="cradle"/> carrying the four guns that elevates from -10 to +100 degrees
    /// (-10/+90 here). One gunner, sitting behind the guns, laid both with his two handwheels; a loader sat on each side
    /// changing the 20-round magazines. The gun turns and lifts its barrels onto the target at handwheel speed and only
    /// fires once it is laid; the rounds leave the four muzzles in diagonal pairs along the barrels.
    ///
    /// Crew (<see cref="crew"/>: 0 gunner, 1 left loader, 2 right loader): no living gunner, no firing. If he is hit, a
    /// surviving loader climbs into his seat after a few seconds. Fewer loaders, slower magazine changes. The rest of the
    /// gun detachment (commander, ammunition numbers) are ordinary soldiers round the emplacement.
    /// Without the parts (an old model, or the primitive stand-in) it falls back to one <see cref="barrel"/> point.
    /// </summary>
    public class FlakGun : MonoBehaviour
    {
        public float burstSeconds = 1.6f, pauseMin = 1.5f, pauseMax = 4f;
        public float volleysPerSecond = 8f;        // each volley = one diagonal pair; the four guns together ~1,000 rpm
        public float muzzleVelocity = 300f;        // slowed down so the tracer can be seen climbing
        public float traverseSpeed = 45f, elevationSpeed = 35f, minElevation = -10f, maxElevation = 90f;
        public Transform barrel;                   // fallback: a single muzzle point
        public bool firing = true;

        [Header("Model parts (Flakvierling38_Parts)")]
        public Transform carriage, cradle;
        public Transform[] muzzles;                // 1..4: right upper, right lower, left upper, left lower
        public Health[] crew;                      // 0 gunner, 1 loader left, 2 loader right (seated on the carriage)
        public Transform[] seats;                  // same order
        public Transform wheelTraverse, wheelElevation;

        readonly List<(Transform t, Vector3 v, float die)> rounds = new List<(Transform, Vector3, float)>();
        float burstEnd, nextBurst, nextVolley, reloadUntil, gunnerLostAt = -1f;
        int pair, shotsLeft = 40;
        Material tracer;
        Vector3 aim;
        float yaw, elev;
        Quaternion car0, cra0; Vector3 fwd0, axis0;
        bool parts;
        FlakCrewSeat[] seated;

        void Start()
        {
            tracer = RuntimeMaterials.Unlit(new Color(1f, 0.55f, 0.15f));   // (null = no rounds drawn, the gun still fires)
            nextBurst = Time.time + Random.Range(0f, 3f);
            parts = carriage && cradle && muzzles != null && muzzles.Length > 0;
            if (parts)
            {
                car0 = carriage.rotation; cra0 = cradle.rotation;
                Vector3 mid = Vector3.zero; foreach (var m in muzzles) mid += m.position; mid /= muzzles.Length;
                fwd0 = Vector3.ProjectOnPlane(mid - cradle.position, Vector3.up).normalized;   // the barrels are level at rest
                axis0 = Vector3.Cross(Vector3.up, fwd0).normalized;
                aim = fwd0;
            }
            seated = new FlakCrewSeat[crew != null ? crew.Length : 0];
            for (int i = 0; i < seated.Length; i++)
            {
                if (!crew[i]) continue;
                var anim = crew[i].GetComponentInChildren<Animator>();
                if (!anim) continue;
                var s = anim.gameObject.AddComponent<FlakCrewSeat>();
                s.Init(this, crew[i], seats != null && i < seats.Length ? seats[i] : null, i == 0);
                seated[i] = s;
                var h = crew[i];
                h.Died += (dead, info) => OnCrewDied(dead);
            }
        }

        bool Alive(int i) => crew != null && i < crew.Length && crew[i] && !crew[i].IsDead;
        bool Manned => crew == null || crew.Length == 0 || Alive(0);
        int Loaders => (Alive(1) ? 1 : 0) + (Alive(2) ? 1 : 0);

        void OnCrewDied(Health dead)
        {
            // the dead fall off the gun, not round with it
            dead.transform.SetParent(transform, true);
            if (crew.Length > 0 && dead == crew[0]) gunnerLostAt = Time.time;
        }

        void Update()
        {
            // a loader takes over the gunner's seat
            if (gunnerLostAt > 0f && Time.time > gunnerLostAt + 5f)
            {
                gunnerLostAt = -1f;
                for (int i = 1; i < crew.Length; i++)
                    if (Alive(i))
                    {
                        var s = seated[i];
                        crew[0] = crew[i]; crew[i] = null;
                        seated[0] = s; seated[i] = null;
                        if (s) s.Reseat(seats[0], true);
                        break;
                    }
            }

            bool manned = Manned;
            if (firing && manned && Time.time >= nextBurst)
            {
                burstEnd = Time.time + burstSeconds; nextBurst = burstEnd + Random.Range(pauseMin, pauseMax);
                // aim at the nearest aircraft in the sky, leading it; otherwise just up into the dark
                var target = NearestAircraft();
                Vector3 from = parts ? cradle.position : transform.position;
                if (target)
                {
                    float tof = (target.transform.position - from).magnitude / muzzleVelocity;
                    aim = (target.transform.position + target.Velocity * tof - from).normalized;
                }
                else aim = Quaternion.Euler(Random.Range(-35f, -70f), Random.Range(0f, 360f), 0f) * Vector3.forward;
            }
            if (Time.time < burstEnd && NearestAircraft() is C47Flight follow && parts)
            {
                // keep tracking it through the burst
                float tof = (follow.transform.position - cradle.position).magnitude / muzzleVelocity;
                aim = (follow.transform.position + follow.Velocity * tof - cradle.position).normalized;
            }

            bool laid = true;
            if (parts && manned)
            {
                Vector3 flat = Vector3.ProjectOnPlane(aim, Vector3.up);
                float wantYaw = flat.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(fwd0, flat, Vector3.up) : yaw;
                float wantElev = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg, minElevation, maxElevation);
                yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, traverseSpeed * Time.deltaTime);
                elev = Mathf.MoveTowards(elev, wantElev, elevationSpeed * Time.deltaTime);
                laid = Mathf.Abs(Mathf.DeltaAngle(yaw, wantYaw)) < 4f && Mathf.Abs(elev - wantElev) < 4f;
                Pose();
            }

            if (manned && laid && Time.time < burstEnd && Time.time >= nextVolley && Time.time >= reloadUntil)
            {
                nextVolley = Time.time + 1f / volleysPerSecond;
                Volley();
                if (--shotsLeft <= 0)
                {
                    // 4 x 20-round magazines: two loaders change them quickly, one slower, the gunner alone slowest
                    shotsLeft = 40;
                    int l = crew != null && crew.Length > 0 ? Loaders : 2;
                    reloadUntil = Time.time + (l >= 2 ? 3.5f : l == 1 ? 6.5f : 12f);
                    foreach (var s in seated) if (s) s.Reload(reloadUntil);
                }
            }
            MoveRounds();
        }

        void Pose()
        {
            var qy = Quaternion.AngleAxis(yaw, Vector3.up);
            carriage.rotation = qy * car0;
            cradle.rotation = Quaternion.AngleAxis(-elev, qy * axis0) * qy * cra0;
        }

        /// <summary>The direction the barrels point.</summary>
        public Vector3 BarrelForward => parts ? Quaternion.AngleAxis(-elev, Quaternion.AngleAxis(yaw, Vector3.up) * axis0) * Quaternion.AngleAxis(yaw, Vector3.up) * fwd0 : aim;
        public float Elevation => elev;
        /// <summary>The way the carriage faces (level).</summary>
        public Vector3 YawForward => parts ? Quaternion.AngleAxis(yaw, Vector3.up) * fwd0 : transform.forward;

        void Volley()
        {
            Vector3 dir = BarrelForward;
            if (parts)
            {
                // diagonal pairs: right-upper with left-lower, then right-lower with left-upper
                int a = pair == 0 ? 0 : 1, b = pair == 0 ? 3 : 2;
                pair ^= 1;
                Fire(muzzles[Mathf.Min(a, muzzles.Length - 1)].position, dir);
                Fire(muzzles[Mathf.Min(b, muzzles.Length - 1)].position, dir);
                Flash(muzzles[Mathf.Min(a, muzzles.Length - 1)].position + dir * 0.1f);
            }
            else Fire(barrel ? barrel.position : transform.position + Vector3.up * 1.5f, dir);
            if (Random.value < 0.5f) Sfx.At(Sfx.GunClip("kwk"), transform.position, 0.45f, 1.9f, 900f);
        }

        void Fire(Vector3 at, Vector3 dir)
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            Destroy(t.GetComponent<Collider>());
            t.GetComponent<Renderer>().sharedMaterial = tracer;
            t.localScale = new Vector3(0.35f, 0.35f, 2.8f);
            Vector3 d = (dir + Random.insideUnitSphere * 0.012f).normalized;
            t.position = at + d * 1.4f;   // drawn just clear of the flash hider
            t.rotation = Quaternion.LookRotation(d);
            rounds.Add((t, d * muzzleVelocity, Time.time + 4f));
        }

        void MoveRounds()
        {
            for (int i = rounds.Count - 1; i >= 0; i--)
            {
                var (t, v, die) = rounds[i];
                if (!t) { rounds.RemoveAt(i); continue; }
                v += Vector3.down * 9.81f * Time.deltaTime;
                t.position += v * Time.deltaTime;
                t.rotation = Quaternion.LookRotation(v);
                if (Time.time > die)
                {
                    Burst(t.position);   // self-destruct airburst (the tracer burns through after ~4.5 s)
                    Destroy(t.gameObject); rounds.RemoveAt(i); continue;
                }
                rounds[i] = (t, v, die);
            }
        }

        static C47Flight NearestAircraft()
        {
            C47Flight best = null; float bd = float.MaxValue;
            foreach (var c in FindObjectsByType<C47Flight>())
            {
                if (!c.Flying) continue;
                float d = c.transform.position.sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        static void Flash(Vector3 p)
        {
            var go = new GameObject("Flak muzzle flash");
            go.transform.position = p;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 9f; l.intensity = 4f; l.color = new Color(1f, 0.75f, 0.4f);
            Destroy(go, 0.05f);
        }

        static void Burst(Vector3 p)
        {
            var go = new GameObject("Flak burst");
            go.transform.position = p;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 40f; l.intensity = 6f; l.color = new Color(1f, 0.7f, 0.35f);
            Destroy(go, 0.12f);
        }
    }

    /// <summary>
    /// A flak crewman sitting on his seat on the gun: hips locked to the seat (which turns with the carriage), feet on
    /// the foot rests, the gunner's hands on the traverse and elevation handwheels, a loader's hands at the magazines
    /// on his side (reaching up to them while he changes them).
    /// </summary>
    public class FlakCrewSeat : MonoBehaviour
    {
        FlakGun gun; Health health; Transform seat; bool gunner;
        Animator anim; Transform root, hips;
        float reloadUntil;

        public void Init(FlakGun g, Health h, Transform s, bool isGunner)
        {
            gun = g; health = h; seat = s; gunner = isGunner;
            anim = GetComponent<Animator>(); root = h.transform; hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            foreach (var mb in h.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb is HandIK) mb.enabled = false;
            // (his CharacterController stays on: it is what bullets hit while he is alive; nothing moves him with it)
        }

        public void Reseat(Transform s, bool isGunner) { seat = s; gunner = isGunner; if (s) root.SetParent(s.parent, true); }
        public void Reload(float until) => reloadUntil = until;

        void LateUpdate()
        {
            if (!seat || !hips || health.IsDead) return;
            // face the way the seat faces (the carriage's forward) and sit the hips on the seat
            root.rotation = Quaternion.LookRotation(gun.YawForward, Vector3.up);
            root.position += seat.position - hips.position;
        }

        void OnAnimatorIK(int layer)
        {
            if (!seat || health.IsDead) return;
            var fwd = gun.YawForward;
            var right = Vector3.Cross(Vector3.up, fwd);
            Vector3 h = seat.position;
            // feet forward and down on the foot rests: the legs fold into a sitting pose
            foreach (var (goal, hint, side) in new[] { (AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, -1f), (AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, 1f) })
            {
                anim.SetIKPositionWeight(goal, 1f); anim.SetIKRotationWeight(goal, 1f);
                anim.SetIKPosition(goal, h + fwd * 0.42f - Vector3.up * (gunner ? 0.42f : 0.45f) + right * side * 0.14f);
                anim.SetIKRotation(goal, Quaternion.LookRotation(fwd, Vector3.up));
                anim.SetIKHintPositionWeight(hint, 1f);
                anim.SetIKHintPosition(hint, h + fwd * 0.6f + Vector3.up * 0.1f + right * side * 0.15f);
            }
            Vector3 lh, rh;
            if (gunner && gun.wheelTraverse && gun.wheelElevation)
            {
                lh = gun.wheelTraverse.position; rh = gun.wheelElevation.position;
                // turning the wheels: the hands circle a little
                float t = Time.time * 4f;
                lh += (fwd * Mathf.Cos(t) + Vector3.up * Mathf.Sin(t)) * 0.05f;
                rh += (fwd * Mathf.Sin(t) + Vector3.up * Mathf.Cos(t)) * 0.05f;
            }
            else
            {
                // a loader: hands at the magazines of the guns on his side, up at them while changing them
                bool reloading = Time.time < reloadUntil;
                var c = gun.cradle;
                Vector3 mag = c ? c.position + right * Vector3.Dot(seat.position - c.position, right) * 0.6f + gun.BarrelForward * 0.15f + Vector3.up * 0.2f
                                : h + fwd * 0.4f + Vector3.up * 0.5f;
                Vector3 rest = h + fwd * 0.35f + Vector3.up * 0.25f;
                Vector3 p = reloading ? mag : Vector3.Lerp(rest, mag, 0.35f);
                lh = p - right * 0.12f; rh = p + right * 0.12f;
            }
            anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f); anim.SetIKPosition(AvatarIKGoal.LeftHand, lh);
            anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f); anim.SetIKPosition(AvatarIKGoal.RightHand, rh);
            anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, 1f); anim.SetIKRotation(AvatarIKGoal.LeftHand, Quaternion.LookRotation(fwd, right));
            anim.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f); anim.SetIKRotation(AvatarIKGoal.RightHand, Quaternion.LookRotation(fwd, -right));
            anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 1f); anim.SetIKHintPosition(AvatarIKHint.LeftElbow, h - right * 0.4f + Vector3.up * 0.2f);
            anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 1f); anim.SetIKHintPosition(AvatarIKHint.RightElbow, h + right * 0.4f + Vector3.up * 0.2f);
            // the gunner watches the target through his sight; loaders watch their magazines
            anim.SetLookAtWeight(0.7f, 0.2f, 0.8f, 1f, 0.5f);
            anim.SetLookAtPosition(gunner ? seat.position + Vector3.up * 0.6f + gun.BarrelForward * 10f : (gun.cradle ? gun.cradle.position : h + fwd));
        }
    }
}
