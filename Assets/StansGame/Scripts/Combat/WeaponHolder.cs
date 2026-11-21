using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Places the weapon against the soldier's body the way that kind of weapon was carried (rifle and SMG at the
    /// shoulder, Bren and MG 42 at the hip, pistols out in front with both hands, PIAT on the shoulder), pointing
    /// where the soldier aims. <see cref="HandIK"/> then puts both hands on the gun: right hand on the grip, left hand
    /// on the forestock / sleeve / handle (or on the magazine while reloading).
    /// Disabled on death so the gun drops with the ragdoll.
    /// </summary>
    public class WeaponHolder : MonoBehaviour, IDisableOnDeath
    {
        public Transform hand;
        public Animator animator;
        public Vector3 handOffset = new Vector3(0f, 0.02f, 0.05f);   // kept for old prefabs; no longer used
        public Transform aimSource;   // optional: camera for the player; otherwise the body's forward

        [Tooltip("How far the gun tilts up or down with the aim (degrees). It never turns sideways away from the body.")]
        public float maxPitch = 35f;
        float pitch;

        /// <summary>The weapon in the soldier's hands (null when he has a grenade out or nothing).</summary>
        public Weapon Active { get; private set; }
        public Transform Body => transform.parent;
        /// <summary>The gun's grip position and rotation in the body's space (used by HandIK, so the hands never lag a frame).</summary>
        public Vector3 LocalPos { get; private set; }
        public Quaternion LocalRot { get; private set; } = Quaternion.identity;

        Transform shoulder;
        HandIK ik;
        bool slung;
        Stance stance;
        Vector3 shoulderLocal;
        bool haveShoulder;

        /// <summary>Where the right hand (gun grip) sits relative to the right shoulder joint, by hold style. Body space: x right, y up, z forward.</summary>
        public static Vector3 GripFromShoulder(HoldStyle h, StanceKind stance = StanceKind.Stand)
        {
            if (stance == StanceKind.Prone)
            {
                // lying down: the gun out in front at shoulder height, rested on the elbows (or the Bren's bipod)
                if (h == HoldStyle.Pistol) return new Vector3(-0.16f, -0.02f, 0.42f);
                if (h == HoldStyle.Heavy) return new Vector3(-0.12f, -0.04f, 0.40f);
                return new Vector3(-0.10f, -0.03f, 0.30f);
            }
            switch (h)
            {
                case HoldStyle.Pistol: return new Vector3(-0.17f, -0.03f, 0.47f);    // arms out, gun on the centre line at eye/shoulder height
                case HoldStyle.Heavy: return new Vector3(-0.12f, -0.40f, 0.26f);     // at the hip
                case HoldStyle.SMG: return new Vector3(-0.10f, -0.14f, 0.27f);       // butt in the shoulder, a little lower
                case HoldStyle.Launcher: return new Vector3(-0.06f, -0.04f, 0.24f);  // on the shoulder
                default: return new Vector3(-0.10f, -0.12f, 0.26f);                  // rifle shouldered
            }
        }

        /// <summary>
        /// In first-person view the gun comes up into the picture (lower right), the way an FPS shows it: from the
        /// shoulder the grip sits so low under the eyes it would be out of sight. Only for the local player's own view.
        /// </summary>
        public static Vector3 FirstPersonLift(HoldStyle h, StanceKind stance)
        {
            if (stance == StanceKind.Prone) return new Vector3(0f, 0.06f, 0.04f);
            switch (h)
            {
                case HoldStyle.Pistol: return new Vector3(0.02f, 0.04f, -0.02f);   // a little below the eye line, so the hands don't cover the ring
                case HoldStyle.Heavy: return new Vector3(0.02f, 0.36f, 0.10f);    // up from the hip to chest height
                case HoldStyle.Launcher: return new Vector3(0.02f, 0.06f, 0.06f);
                // (the hand IK then brings the gun back as far as the arms need: the left hand has to reach the barrel)
                case HoldStyle.SMG: return new Vector3(0.03f, 0.09f, 0.10f);
                default: return new Vector3(0.03f, 0.08f, 0.10f);
            }
        }

        SoldierController owner;
        Loadout kit;
        float fpBlend;

        public enum HoldGesture { None, Grenade, Bandage }
        /// <summary>What his hands do when there is no gun in them: a grenade ready to throw, or putting on a field dressing.</summary>
        public HoldGesture Gesture { get; private set; }
        /// <summary>0 in third person, 1 in the local player's first-person view (blended).</summary>
        public float FpBlend => fpBlend;
        /// <summary>The right shoulder joint in the body's space (smoothed).</summary>
        public Vector3 ShoulderLocal => shoulderLocal;
        public StanceKind StanceNow => stance ? stance.Current : StanceKind.Stand;
        /// <summary>Seconds since he last threw a grenade.</summary>
        public float ThrowAge => kit ? Time.time - kit.LastThrow : 99f;

        void Start()
        {
            if (!animator && Body) animator = Body.GetComponentInChildren<Animator>();
            if (animator && animator.isHuman)
            {
                shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                if (!animator.TryGetComponent<HandIK>(out var ik)) ik = animator.gameObject.AddComponent<HandIK>();
                ik.holder = this;
                this.ik = ik;
            }
        }

        void LateUpdate()
        {
            Transform body = Body;
            if (!body) return;
            Active = GetComponentInChildren<Weapon>();
            // swimming: the gun is slung on his back (out of sight, hands free for the stroke)
            bool swim = Water.Swimming(body.position, out _);
            if (!owner) body.TryGetComponent(out owner);
            if (!kit) body.TryGetComponent(out kit);
            // putting on a field dressing: the gun goes over his shoulder too (both hands are busy)
            bool bandage = kit && kit.Bandaging;
            bool stow = swim || bandage;
            if (stow != slung)
            {
                slung = stow;
                foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = !stow;
            }
            if (stow) Active = null;
            bool grenade = !stow && !Active && owner && owner.enabled && owner.WeaponIndex == 2 && kit && (kit.NextGrenade || ThrowAge < 0.6f);
            Gesture = swim ? HoldGesture.None : bandage ? HoldGesture.Bandage : grenade ? HoldGesture.Grenade : HoldGesture.None;
            HoldStyle style = Active ? Active.hold : HoldStyle.Rifle;
            if (!stance) body.TryGetComponent(out stance);
            StanceKind sk = stance ? stance.Current : StanceKind.Stand;

            // the gun points the way the body faces, tilted up/down with the aim (limited), so it can't spin in the hand
            bool fp = owner && owner.enabled && SoldierController.FirstPerson;
            fpBlend = Mathf.MoveTowards(fpBlend, fp ? 1f : 0f, Time.deltaTime * 6f);
            float limit = fp ? (sk == StanceKind.Prone ? 20f : 70f) : sk == StanceKind.Prone ? Mathf.Min(maxPitch, 15f) : style == HoldStyle.Heavy && sk == StanceKind.Stand ? Mathf.Min(maxPitch, 12f) : maxPitch;
            // kneeling, the Bren and MG 42 come up to the shoulder instead of the hip
            if (style == HoldStyle.Heavy && sk == StanceKind.Crouch) style = HoldStyle.Rifle;
            float target = 0f;
            if (aimSource)
            {
                Vector3 f = aimSource.forward;
                target = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg, -limit, limit);
            }
            pitch = Mathf.Lerp(pitch, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
            Quaternion tilt = Quaternion.Euler(pitch, 0f, 0f);

            // the shoulder joint as animated (crouching, breathing), in body space, smoothed so the gun doesn't jitter
            Vector3 sh = shoulder ? body.InverseTransformPoint(shoulder.position) : new Vector3(0.18f, 1.42f, 0f);
            shoulderLocal = haveShoulder ? Vector3.Lerp(shoulderLocal, sh, 1f - Mathf.Exp(-15f * Time.deltaTime)) : sh;
            haveShoulder = true;

            // tilting with the aim pivots around the shoulder, so the arms come up with the gun
            LocalPos = shoulderLocal + tilt * (GripFromShoulder(style, sk) + FirstPersonLift(style, sk) * fpBlend);
            LocalRot = tilt;
            // first person: the gun lines up on what the eye looks at (a point well out in front), so the barrel
            // converges on the crosshair instead of lying across the view
            if (fpBlend > 0f && aimSource)
            {
                Vector3 grip = body.TransformPoint(LocalPos);
                Vector3 aimPoint = aimSource.position + aimSource.forward * 25f;
                Quaternion look = Quaternion.Inverse(body.rotation) * Quaternion.LookRotation(aimPoint - grip, body.up);
                Vector3 e = look.eulerAngles;   // never further up or down than the hold allows (lying down: hardly at all)
                look = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), -limit, limit), Mathf.DeltaAngle(0f, e.y), 0f);
                LocalRot = Quaternion.Slerp(tilt, look, fpBlend);
            }
            // the arms are only so long: bring the gun back until both hands reach it with the elbows bent
            if (ik && Active)
            {
                Vector3 p = LocalPos;
                ik.FitGun(Active, style, ref p, LocalRot);
                LocalPos = p;
            }
            transform.SetPositionAndRotation(body.TransformPoint(LocalPos), body.rotation * LocalRot);
            GrenadeInHand();
        }

        // ------------------------------------------------------------------ the grenade in his right fist

        GameObject heldGrenade; Shell heldFrom;
        Transform rightHand, rightKnuckles;

        void GrenadeInHand()
        {
            bool show = Gesture == HoldGesture.Grenade && kit && kit.NextGrenade && ThrowAge > 0.3f && animator && animator.isHuman;
            if (show && (!heldGrenade || heldFrom != kit.NextGrenade))
            {
                if (heldGrenade) Destroy(heldGrenade);
                heldFrom = kit.NextGrenade;
                heldGrenade = VisualCopy(heldFrom.gameObject);
            }
            if (!heldGrenade) return;
            if (heldGrenade.activeSelf != show) heldGrenade.SetActive(show);
            if (!show) return;
            if (ik && ik.HasGrenadePoint)
            {
                // on the palm, where the hand IK has cupped it
                heldGrenade.transform.SetPositionAndRotation(ik.GrenadePoint, ik.GrenadeRotation);
                return;
            }
            if (!rightHand) { rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand); rightKnuckles = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal); }
            if (!rightHand) return;
            // in the palm: most of the way from the wrist to the knuckles
            // held up in the fingers, so it shows over the fist
            Vector3 p = rightKnuckles ? Vector3.Lerp(rightHand.position, rightKnuckles.position, 0.9f) : rightHand.position;
            heldGrenade.transform.SetPositionAndRotation(p + Body.up * 0.035f + Body.forward * 0.01f, rightHand.rotation);
        }

        /// <summary>A copy of a prefab that only draws: no scripts, physics, sounds or effects.</summary>
        static GameObject VisualCopy(GameObject prefab)
        {
            var holderGo = new GameObject("held grenade");
            holderGo.SetActive(false);
            var copy = Instantiate(prefab, holderGo.transform);
            foreach (var r in copy.GetComponentsInChildren<ParticleSystemRenderer>(true)) DestroyImmediate(r);
            foreach (var c in copy.GetComponentsInChildren<Component>(true))
                if (c is MonoBehaviour || c is AudioSource || c is ParticleSystem || c is Light || c is TrailRenderer) DestroyImmediate(c);
            foreach (var c in copy.GetComponentsInChildren<Joint>(true)) DestroyImmediate(c);
            foreach (var c in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(c);
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            holderGo.SetActive(true);
            return holderGo;
        }

        void OnDestroy() { if (heldGrenade) Destroy(heldGrenade); }

        void OnDisable()
        {
            if (heldGrenade) heldGrenade.SetActive(false);
            // drop the weapon when its owner dies
            if (!Application.isPlaying || !transform.parent) return;
            var health = GetComponentInParent<Health>();
            if (health && health.IsDead)
            {
                transform.SetParent(null, true);
                var rb = gameObject.AddComponent<Rigidbody>(); rb.mass = 4f;
                var col = gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(0.06f, 0.12f, 0.9f); col.center = new Vector3(0f, 0f, 0.3f);
                // anyone can pick it up (E); it comes with what was left in the dead man's pouches
                var w = GetComponentInChildren<Weapon>() ?? GetComponentInChildren<Weapon>(true);   // the one in his hands
                if (w)
                {
                    w.SetAmmo(w.Ammo, Mathf.Min(w.SpareRounds, w.magazineSize * 2));
                    var p = gameObject.AddComponent<Pickup>(); p.kind = Pickup.Kind.Weapon; p.weapon = w;
                }
            }
        }
    }
}
