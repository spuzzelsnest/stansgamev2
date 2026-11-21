using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Makes a tank's tracks run: slides the track texture round the track as the hull moves, forwards or backwards,
    /// whoever drives it (the player, the TankAI, the network). The builder measures, from the model itself, which way
    /// the texture runs and how many texture repeats there are to a metre of track (PegasusLevelBuilder.AddTrackScroll),
    /// so the links on the top run move forward at the tank's own speed and the ones on the ground stand still under it.
    /// Only for tracks textured along their length (Panzer IV); tracks modelled link by link have nothing to slide.
    /// </summary>
    public class TrackScroll : MonoBehaviour
    {
        public Renderer track;
        public int submesh;
        [Tooltip("0 = the texture's U runs along the track, 1 = V.")]
        public int axis = 1;
        [Tooltip("Texture units per metre forward along the top run (signed, measured from the mesh).")]
        public float uvPerMetre = 1f;
        [Tooltip("-1 the left track, +1 the right, 0 one texture for both (no turning difference).")]
        public float side;
        [Tooltip("Metres from the tank's centre line to this track's middle.")]
        public float halfTrack = 1f;

        static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        MaterialPropertyBlock block;
        Vector4 st = new Vector4(1f, 1f, 0f, 0f);
        Vector3 lastPos;
        float lastYaw;

        void Start()
        {
            block = new MaterialPropertyBlock();
            var mats = track ? track.sharedMaterials : null;
            if (mats != null && submesh < mats.Length && mats[submesh] && mats[submesh].HasProperty(BaseMapST))
                st = mats[submesh].GetVector(BaseMapST);
            lastPos = transform.position;
            lastYaw = transform.eulerAngles.y;
        }

        void LateUpdate()
        {
            if (!track) return;
            Vector3 moved = transform.position - lastPos;
            lastPos = transform.position;
            if (moved.sqrMagnitude > 400f) return;   // teleported, not driven
            float d = Vector3.Dot(moved, transform.forward);
            // turning: the outer track runs faster, the inner slower or backwards (a pivot turn: one forward, one back)
            float yaw = Mathf.DeltaAngle(lastYaw, transform.eulerAngles.y) * Mathf.Deg2Rad;
            lastYaw = transform.eulerAngles.y;
            d += -side * yaw * halfTrack;
            if (Mathf.Abs(d) < 1e-5f) return;
            // the top run carries the links forward by d: the texture slides back under it by the measured rate
            if (axis == 0) st.z = Mathf.Repeat(st.z - uvPerMetre * st.x * d, 1f); else st.w = Mathf.Repeat(st.w - uvPerMetre * st.y * d, 1f);
            track.GetPropertyBlock(block, submesh);
            block.SetVector(BaseMapST, st);
            track.SetPropertyBlock(block, submesh);
        }
    }
}
