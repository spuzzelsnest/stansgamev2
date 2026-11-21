using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Makes humanoid crouch and prone animations (the old Hero set has none): poses are built by bending the bones
    /// of a humanoid model in world space (knees, hips, spine, neck), read back as muscle values with a
    /// HumanPoseHandler and saved as humanoid clips, so they play on every soldier (German and British alike).
    ///   CrouchIdle, CrouchWalk (loop), ProneIdle, ProneCrawl (loop).
    /// The arms keep the standing aim pose; HandIK puts the hands on the gun in every stance.
    /// </summary>
    public static class StanceClips
    {
        const string Dir = "Assets/StansGame/Animations/Generated";

        public static Dictionary<string, AnimationClip> Build(GameObject modelAsset, AnimationClip standRef)
        {
            var result = new Dictionary<string, AnimationClip>();
            if (!modelAsset) return result;
            var inst = (GameObject)Object.Instantiate(modelAsset);
            inst.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var anim = inst.GetComponent<Animator>();
                if (!anim || !anim.avatar || !anim.avatar.isHuman) { Debug.LogWarning("Stan's Game: no humanoid model for the stance clips"); return result; }
                if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/StansGame/Animations", "Generated");

                var handler = new HumanPoseHandler(anim.avatar, inst.transform);
                var basePose = new HumanPose();
                handler.GetHumanPose(ref basePose);
                // the standing aim pose's muscles (arms, fingers, head) from the reference clip, where it has curves
                if (standRef)
                {
                    foreach (var b in AnimationUtility.GetCurveBindings(standRef))
                    {
                        int mi = System.Array.IndexOf(HumanTrait.MuscleName, b.propertyName);
                        if (mi >= 0) basePose.muscles[mi] = AnimationUtility.GetEditorCurve(standRef, b).Evaluate(0f);
                    }
                }
                // legs straight, spine upright to start from
                for (int i = 0; i < HumanTrait.MuscleCount; i++)
                {
                    string n = HumanTrait.MuscleName[i];
                    if (n.Contains("Leg") || n.Contains("Foot") || n.Contains("Toes") || n.StartsWith("Spine") || n.StartsWith("Chest") || n.StartsWith("UpperChest"))
                        basePose.muscles[i] = 0f;
                }

                Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
                var hips = B(HumanBodyBones.Hips);
                var lUp = B(HumanBodyBones.LeftUpperLeg); var lLo = B(HumanBodyBones.LeftLowerLeg); var lFoot = B(HumanBodyBones.LeftFoot);
                var rUp = B(HumanBodyBones.RightUpperLeg); var rLo = B(HumanBodyBones.RightLowerLeg); var rFoot = B(HumanBodyBones.RightFoot);
                var spine = B(HumanBodyBones.Spine); var chest = B(HumanBodyBones.Chest) ?? spine;
                var neck = B(HumanBodyBones.Neck); var head = B(HumanBodyBones.Head);
                Vector3 right = Vector3.right, up = Vector3.up;

                void Reset() { handler.SetHumanPose(ref basePose); }
                float FootY() => Mathf.Min(lFoot.position.y, rFoot.position.y);
                void Rot(Transform t, Vector3 axis, float deg) { if (t) t.rotation = Quaternion.AngleAxis(deg, axis) * t.rotation; }
                // bend a leg: thigh forward (negative about +X lifts the knee), knee back, foot kept flat
                void Leg(Transform upper, Transform lower, Transform foot, float thigh, float knee)
                {
                    Quaternion footWas = foot.rotation;
                    Rot(upper, right, thigh);
                    Rot(lower, right, knee);
                    foot.rotation = footWas;
                }

                float groundFoot;
                Reset();
                groundFoot = FootY();

                var lUpArm = B(HumanBodyBones.LeftUpperArm); var lLoArm = B(HumanBodyBones.LeftLowerArm); var lHand = B(HumanBodyBones.LeftHand);
                var rUpArm = B(HumanBodyBones.RightUpperArm); var rLoArm = B(HumanBodyBones.RightLowerArm); var rHand = B(HumanBodyBones.RightHand);
                // turn a bone so that it points (towards its child) along a world direction
                void Aim(Transform bone, Transform child, Vector3 dir)
                {
                    if (!bone || !child) return;
                    Vector3 cur = child.position - bone.position;
                    if (cur.sqrMagnitude < 1e-6f || dir.sqrMagnitude < 1e-6f) return;
                    bone.rotation = Quaternion.FromToRotation(cur, dir.normalized) * bone.rotation;
                }

                HumanPose Capture(bool keepArms = false)
                {
                    var p = new HumanPose(); handler.GetHumanPose(ref p);
                    if (keepArms) return p;
                    // keep the arms as in the standing aim pose
                    for (int i = 0; i < HumanTrait.MuscleCount; i++)
                    {
                        string n = HumanTrait.MuscleName[i];
                        if (n.Contains("Arm") || n.Contains("Hand") || n.Contains("Shoulder") || n.Contains("Finger") || n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little"))
                            p.muscles[i] = basePose.muscles[i];
                    }
                    return p;
                }

                // ---------------------------------------------------- crouch
                HumanPose CrouchPose(float phase, bool moving)
                {
                    Reset();
                    float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
                    float swing = moving ? 22f : 0f;
                    // left knee up, right knee lower (one knee nearly down when still, like a man kneeling to fire)
                    float lThigh = moving ? -62f + swing * s : -88f, lKnee = moving ? 95f - 12f * s + Mathf.Max(0f, c) * 20f : 95f;
                    float rThigh = moving ? -62f - swing * s : -20f, rKnee = moving ? 95f + 12f * s + Mathf.Max(0f, -c) * 20f : 120f;
                    Leg(lUp, lLo, lFoot, lThigh, lKnee);
                    Leg(rUp, rLo, rFoot, rThigh, rKnee);
                    if (!moving && rFoot) rFoot.rotation = Quaternion.AngleAxis(55f, right) * rFoot.rotation;  // up on the toes of the kneeling leg
                    Rot(spine, right, moving ? 22f : 14f);
                    Rot(neck, right, moving ? -14f : -10f);
                    // put the lower foot (or the kneeling knee) back on the ground
                    float low = FootY();
                    if (!moving && rLo) low = Mathf.Min(low, rLo.position.y - 0.04f);
                    hips.position += up * (groundFoot - low);
                    return Capture();
                }

                // ---------------------------------------------------- prone
                HumanPose PronePose(float phase, bool moving)
                {
                    Reset();
                    // face down: the whole body pitches forward around the hips
                    hips.rotation = Quaternion.AngleAxis(88f, right) * hips.rotation;
                    // legs a little apart, toes on the ground
                    float s = moving ? Mathf.Max(0f, Mathf.Sin(phase)) : 0f, t = moving ? Mathf.Max(0f, -Mathf.Sin(phase)) : 0f;
                    Rot(lUp, up, 12f + 40f * s); Rot(lLo, up, 75f * s);
                    Rot(rUp, up, -12f - 40f * t); Rot(rLo, up, -75f * t);
                    // shoulders raised on the elbows, head up to look ahead
                    Rot(chest, right, -18f);
                    Rot(neck, right, -38f);
                    Rot(head, right, -22f);
                    // lie on the ground, the body centred over the feet position of the soldier
                    float low = float.MaxValue;
                    foreach (var b in new[] { hips, spine, chest, lUp, rUp, lLo, rLo }) if (b) low = Mathf.Min(low, b.position.y);
                    hips.position += up * (0.13f - low + groundFoot);
                    hips.position += Vector3.back * 0.35f;
                    return Capture();
                }

                // ---------------------------------------------------- treading water (still, head out)
                HumanPose TreadPose(float phase)
                {
                    Reset();
                    float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
                    // "eggbeater" legs: each knee drawn up and pushed down in turn
                    Leg(lUp, lLo, lFoot, -35f + 18f * s, 55f + 25f * c);
                    Leg(rUp, rLo, rFoot, -35f - 18f * s, 55f - 25f * c);
                    // arms out to the sides just under the surface, sculling forward and back
                    foreach (var (ua, lo, hand, side) in new[] { (lUpArm, lLoArm, lHand, -1f), (rUpArm, rLoArm, rHand, 1f) })
                    {
                        Aim(ua, lo, new Vector3(side * 0.85f, -0.35f, 0.25f + 0.25f * s));
                        Aim(lo, hand, new Vector3(side * 0.55f, -0.25f, 0.75f + 0.35f * s));
                    }
                    Rot(spine, right, 8f);
                    Rot(neck, right, -8f);
                    hips.position += up * (0.04f * c);        // a little bob
                    return Capture(true);
                }

                // ---------------------------------------------------- front crawl (moving)
                HumanPose CrawlPose(float phase)
                {
                    Reset();
                    // face down, flat at the surface
                    hips.rotation = Quaternion.AngleAxis(82f, right) * hips.rotation;
                    // flutter kick: straight legs beating up and down, quick
                    float k = Mathf.Sin(phase * 3f);
                    Rot(lUp, right, 10f * k); Rot(rUp, right, -10f * k);
                    Rot(lLo, right, 12f + 8f * k); Rot(rLo, right, 12f - 8f * k);
                    // arm strokes, half a cycle apart: reach forward, pull down and back under the body, recover over the water
                    foreach (var (ua, lo, hand, side, off) in new[] { (lUpArm, lLoArm, lHand, -1f, 0f), (rUpArm, rLoArm, rHand, 1f, Mathf.PI) })
                    {
                        float a = Mathf.Repeat(phase + off, Mathf.PI * 2f);
                        Vector3 dir, fore;
                        if (a < Mathf.PI)
                        {
                            // the pull: forward -> straight down -> back along the body, under water
                            dir = new Vector3(side * 0.08f, -Mathf.Sin(a), Mathf.Cos(a));
                            fore = dir + new Vector3(0f, -0.25f * Mathf.Sin(a), 0f);
                        }
                        else
                        {
                            // the recovery: elbow high out of the water, hand swinging forward past the head
                            float b = a - Mathf.PI;
                            dir = new Vector3(side * 0.55f * Mathf.Sin(b), 0.65f * Mathf.Sin(b), -Mathf.Cos(b));
                            fore = new Vector3(side * 0.2f, -0.3f, 1f - 0.6f * Mathf.Sin(b));
                        }
                        Aim(ua, lo, dir);
                        Aim(lo, hand, fore);
                    }
                    // head down in the water, turning to breathe on the right every stroke
                    Rot(neck, right, -12f);
                    if (head) head.rotation = Quaternion.AngleAxis(Mathf.Max(0f, Mathf.Sin(phase)) * 55f, Vector3.forward) * head.rotation;
                    // float: the chest just under the surface, which is ~1.35 m above the feet of a man treading water
                    float low = float.MaxValue;
                    foreach (var b in new[] { hips, spine, chest }) if (b) low = Mathf.Min(low, b.position.y);
                    hips.position += up * (1.2f - low + groundFoot);
                    hips.position += Vector3.back * 0.35f;
                    return Capture(true);
                }

                AnimationClip Save(string name, System.Func<float, HumanPose> pose, float length, int keys, bool loop)
                {
                    var clip = new AnimationClip { name = name, frameRate = 30f };
                    var curves = new AnimationCurve[HumanTrait.MuscleCount];
                    for (int i = 0; i < curves.Length; i++) curves[i] = new AnimationCurve();
                    var rt = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                    var rq = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                    int n = loop ? keys + 1 : 1;
                    for (int k = 0; k < n; k++)
                    {
                        float u = loop ? (float)k / keys : 0f;
                        var p = pose(u * Mathf.PI * 2f);
                        float time = loop ? u * length : 0f;
                        for (int i = 0; i < curves.Length; i++) curves[i].AddKey(time, p.muscles[i]);
                        rt[0].AddKey(time, p.bodyPosition.x); rt[1].AddKey(time, p.bodyPosition.y); rt[2].AddKey(time, p.bodyPosition.z);
                        rq[0].AddKey(time, p.bodyRotation.x); rq[1].AddKey(time, p.bodyRotation.y); rq[2].AddKey(time, p.bodyRotation.z); rq[3].AddKey(time, p.bodyRotation.w);
                        if (!loop) // a still pose still needs a little length
                        {
                            for (int i = 0; i < curves.Length; i++) curves[i].AddKey(length, p.muscles[i]);
                            rt[0].AddKey(length, p.bodyPosition.x); rt[1].AddKey(length, p.bodyPosition.y); rt[2].AddKey(length, p.bodyPosition.z);
                            rq[0].AddKey(length, p.bodyRotation.x); rq[1].AddKey(length, p.bodyRotation.y); rq[2].AddKey(length, p.bodyRotation.z); rq[3].AddKey(length, p.bodyRotation.w);
                        }
                    }
                    for (int i = 0; i < curves.Length; i++)
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), HumanTrait.MuscleName[i]), curves[i]);
                    string[] tn = { "RootT.x", "RootT.y", "RootT.z" }, qn = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
                    for (int i = 0; i < 3; i++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), tn[i]), rt[i]);
                    for (int i = 0; i < 4; i++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), qn[i]), rq[i]);
                    var st = AnimationUtility.GetAnimationClipSettings(clip);
                    st.loopTime = true;
                    st.loopBlendOrientation = true; st.loopBlendPositionY = true; st.loopBlendPositionXZ = true;
                    st.keepOriginalOrientation = true; st.keepOriginalPositionY = true; st.keepOriginalPositionXZ = true;
                    AnimationUtility.SetAnimationClipSettings(clip, st);
                    string path = $"{Dir}/{name}.anim";
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(clip, path);
                    return clip;
                }

                result["CrouchIdle"] = Save("CrouchIdle", ph => CrouchPose(ph, false), 1f, 1, false);
                result["CrouchWalk"] = Save("CrouchWalk", ph => CrouchPose(ph, true), 1.0f, 12, true);
                result["ProneIdle"] = Save("ProneIdle", ph => PronePose(ph, false), 1f, 1, false);
                result["ProneCrawl"] = Save("ProneCrawl", ph => PronePose(ph, true), 1.4f, 12, true);
                result["TreadWater"] = Save("TreadWater", ph => TreadPose(ph), 1.6f, 12, true);
                result["SwimCrawl"] = Save("SwimCrawl", ph => CrawlPose(ph), 1.5f, 16, true);
                handler.Dispose();
                AssetDatabase.SaveAssets();
            }
            finally { Object.DestroyImmediate(inst); }
            return result;
        }
    }
}
