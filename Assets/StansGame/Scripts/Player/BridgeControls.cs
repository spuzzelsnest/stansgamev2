using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>
    /// On the player: standing at the controller in the bridge-keeper's hut, press E to raise or lower the bridge.
    /// Offline and on the host the bridge answers directly; a client asks the host. Added by SoldierController.
    /// </summary>
    public class BridgeControls : MonoBehaviour
    {
        public float range = 2.3f;

        SoldierController soldier;
        Loadout loadout;
        float cooldown;

        void Awake() { soldier = GetComponent<SoldierController>(); loadout = GetComponent<Loadout>(); }

        BridgeLift Lift => BridgeLift.Instance;

        bool Near => Lift && Lift.panel && soldier && soldier.enabled && !CinematicDirector.Playing
                     && Vector3.Distance(transform.position + Vector3.up, Lift.panel.position) < range;

        void Update()
        {
            cooldown -= Time.deltaTime;
            if (!Near || cooldown > 0f) return;
            if (loadout && loadout.NearbyPickup) return;          // E picks things up first
            var kb = Keyboard.current;
            if (kb == null || !kb.eKey.wasPressedThisFrame) return;
            cooldown = 1f;
            bool up = !Lift.Raised;
            if (Net.IsRemoteClient) { NetHooks.RequestBridge?.Invoke(up); return; }
            if (!Lift.Request(up, out string why) && MissionDirector.Instance) MissionDirector.Instance.Say(why, 4f);
        }

        void OnGUI()
        {
            if (!Near) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, richText = true };
            style.normal.textColor = new Color(0.95f, 0.88f, 0.6f);
            string s = Lift.Moving
                ? (Lift.Raised ? "The bridge is lifting...  (<b>E</b> to bring it down)" : "The bridge is coming down...  (<b>E</b> to lift it)")
                : (Lift.Raised ? "Press <b>E</b> to lower the bridge" : "Press <b>E</b> to lift the bridge");
            GUI.Label(new Rect(Screen.width / 2f - 300f, Screen.height * 0.66f, 600f, 30f), s, style);
        }
    }
}
