using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace StansGame.Additions
{
    /// <summary>
    /// Shared helpers for placing the first-person weapon.
    /// - <see cref="SetHolder"/> moves the WeaponHolder and also writes its stored pose (LocalPos/LocalRot,
    ///   body space), which HandIK reads to place the hands, so the hands follow the weapon (one frame later).
    /// - <see cref="AlignModel"/> straightens weapon models that sit at an angle to their own bore
    ///   (the Sten model is turned 12.9 degrees against its muzzle), so the barrel, the body and the iron
    ///   sights lie on the line the rounds travel. With the Unity source this is a fix on the prefab.
    /// </summary>
    public static class WeaponPose
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo localPos = typeof(WeaponHolder).GetField("<LocalPos>k__BackingField", Any);
        static readonly FieldInfo localRot = typeof(WeaponHolder).GetField("<LocalRot>k__BackingField", Any);
        static readonly PropertyInfo body = typeof(WeaponHolder).GetProperty("Body", Any);

        /// <summary>Model yaw against the bore, in degrees, measured from above (positive turns the model to the right).</summary>
        static readonly Dictionary<string, float> modelYaw = new Dictionary<string, float>
        {
            { "Sten Mk II", 12.9f },
        };

        static readonly HashSet<Weapon> aligned = new HashSet<Weapon>();

        /// <summary>
        /// How far each weapon sits too low in the hands, in metres. The M1911's slide is level with the
        /// knuckles of the (large) gloves, so from the eyes the hands cover it; raising it in the grip
        /// shows the slide above the fist.
        /// </summary>
        static readonly Dictionary<string, float> gripLift = new Dictionary<string, float>
        {
            { "M1911", 0.03f },
        };

        /// <summary>Raises the weapon in the hands once (the hands follow the holder, not the weapon).</summary>
        public static void LiftInHands(Weapon w)
        {
            float lift;
            if (w == null || !gripLift.TryGetValue(w.displayName ?? "", out lift)) return;
            Transform wt = w.transform;
            if (wt.parent == null) return;
            wt.localPosition += wt.parent.InverseTransformDirection(wt.up) * (lift / Mathf.Max(0.0001f, wt.parent.lossyScale.y));
        }

        /// <summary>Places the holder (and so the weapon) and records the pose for the hand IK.</summary>
        public static void SetHolder(WeaponHolder holder, Vector3 position, Quaternion rotation)
        {
            if (holder == null) return;
            holder.transform.SetPositionAndRotation(position, rotation);
            SyncHands(holder);
        }

        /// <summary>Writes the holder's current transform into its stored pose, so the hands follow it.</summary>
        public static void SyncHands(WeaponHolder holder)
        {
            if (holder == null || localPos == null || localRot == null || body == null) return;
            Transform b = body.GetValue(holder, null) as Transform;
            if (b == null) return;
            Transform t = holder.transform;
            localPos.SetValue(holder, b.InverseTransformPoint(t.position));
            localRot.SetValue(holder, Quaternion.Inverse(b.rotation) * t.rotation);
        }

        /// <summary>Straightens the weapon's model against its bore once. Safe to call every time.</summary>
        public static void AlignModel(Weapon w)
        {
            if (w == null || w.muzzle == null || aligned.Contains(w)) return;
            aligned.Add(w);
            float yaw;
            if (!modelYaw.TryGetValue(w.displayName ?? "", out yaw) || Mathf.Approximately(yaw, 0f)) return;

            Transform wt = w.transform;
            Transform model = ModelRoot(w);
            if (model == null) return;
            Vector3 muzzlePos = w.muzzle.position;
            Quaternion muzzleRot = w.muzzle.rotation;
            Quaternion turn = Quaternion.AngleAxis(yaw, wt.up);
            model.SetPositionAndRotation(muzzlePos + turn * (model.position - muzzlePos), turn * model.rotation);
            if (w.muzzle.IsChildOf(model)) w.muzzle.SetPositionAndRotation(muzzlePos, muzzleRot);   // the bore stays where it was
            Debug.Log("Stan's Game: straightened the " + w.displayName + " model by " + yaw.ToString("0.0") + " degrees to its bore");
        }

        /// <summary>The weapon's direct child that holds its meshes (the imported model), or null.</summary>
        static Transform ModelRoot(Weapon w)
        {
            Transform wt = w.transform;
            foreach (MeshFilter mf in w.GetComponentsInChildren<MeshFilter>(true))
            {
                Transform t = mf.transform;
                if (t == wt) return null;   // mesh on the weapon itself: nothing to turn separately
                while (t.parent != null && t.parent != wt) t = t.parent;
                if (t.parent != wt || t.name.StartsWith("Iron sights")) continue;   // our generated sights are not the model
                return t;
            }
            return null;
        }
    }
}
