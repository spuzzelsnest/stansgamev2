using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// Piper Bill Millin's pipes: Lord Lovat had him play the commandos across the bridges on 6 June 1944 ("Blue
    /// Bonnets over the Border"). Plays while the piper is in play (MissionDirector puts the commandos in play at the
    /// relief), a 3D sound that carries a long way.
    /// </summary>
    public class PiperMusic : MonoBehaviour
    {
        public float volume = 0.9f;
        AudioSource src; Health health; Renderer rend;

        void Start()
        {
            health = GetComponent<Health>();
            rend = GetComponentInChildren<Renderer>();
            src = gameObject.AddComponent<AudioSource>();
            src.clip = Sfx.Bagpipes; src.loop = true; src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 8f; src.maxDistance = 450f; src.volume = volume; src.playOnAwake = false;
        }

        void Update()
        {
            if (!src) return;
            bool live = (!health || (health.enabled && !health.IsDead)) && (!rend || rend.enabled);
            if (live && !src.isPlaying) src.Play();
            else if (!live && src.isPlaying) src.Stop();
        }
    }
}
