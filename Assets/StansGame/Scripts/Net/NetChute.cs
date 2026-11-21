#if STANS_NET
using Unity.Netcode;
using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// A player's parachute as the others see it: the owner publishes where he is (in the aircraft, falling, under the
    /// canopy, on the ground); the other machines hide the soldier while he is aboard, open the T-5 canopy over his
    /// copy while he is under it and let it collapse like silk when he lands. The owner's own ParachuteDrop does the flying.
    /// </summary>
    public class NetChute : NetworkBehaviour
    {
        public enum Stage : byte { Aboard, Falling, Canopy, Down }
        readonly NetworkVariable<byte> stage = new NetworkVariable<byte>((byte)Stage.Down, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        ParachuteDrop drop;
        Transform canopy;
        Stage shown = (Stage)255;

        void Awake() => drop = GetComponent<ParachuteDrop>();

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                var mission = SainteMereMission.Instance;
                Stage s = mission && mission.LocalAboard ? Stage.Aboard
                        : drop && drop.enabled && drop.Current == ParachuteDrop.State.Falling ? Stage.Falling
                        : drop && drop.enabled && (drop.Current == ParachuteDrop.State.Canopy || drop.Current == ParachuteDrop.State.HungUp) ? Stage.Canopy
                        : Stage.Down;
                if (stage.Value != (byte)s) stage.Value = (byte)s;
                return;
            }
            var now = (Stage)stage.Value;
            if (now == shown) { if (canopy) canopy.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.3f) * 6f, 0f, Mathf.Cos(Time.time * 0.9f) * 5f); return; }
            if (shown == Stage.Aboard || shown == (Stage)255 || now == Stage.Aboard)
                foreach (var r in GetComponentsInChildren<Renderer>(true)) if (!canopy || !r.transform.IsChildOf(canopy)) r.enabled = now != Stage.Aboard;
            if (now == Stage.Canopy && !canopy)
            {
                canopy = drop && drop.canopyPrefab ? Instantiate(drop.canopyPrefab, transform).transform : ParachuteDrop.BuildCanopy(transform);
                canopy.localPosition = new Vector3(0f, 1.6f, 0f);
                if (!canopy.GetComponent<CanopyCloth>()) canopy.gameObject.AddComponent<CanopyCloth>();
            }
            if (now == Stage.Down && canopy)
            {
                var cloth = canopy.GetComponent<CanopyCloth>();
                foreach (var c in canopy.GetComponentsInChildren<Collider>()) c.enabled = false;
                if (cloth) cloth.Collapse(transform.position, drop ? drop.wind : Vector3.right, transform.eulerAngles.y);
                else canopy.SetParent(null, true);
                canopy = null;
            }
            shown = now;
        }

        public override void OnNetworkDespawn() { if (canopy) Destroy(canopy.gameObject); }
    }
}
#endif
