using UnityEngine;
using UnityEngine.InputSystem;

namespace StansGame
{
    /// <summary>
    /// The No. 18 wireless set on the player's back. When the mission asks for it, hold H to send the success signal
    /// "Ham and Jam" (about three seconds of keying, with static in the headphones). Each completed transmission is
    /// reported to the mission (on a client: to the host), which answers after a few tries. Added by SoldierController.
    /// </summary>
    public class RadioSet : MonoBehaviour
    {
        public float sendSeconds = 3f;

        SoldierController soldier;
        AudioSource hiss;
        float progress;
        bool sentThisPress;

        void Awake() => soldier = GetComponent<SoldierController>();

        bool Wanted => MissionDirector.Instance && MissionDirector.Instance.CurrentPhase == 2 && soldier && soldier.enabled && !CinematicDirector.Playing;

        void Update()
        {
            var kb = Keyboard.current;
            bool holding = Wanted && kb != null && kb.hKey.isPressed;
            if (!holding) { progress = 0f; sentThisPress = false; Hiss(false); return; }
            if (sentThisPress) { Hiss(false); return; }     // let go and press again for the next transmission
            Hiss(true);
            progress += Time.deltaTime / sendSeconds;
            if (progress >= 1f)
            {
                progress = 0f; sentThisPress = true; Hiss(false);
                if (Net.IsRemoteClient && NetHooks.RequestSignal != null) NetHooks.RequestSignal();
                else MissionDirector.Instance.ReportSignal();
            }
        }

        void Hiss(bool on)
        {
            if (on && !hiss)
            {
                hiss = gameObject.AddComponent<AudioSource>();
                hiss.clip = MakeStatic(); hiss.loop = true; hiss.spatialBlend = 0f; hiss.volume = 0.18f;
            }
            if (!hiss) return;
            if (on && !hiss.isPlaying) hiss.Play();
            if (!on && hiss.isPlaying) hiss.Stop();
        }

        static AudioClip staticClip;
        static AudioClip MakeStatic()
        {
            if (staticClip) return staticClip;
            const int rate = 22050; int n = rate * 2;
            var d = new float[n];
            var rnd = new System.Random(1944);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                lp = Mathf.Lerp(lp, (float)rnd.NextDouble() * 2f - 1f, 0.35f);
                // static, plus a keyed tone "dit-dah" pattern underneath, like a set being worked
                float key = Mathf.Repeat(t * 3.1f, 1f) < (Mathf.Repeat(t * 0.9f, 1f) < 0.5f ? 0.25f : 0.6f) ? 1f : 0f;
                d[i] = lp * 0.6f + Mathf.Sin(2f * Mathf.PI * 760f * t) * 0.25f * key;
            }
            staticClip = AudioClip.Create("radio static", n, 1, rate, false);
            staticClip.SetData(d, 0);
            return staticClip;
        }

        void OnGUI()
        {
            if (!Wanted) return;
            var md = MissionDirector.Instance;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, richText = true };
            style.normal.textColor = new Color(0.95f, 0.88f, 0.6f);
            float w = 420f, x = (Screen.width - w) / 2f, y = Screen.height * 0.68f;
            string sent = md.SignalsSent > 0 ? $"   (sent {md.SignalsSent}×)" : "";
            // the last try: Tappenden had been sending for ages with no answer
            bool last = md.SignalsSent >= md.signalsNeeded - 1;
            string calling = last ? "<b>\"Ham and Bloody Jam!\"</b>" : "<b>\"Ham and Jam... Ham and Jam...\"</b>";
            GUI.Label(new Rect(x - 100, y - 30, w + 200, 26), progress > 0f ? calling : $"Hold <b>H</b> to send \"Ham and Jam\" on the wireless{sent}", style);
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(new Rect(x, y, w, 12), Texture2D.whiteTexture);
            GUI.color = new Color(0.95f, 0.8f, 0.35f, 0.9f); GUI.DrawTexture(new Rect(x + 1, y + 1, (w - 2) * progress, 10), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
