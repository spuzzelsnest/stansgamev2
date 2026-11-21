using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Short-lived line renderer tracer. Every few rounds glow at night, which sells the firefight.
    /// Never throws: a tracer is decoration, the shot itself must always go through.
    /// </summary>
    public class Tracer : MonoBehaviour
    {
        static Material sharedMat;
        static bool noMaterial;
        LineRenderer lr;
        float life = 0.06f, t;

        public static void Spawn(Vector3 from, Vector3 to, bool always = false)
        {
            if (!always && Random.value > 0.35f) return;
            if (noMaterial) return;
            try
            {
                if (!sharedMat)
                {
                    sharedMat = RuntimeMaterials.Unlit(new Color(1f, 0.75f, 0.35f, 1f));
                    if (!sharedMat) { noMaterial = true; return; }
                }
                var go = new GameObject("Tracer");
                var tr = go.AddComponent<Tracer>();
                tr.lr = go.AddComponent<LineRenderer>();
                tr.lr.sharedMaterial = sharedMat;
                tr.lr.positionCount = 2;
                tr.lr.SetPosition(0, from);
                tr.lr.SetPosition(1, to);
                tr.lr.startWidth = 0.025f; tr.lr.endWidth = 0.01f;
                tr.lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.lr.receiveShadows = false;
            }
            catch (System.Exception e) { noMaterial = true; Debug.LogWarning("Stan's Game: tracers off - " + e.Message); }
        }

        void Update()
        {
            t += Time.deltaTime;
            if (t > life) Destroy(gameObject);
        }
    }
}
