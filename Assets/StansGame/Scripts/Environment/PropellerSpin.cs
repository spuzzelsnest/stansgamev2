using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Turns an aircraft's propellers: every child whose name starts with "Prop_" (cut out of the model with its
    /// origin on the propeller shaft, see Models/Source~/halifax_b3.py) spins round the aircraft's nose axis.
    /// Put it on the object whose +Z is the nose. The rate is a "screen" rate rather than the real 1,300-odd rpm,
    /// which at 60 frames a second would strobe and seem to stand still or turn backwards; each engine is a little
    /// out of step with the others.
    /// </summary>
    public class PropellerSpin : MonoBehaviour
    {
        [Tooltip("Degrees a second (about 4 turns a second reads as 'engines running' without strobing).")]
        public float degreesPerSecond = 1500f;
        [Tooltip("Turn the other way.")]
        public bool reverse;

        Transform[] props;
        Quaternion[] rest;
        Vector3[] axis;
        float[] angle, rate;

        void Awake()
        {
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("Prop_")) list.Add(t);
            props = list.ToArray();
            rest = new Quaternion[props.Length]; axis = new Vector3[props.Length];
            angle = new float[props.Length]; rate = new float[props.Length];
            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                rest[i] = p.localRotation;
                Vector3 nose = transform.forward;   // the shaft, in the propeller's parent space
                axis[i] = p.parent ? p.parent.InverseTransformDirection(nose).normalized : nose;
                angle[i] = i * 37f;
                rate[i] = degreesPerSecond * (1f + 0.03f * Mathf.Sin(i * 2.1f + 0.7f)) * (reverse ? -1f : 1f);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < props.Length; i++)
            {
                if (!props[i]) continue;
                angle[i] = Mathf.Repeat(angle[i] + rate[i] * dt, 360f);
                props[i].localRotation = Quaternion.AngleAxis(angle[i], axis[i]) * rest[i];
            }
        }
    }
}
