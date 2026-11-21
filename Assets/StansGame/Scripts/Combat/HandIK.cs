using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Puts the soldier's hands on the weapon he carries (Unity humanoid IK, needs "IK Pass" on the animator layer).
    ///
    /// The right hand always closes round the grip (palm against its right side, knuckles towards the trigger); the left
    /// hand cradles the gun from underneath, palm up, with the barrel / forestock / sleeve resting in it — or, with a
    /// pistol, cups the right fist; and goes to the magazine while reloading.
    ///
    /// The arms are measured from the skeleton (shoulder to elbow to wrist) and the gun is never put where a hand can't
    /// reach it: if the hold would need a longer arm, the whole gun comes back towards the shoulders until both hands
    /// reach with the elbows still bent (<see cref="FitGun"/>). So an arm is never pulled out straight and stretched.
    /// The palm, not the wrist, is put on the gun (measured from the hand's own knuckle joints, so it works for any rig).
    /// Added automatically by WeaponHolder.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class HandIK : MonoBehaviour
    {
        public WeaponHolder holder;

        [Tooltip("The furthest a hand may be from its shoulder, as a fraction of the arm's length (1 = arm locked straight).")]
        public float maxReach = 0.92f;
        [Tooltip("The closest a hand may come to its shoulder (fraction of arm length), so the forearm doesn't fold into the upper arm.")]
        public float minReach = 0.42f;
        [Tooltip("With a shouldered long gun the chest turns this far to the right (degrees), bringing the left shoulder forward under the gun - the bladed stance. The head turns back so he still looks ahead.")]
        public float blade = 25f;

        Animator anim;
        float wRight, wLeft;
        int startup, lastIkFrame = -100;
        bool snapTwist;

        // ------------------------------------------------------------------ the arms, measured once from the skeleton
        Transform rUpper, rLower, rHand, lUpper, lLower, lHand;
        float reachR = 0.55f, reachL = 0.55f;
        // per hand, in the hand bone's own space: the direction wrist -> middle knuckle, the palm's normal (the way the
        // palm faces) and the wrist -> palm-centre offset
        Vector3 rFingers, rPalm, rPalmAt, lFingers, lPalm, lPalmAt;
        bool frames;
        // humanoid IK goals are not exactly the hand bones' rotations: the fixed difference, measured while IK is off
        Quaternion rGoalOff = Quaternion.identity, lGoalOff = Quaternion.identity;
        bool haveGoalOff;

        // ------------------------------------------------------------------ the current solution (body space)
        Vector3 rShB, lShB; bool haveSh;
        Vector3 rWristB, lWristB; Quaternion rRotB = Quaternion.identity, lRotB = Quaternion.identity;
        bool solved;
        Vector3 leftLocal; bool haveLeft;   // smoothed left-hand point in gun space (slides to the magazine and back)
        Vector3 gestureR, gestureL; bool haveGesture;
        // the bladed stance: the chest turned, the neck turned back (local deltas applied last frame, so they can be undone)
        Transform chest, neck;
        HumanBodyBones chestBone = HumanBodyBones.Chest, neckBone = HumanBodyBones.Neck;
        Quaternion chestDelta = Quaternion.identity, neckDelta = Quaternion.identity;
        float twist;

        /// <summary>Where a grenade held ready sits in the right palm (world), valid while <see cref="HasGrenadePoint"/>.</summary>
        public Vector3 GrenadePoint { get; private set; }
        public Quaternion GrenadeRotation { get; private set; }
        int grenadeFrame = -10;
        public bool HasGrenadePoint => Time.frameCount - grenadeFrame <= 1;

        /// <summary>The length of each arm (shoulder joint to wrist), metres.</summary>
        public float ReachRight => reachR;
        public float ReachLeft => reachL;
        /// <summary>The shoulders in the body's space (smoothed), as the solver uses them.</summary>
        public Vector3 RightShoulder => rShB;
        public Vector3 LeftShoulder => lShB;

        void Awake()
        {
            anim = GetComponent<Animator>();
            Measure();
        }

        void Measure()
        {
            if (!anim || !anim.isHuman) return;
            rUpper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rLower = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            lUpper = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            lLower = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            chestBone = anim.GetBoneTransform(HumanBodyBones.Chest) ? HumanBodyBones.Chest : HumanBodyBones.Spine;
            neckBone = anim.GetBoneTransform(HumanBodyBones.Neck) ? HumanBodyBones.Neck : HumanBodyBones.Head;
            chest = anim.GetBoneTransform(chestBone);
            neck = anim.GetBoneTransform(neckBone);
            if (!rUpper || !rLower || !rHand || !lUpper || !lLower || !lHand) return;
            float s = Mathf.Max(0.01f, transform.lossyScale.y);
            reachR = (Vector3.Distance(rUpper.position, rLower.position) + Vector3.Distance(rLower.position, rHand.position)) / s;
            reachL = (Vector3.Distance(lUpper.position, lLower.position) + Vector3.Distance(lLower.position, lHand.position)) / s;
            frames = HandFrame(rHand, true, out rFingers, out rPalm, out rPalmAt) & HandFrame(lHand, false, out lFingers, out lPalm, out lPalmAt);
            if (!frames)
            {
                // no finger bones in this rig: work in Unity's humanoid goal space instead, where a hand goal's
                // axes are the same for every rig: +Z runs along the hand to the fingers and -Y is the way the palm
                // faces, for both hands (measured from the aim clip: right palm against the grip, left palm up under
                // the forestock)
                goalSpace = true;
                rFingers = Vector3.forward; rPalm = Vector3.down;
                lFingers = Vector3.forward; lPalm = Vector3.down;
                rPalmAt = rFingers * 0.05f + rPalm * 0.02f;
                lPalmAt = lFingers * 0.05f + lPalm * 0.02f;
                frames = true;
            }
        }

        bool goalSpace;
        float goalCheckR = -1f, goalCheckL = -1f;
        Vector3 goalForearmR, goalForearmL; string goalAxesR = "", goalAxesL = "";   // degrees between the goal's finger direction and the forearm, measured once

        /// <summary>The hand's layout from its knuckle joints (index, middle, little proximal), in the hand bone's space.</summary>
        bool HandFrame(Transform hand, bool right, out Vector3 fingers, out Vector3 palm, out Vector3 palmAt)
        {
            fingers = Vector3.forward; palm = Vector3.down; palmAt = Vector3.zero;
            var index = anim.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            var middle = anim.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            var little = anim.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal)
                      ?? anim.GetBoneTransform(right ? HumanBodyBones.RightRingProximal : HumanBodyBones.LeftRingProximal);
            if (!index || !middle || !little) return false;
            Quaternion inv = Quaternion.Inverse(hand.rotation);              // the bone's frame, world lengths
            Vector3 f = inv * (middle.position - hand.position);
            Vector3 across = inv * (index.position - little.position);       // little finger -> index (towards the thumb)
            fingers = f.normalized;
            // right hand, palm down, fingers forward: the thumb side is to the left, so fingers x across = down = palm
            palm = (right ? Vector3.Cross(fingers, across) : Vector3.Cross(across, fingers)).normalized;
            if (palm.sqrMagnitude < 0.5f) return false;
            // the middle of the palm: a little over half way to the knuckles, on the palm's surface
            palmAt = f * 0.55f + palm * 0.02f;                               // metres, in the bone's frame
            return true;
        }

        // ------------------------------------------------------------------ how each hand sits on the gun (gun space: x right, y up, z to the muzzle)

        /// <summary>Right hand: the palm against the right side of the grip, knuckles forward towards the trigger.</summary>
        static void RightGrip(HoldStyle h, out Vector3 fingers, out Vector3 palm, out float radius)
        {
            palm = new Vector3(-1f, 0f, 0f);
            // a pistol's grip rakes back, so the hand rides up behind the slide; a rifle's wrist / pistol grip is steeper
            fingers = h == HoldStyle.Pistol ? new Vector3(0f, 0.30f, 1f) : new Vector3(0f, 0.15f, 1f);
            radius = h == HoldStyle.Pistol ? 0.016f : 0.02f;
        }

        /// <summary>Left hand: under the barrel / forestock, palm up and fingers round the right side; with a pistol, cupping the right fist.</summary>
        static void LeftGrip(HoldStyle h, bool reloading, out Vector3 fingers, out Vector3 palm, out float radius)
        {
            if (h == HoldStyle.Pistol && !reloading)
            {
                palm = new Vector3(0.75f, 0.55f, 0f);       // facing up and in, under and round the right fingers
                fingers = new Vector3(0.45f, 0.15f, 0.85f);
                radius = 0.035f;                              // round the right fist, not the grip
                return;
            }
            if (reloading)
            {
                palm = new Vector3(0.6f, 0.4f, 0f);          // on the magazine / action from the left
                fingers = new Vector3(0.2f, -0.1f, 1f);
                radius = 0.02f;
                return;
            }
            if (h == HoldStyle.Launcher)
            {
                palm = new Vector3(1f, 0f, 0f);              // a front grip: closed round it from the left, like the right hand
                fingers = new Vector3(0f, 0.35f, 1f);
                radius = 0.02f;
                return;
            }
            palm = new Vector3(0.25f, 1f, 0f);                // the barrel resting in an upturned palm
            fingers = new Vector3(0.65f, 0.05f, 0.75f);       // fingers forward and round the far side
            radius = 0.024f;
        }

        Quaternion HandRot(Quaternion gunRotB, Vector3 fingersGun, Vector3 palmGun, Vector3 fingersLocal, Vector3 palmLocal)
        {
            Quaternion want = Quaternion.LookRotation(gunRotB * fingersGun, gunRotB * palmGun);
            Quaternion have = Quaternion.LookRotation(fingersLocal, palmLocal);
            return want * Quaternion.Inverse(have);   // hand bone rotation in body space
        }

        // ------------------------------------------------------------------ fitting the gun to the arms

        void Shoulders(Transform body)
        {
            if (!rUpper || !lUpper) return;
            Vector3 r = body.InverseTransformPoint(rUpper.position), l = body.InverseTransformPoint(lUpper.position);
            float k = haveSh ? 1f - Mathf.Exp(-15f * Time.deltaTime) : 1f;
            rShB = Vector3.Lerp(rShB, r, k); lShB = Vector3.Lerp(lShB, l, k);
            haveSh = true;
        }

        /// <summary>
        /// Called by WeaponHolder each frame with where it wants the gun (grip position and rotation, body space). Works
        /// out both hands on it, and moves the gun (never turns it) until both hands reach it with bent elbows.
        /// </summary>
        public void FitGun(Weapon w, HoldStyle style, ref Vector3 gripB, Quaternion rotB)
        {
            solved = false;
            if (!frames || !w || !holder || !holder.Body) return;
            Shoulders(holder.Body);
            float scale = Mathf.Max(0.01f, transform.lossyScale.y) / Mathf.Max(0.01f, holder.Body.lossyScale.y);
            float rMax = reachR * scale * maxReach, lMax = reachL * scale * Mathf.Min(0.95f, maxReach);
            float rMin = reachR * scale * minReach, lMin = reachL * scale * minReach * 0.8f;

            bool reloading = w.IsReloading;
            Vector3 target = reloading ? w.magazinePoint : w.supportHand;
            leftLocal = haveLeft ? Vector3.Lerp(leftLocal, target, 1f - Mathf.Exp(-9f * Time.deltaTime)) : target;
            haveLeft = true;

            RightGrip(style, out var rf, out var rp, out float rr);
            LeftGrip(style, reloading, out var lf, out var lp, out float lr);
            rRotB = HandRot(rotB, rf, rp, rFingers, rPalm);
            lRotB = HandRot(rotB, lf, lp, lFingers, lPalm);
            // the hand bone (wrist) sits back from the palm, and the palm sits on the surface, not the centre line
            Vector3 rBack = -(rotB * rp.normalized) * rr - rRotB * rPalmAt;
            Vector3 lBack = -(rotB * lp.normalized) * lr - lRotB * lPalmAt;
            Vector3 handle = w.handOffset;      // the middle of the grip, in gun space (from the model's pivot, as is the left point)
            // a shouldered gun's stock must stay in front of the chest, not run through it
            bool shouldered = style == HoldStyle.Rifle || style == HoldStyle.SMG || style == HoldStyle.Launcher;
            float stock = shouldered ? StockEnd(w) : 0f;
            float chestFront = (rShB.z + lShB.z) * 0.5f + 0.11f;   // the front of the chest, a hand in front of the shoulder joints
            float chestX = (rShB.x + lShB.x) * 0.5f, chestHalf = Mathf.Abs(rShB.x - lShB.x) * 0.5f + 0.05f;
            float chestTop = Mathf.Max(rShB.y, lShB.y) + 0.1f, chestBottom = Mathf.Min(rShB.y, lShB.y) - 0.6f;
            // the left hand slides back along the barrel before the gun is pulled in
            float sz = leftLocal.z, szMin = Mathf.Min(leftLocal.z, handle.z + 0.13f);
            bool canSlide = !reloading && style != HoldStyle.Pistol;
            // first the stock and the arms together; the last passes only the arms, so a hand is never pulled off the gun
            // or an arm stretched (the arms on these models are short - where they can't both be had, the hands win)
            for (int i = 0; i < 18; i++)
            {
                bool torso = i < 12;
                rWristB = gripB + rotB * handle + rBack;
                lWristB = gripB + rotB * new Vector3(leftLocal.x, leftLocal.y, sz) + lBack;
                Vector3 moved = Vector3.zero;
                Vector3 dr = rWristB - rShB, dl = lWristB - lShB;
                if (dr.magnitude > rMax) moved += -dr.normalized * (dr.magnitude - rMax);
                else if (dr.magnitude < rMin) moved += dr.normalized * (rMin - dr.magnitude);
                bool slid = false;
                if (dl.magnitude > lMax)
                {
                    if (canSlide && sz > szMin + 0.002f) { sz = Mathf.Max(szMin, sz - (dl.magnitude - lMax) * 1.3f); slid = true; }
                    else moved += -dl.normalized * (dl.magnitude - lMax);
                }
                else if (dl.magnitude < lMin) moved += dl.normalized * (lMin - dl.magnitude);
                if (torso && shouldered && stock < -0.05f)
                {
                    // the butt and the middle of the stock: if inside the chest, the gun goes forward - or out to the
                    // right, under the arm, whichever is the shorter way out
                    float pushZ = 0f, pushX = 0f;
                    foreach (float k in new[] { stock, stock * 0.5f })
                    {
                        Vector3 p = gripB + rotB * new Vector3(0f, 0f, k);
                        if (Mathf.Abs(p.x - chestX) < chestHalf && p.y < chestTop && p.y > chestBottom && p.z < chestFront)
                        {
                            pushZ = Mathf.Max(pushZ, chestFront - p.z);
                            pushX = Mathf.Max(pushX, chestX + chestHalf - p.x);
                        }
                    }
                    if (pushZ > 0f) { if (pushX < pushZ * 1.3f) moved.x += pushX * 0.5f; else moved.z += pushZ * 0.5f; }
                }
                if (moved.sqrMagnitude < 1e-8f && !slid && !torso) break;
                gripB += moved * (i < 15 ? 0.9f : 1f);
            }
            solved = true;
        }

        static readonly System.Collections.Generic.Dictionary<Weapon, float> stocks = new System.Collections.Generic.Dictionary<Weapon, float>();

        /// <summary>How far behind the pivot the gun's butt reaches (gun space z, negative), from its meshes.</summary>
        static float StockEnd(Weapon w)
        {
            if (stocks.TryGetValue(w, out float z)) return z;
            z = 0f;
            foreach (var r in w.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var b = r.localBounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    float lz = w.transform.InverseTransformPoint(r.transform.TransformPoint(corner)).z;
                    if (lz < z) z = lz;
                }
            }
            stocks[w] = z;
            return z;
        }

        // ------------------------------------------------------------------ applying it

        void OnAnimatorIK(int layerIndex)
        {
            if (!anim || !anim.isHuman) return;
            startup++;
            // the first frames: no IK, so the goal-vs-bone rotation difference can be measured cleanly
            if (startup < 4 || !haveGoalOff)
            {
                // (lastIkFrame stays old, so the first real pass puts the hands straight onto the gun)
                if (goalSpace && startup >= 2)
                {
                    // check the goal-space assumption: the goal's "fingers" should run on from the forearm
                    Quaternion gr = anim.GetIKRotation(AvatarIKGoal.RightHand), gl = anim.GetIKRotation(AvatarIKGoal.LeftHand);
                    goalCheckR = Vector3.Angle(gr * rFingers, rHand.position - rLower.position);
                    goalCheckL = Vector3.Angle(gl * lFingers, lHand.position - lLower.position);
                    goalForearmR = (Quaternion.Inverse(gr) * (rHand.position - rLower.position)).normalized;
                    goalForearmL = (Quaternion.Inverse(gl) * (lHand.position - lLower.position)).normalized;
                    Transform b = holder && holder.Body ? holder.Body : transform;
                    Quaternion ib = Quaternion.Inverse(b.rotation);
                    goalAxesR = $"x->{ib * (gr * Vector3.right):F2} y->{ib * (gr * Vector3.up):F2} z->{ib * (gr * Vector3.forward):F2}";
                    goalAxesL = $"x->{ib * (gl * Vector3.right):F2} y->{ib * (gl * Vector3.up):F2} z->{ib * (gl * Vector3.forward):F2}";
                    if (startup >= 3) haveGoalOff = true;
                }
                else if (frames && startup >= 2)
                {
                    rGoalOff = Quaternion.Inverse(rHand.rotation) * anim.GetIKRotation(AvatarIKGoal.RightHand);
                    lGoalOff = Quaternion.Inverse(lHand.rotation) * anim.GetIKRotation(AvatarIKGoal.LeftHand);
                    if (startup >= 3) haveGoalOff = true;
                }
                Off();
                return;
            }

            var w = holder && holder.isActiveAndEnabled ? holder.Active : null;
            var gesture = holder && holder.isActiveAndEnabled && !w ? holder.Gesture : WeaponHolder.HoldGesture.None;
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            float on = w || gesture != WeaponHolder.HoldGesture.None ? 1f : 0f;
            // a soldier whose animation was switched off while out of sight comes back with his hands already on
            // the gun, not sliding onto it
            bool wasAway = Time.frameCount - lastIkFrame > 3;
            lastIkFrame = Time.frameCount;
            wRight = wasAway ? on : Mathf.Lerp(wRight, on, k);
            wLeft = wasAway ? on : Mathf.Lerp(wLeft, on, k);
            if (wasAway) snapTwist = true;
            bool longGun = w && w.hold != HoldStyle.Pistol;
            Blade(longGun ? blade * (w.hold == HoldStyle.Heavy ? 0.8f : 1f) * wRight : 0f);
            if (wRight < 0.01f) { Off(); return; }

            if (gesture != WeaponHolder.HoldGesture.None) { Gesture(gesture); return; }
            if (!w) { Off(); return; }

            Transform body = holder.Body;
            if (solved && frames)
            {
                anim.SetIKPosition(AvatarIKGoal.RightHand, body.TransformPoint(rWristB));
                anim.SetIKRotation(AvatarIKGoal.RightHand, body.rotation * rRotB * rGoalOff);
                anim.SetIKPosition(AvatarIKGoal.LeftHand, body.TransformPoint(lWristB));
                anim.SetIKRotation(AvatarIKGoal.LeftHand, body.rotation * lRotB * lGoalOff);
                anim.SetIKPositionWeight(AvatarIKGoal.RightHand, wRight);
                anim.SetIKRotationWeight(AvatarIKGoal.RightHand, wRight);
                anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, wLeft);
                anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, wLeft);
            }
            else
            {
                // a rig without finger bones: wrists on the grip and the support point, rotations from the clip
                Quaternion gunRot = body.rotation * holder.LocalRot;
                Vector3 grip = body.TransformPoint(holder.LocalPos) + gunRot * w.handOffset;
                Quaternion tilt = gunRot * Quaternion.Inverse(body.rotation);
                Vector3 target = w.IsReloading ? w.magazinePoint : w.supportHand;
                anim.SetIKPosition(AvatarIKGoal.RightHand, grip);
                anim.SetIKRotation(AvatarIKGoal.RightHand, tilt * anim.GetIKRotation(AvatarIKGoal.RightHand));
                anim.SetIKPosition(AvatarIKGoal.LeftHand, grip + gunRot * target);
                anim.SetIKRotation(AvatarIKGoal.LeftHand, tilt * anim.GetIKRotation(AvatarIKGoal.LeftHand));
                anim.SetIKPositionWeight(AvatarIKGoal.RightHand, wRight);
                anim.SetIKRotationWeight(AvatarIKGoal.RightHand, wRight * 0.8f);
                anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, wLeft);
                anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, wLeft * 0.6f);
            }

            // elbows: a rifle's right elbow out to the side and down, the left elbow down under the gun;
            // a pistol's both down and a little in (arms out straight-ish in front)
            bool pistol = w.hold == HoldStyle.Pistol;
            if (rUpper && lUpper)
            {
                anim.SetIKHintPosition(AvatarIKHint.RightElbow, rUpper.position + body.rotation * (pistol ? new Vector3(0.15f, -0.30f, 0.10f) : new Vector3(0.32f, -0.32f, -0.05f)));
                anim.SetIKHintPosition(AvatarIKHint.LeftElbow, lUpper.position + body.rotation * (pistol ? new Vector3(-0.15f, -0.30f, 0.10f) : new Vector3(-0.05f, -0.40f, 0.18f)));
                anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0.6f * wRight);
                anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0.6f * wLeft);
            }
        }

        /// <summary>
        /// Turns the chest about the body's up axis (positive: right shoulder back, left shoulder forward) and the neck
        /// back the other way. The bones still hold last frame's result, so last frame's turn is taken off first.
        /// </summary>
        void Blade(float target)
        {
            twist = snapTwist ? target : Mathf.Lerp(twist, target, 1f - Mathf.Exp(-8f * Time.deltaTime));
            snapTwist = false;
            if (!chest || !holder || !holder.Body) return;
            if (Mathf.Abs(twist) < 0.05f && chestDelta == Quaternion.identity) return;
            Vector3 up = holder.Body.up;
            Quaternion turn = Quaternion.AngleAxis(twist, up);
            Quaternion baseChest = Quaternion.Inverse(chestDelta) * chest.localRotation;
            Quaternion pc = chest.parent ? chest.parent.rotation : Quaternion.identity;
            chestDelta = Quaternion.Inverse(pc) * turn * pc;
            anim.SetBoneLocalRotation(chestBone, chestDelta * baseChest);
            if (neck && neck != chest)
            {
                Quaternion baseNeck = Quaternion.Inverse(neckDelta) * neck.localRotation;
                Quaternion pn = neck.parent ? neck.parent.rotation : Quaternion.identity;
                // the neck's parent has (from last frame) already been turned with the chest
                neckDelta = Quaternion.Inverse(pn) * Quaternion.Inverse(turn) * pn;
                anim.SetBoneLocalRotation(neckBone, neckDelta * baseNeck);
            }
            if (Mathf.Abs(twist) < 0.05f) { chestDelta = Quaternion.identity; neckDelta = Quaternion.identity; }
        }

        void Off()
        {
            foreach (var g in new[] { AvatarIKGoal.RightHand, AvatarIKGoal.LeftHand }) { anim.SetIKPositionWeight(g, 0f); anim.SetIKRotationWeight(g, 0f); }
            anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
            anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
        }

        /// <summary>A point for a hand, pulled in to within the arm's reach of its shoulder (body space).</summary>
        Vector3 Reachable(Vector3 p, Vector3 shoulder, float reach)
        {
            Vector3 d = p - shoulder;
            float max = reach * maxReach;
            return d.magnitude > max ? shoulder + d.normalized * max : p;
        }

        /// <summary>
        /// Hands without a gun: a grenade held ready in front of the right shoulder with the left arm out for balance
        /// (and the arm whipping over and through after a throw), or a field dressing wound round the left forearm.
        /// Raised a little in first person so the player sees them. Never further than the arm reaches.
        /// </summary>
        void Gesture(WeaponHolder.HoldGesture g)
        {
            Transform body = holder.Body;
            Shoulders(body);
            float fp = holder.FpBlend;
            bool prone = holder.StanceNow == StanceKind.Prone;
            Vector3 sh = haveSh ? rShB : holder.ShoulderLocal;      // right shoulder, body space
            Vector3 lsh = haveSh ? lShB : new Vector3(-sh.x, sh.y, sh.z);
            Vector3 r, l;
            if (g == WeaponHolder.HoldGesture.Grenade)
            {
                r = sh + Vector3.Lerp(new Vector3(0.02f, -0.10f, 0.28f), new Vector3(-0.06f, -0.01f, 0.32f), fp);
                l = lsh + Vector3.Lerp(new Vector3(0.08f, -0.14f, 0.30f), new Vector3(0.06f, -0.20f, 0.30f), fp);
                if (prone) { r.y = sh.y + 0.02f; l.y = lsh.y - 0.02f; }
                float t = holder.ThrowAge;
                if (t < 0.6f)
                {
                    // the follow-through: the arm comes over from behind the head, out in front, then back to ready
                    Vector3 back = sh + new Vector3(0.02f, 0.28f, -0.12f), through = sh + new Vector3(-0.10f, 0.10f, 0.50f);
                    r = t < 0.12f ? Vector3.Lerp(back, through, t / 0.12f) : Vector3.Lerp(through, r, Mathf.SmoothStep(0f, 1f, (t - 0.12f) / 0.48f));
                }
            }
            else
            {
                // left forearm across the body at belly / chest height, right hand winding the bandage round it
                float y = prone ? sh.y - 0.10f : Mathf.Lerp(sh.y - 0.32f, sh.y - 0.08f, fp);
                float z = Mathf.Lerp(0.28f, 0.38f, fp);
                l = new Vector3(0.04f, y, z);
                float a = Time.time * 7f;
                r = l + new Vector3(0.08f + 0.05f * Mathf.Cos(a), 0.05f * Mathf.Sin(a), -0.02f + 0.03f * Mathf.Sin(a));
            }
            float scale = Mathf.Max(0.01f, transform.lossyScale.y) / Mathf.Max(0.01f, body.lossyScale.y);
            r = Reachable(r, sh, reachR * scale);
            l = Reachable(l, lsh, reachL * scale);
            gestureR = r; gestureL = l; haveGesture = true;

            anim.SetIKPosition(AvatarIKGoal.RightHand, body.TransformPoint(r));
            anim.SetIKPositionWeight(AvatarIKGoal.RightHand, wRight);
            anim.SetIKPosition(AvatarIKGoal.LeftHand, body.TransformPoint(l));
            anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, wLeft);
            if (frames && g == WeaponHolder.HoldGesture.Grenade && holder.ThrowAge > 0.6f)
            {
                // the grenade cupped in the right hand, palm up and in, fingers (and lever) towards the sky; the left
                // hand out in front, palm down, pointing at the target
                Quaternion rr = HandRot(Quaternion.identity, new Vector3(-0.15f, 0.65f, 0.75f), new Vector3(-0.75f, 0.45f, -0.3f), rFingers, rPalm);
                Quaternion lr = HandRot(Quaternion.identity, new Vector3(0.15f, 0.05f, 1f), new Vector3(0.35f, -1f, 0f), lFingers, lPalm);
                anim.SetIKRotation(AvatarIKGoal.RightHand, body.rotation * rr * rGoalOff);
                // where the grenade sits: on the palm (WeaponHolder puts the grenade model there)
                Quaternion rw = body.rotation * rr;
                GrenadePoint = body.TransformPoint(r) + rw * rPalmAt + rw * rPalm * 0.03f;
                GrenadeRotation = rw;
                grenadeFrame = Time.frameCount;
                anim.SetIKRotation(AvatarIKGoal.LeftHand, body.rotation * lr * lGoalOff);
                anim.SetIKRotationWeight(AvatarIKGoal.RightHand, wRight);
                anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, wLeft);
            }
            else
            {
                anim.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            }

            // elbows down and out
            if (rUpper && lUpper)
            {
                anim.SetIKHintPosition(AvatarIKHint.RightElbow, rUpper.position + body.rotation * new Vector3(0.3f, -0.3f, -0.05f));
                anim.SetIKHintPosition(AvatarIKHint.LeftElbow, lUpper.position + body.rotation * new Vector3(-0.3f, -0.3f, 0f));
                anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0.5f * wRight);
                anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0.5f * wLeft);
            }
        }

        // ------------------------------------------------------------------ checking it (Stan's Game > Debug > Capture Hand Poses)

        /// <summary>
        /// How the arms are doing right now: each hand's distance from its shoulder as a share of the arm's length
        /// (over ~0.97 means the arm is locked straight), and how far the hand bone ended up from where it should be.
        /// </summary>
        public string Report()
        {
            if (!frames) return "no hand bones - fallback IK";
            Transform body = holder ? holder.Body : transform;
            float scale = Mathf.Max(0.01f, transform.lossyScale.y);
            string Hand(string name, Transform upper, Transform hand, Vector3 targetB, float reach)
            {
                Vector3 target = body.TransformPoint(targetB);
                float need = Vector3.Distance(upper.position, target) / scale / reach;
                float now = Vector3.Distance(upper.position, hand.position) / scale / reach;
                float miss = Vector3.Distance(hand.position, target) * 100f;
                return $"{name}: target at {need:0.00} of arm, hand at {now:0.00}, off by {miss:0.0} cm";
            }
            var w = holder ? holder.Active : null;
            string extra = "";
            if (goalSpace) extra = $"\n  goal-space forearm R {goalForearmR:F2} L {goalForearmL:F2}\n  goal axes in body space (clip pose) R {goalAxesR}\n                                  L {goalAxesL}";
            extra += $"\n  scale anim {transform.lossyScale.y:0.000} body {body.lossyScale.y:0.000}, chest turned {twist:0} deg, hand weights {wRight:0.00}/{wLeft:0.00}";
            if (w) extra += $"\n  right hand bone to the gun's grip {Vector3.Distance(rHand.position, w.transform.TransformPoint(w.handOffset)) * 100f:0.0} cm, gun pivot vs holder {Vector3.Distance(w.transform.position, holder.transform.position) * 100f:0.0} cm";
            if (w && solved)
                return $"{w.displayName} ({w.hold}), arms {reachR:0.00} / {reachL:0.00} m" + (goalSpace ? $", goal space (check {goalCheckR:0} / {goalCheckL:0} deg)" : ", finger frames") + "\n  " + Hand("right", rUpper, rHand, rWristB, reachR) + "\n  " + Hand("left ", lUpper, lHand, lWristB, reachL) + extra;
            if (haveGesture && holder && holder.Gesture != WeaponHolder.HoldGesture.None)
                return $"{holder.Gesture} gesture\n  " + Hand("right", rUpper, rHand, gestureR, reachR) + "\n  " + Hand("left ", lUpper, lHand, gestureL, reachL);
            return "hands free";
        }
    }
}
