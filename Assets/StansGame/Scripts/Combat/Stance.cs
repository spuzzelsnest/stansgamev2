using UnityEngine;

namespace StansGame
{
    public enum StanceKind { Stand, Crouch, Prone }

    /// <summary>
    /// Standing, crouching or lying prone — for the player and the AI alike. Shrinks the CharacterController (so a
    /// prone man is a small target and fits under cover), drives the animator ("Crouch" / "Prone"), and tells the
    /// weapon how steady the aim is. Getting down or up takes a moment, as it does in real life.
    /// Added automatically by SoldierController and SoldierAI.
    /// </summary>
    [DisallowMultipleComponent]
    public class Stance : MonoBehaviour
    {
        public Animator animator;
        public StanceKind Current { get; private set; }
        /// <summary>True while getting down or up (no firing for that moment).</summary>
        public bool Changing => Time.time < changeEnd;

        CharacterController cc;
        float standHeight = 1.8f, standRadius = 0.3f;
        float changeEnd;

        /// <summary>Height of the body compared to standing (eyes, muzzle, where to aim at him).</summary>
        public float HeightFactor => Current == StanceKind.Crouch ? 0.62f : Current == StanceKind.Prone ? 0.22f : 1f;
        /// <summary>Spread multiplier: kneeling steadies the aim, lying down with the weapon rested steadies it most.</summary>
        public float SpreadScale => Current == StanceKind.Crouch ? 0.7f : Current == StanceKind.Prone ? 0.45f : 1f;
        /// <summary>Eye height above the feet in metres.</summary>
        public float EyeHeight => Current == StanceKind.Crouch ? 1.05f : Current == StanceKind.Prone ? 0.32f : 1.6f;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (cc) { standHeight = cc.height; standRadius = cc.radius; }
            if (!animator) animator = GetComponentInChildren<Animator>();
        }

        public void Set(StanceKind s)
        {
            if (s == Current) return;
            // dropping prone or getting up from it takes longest
            changeEnd = Time.time + (s == StanceKind.Prone || Current == StanceKind.Prone ? 0.7f : 0.3f);
            Current = s;
        }

        public void Toggle(StanceKind s) => Set(Current == s ? StanceKind.Stand : s);

        void Update()
        {
            if (cc)
            {
                float h = Mathf.Lerp(cc.height, standHeight * (Current == StanceKind.Prone ? 0.3f : HeightFactor), Time.deltaTime * 8f);
                cc.radius = Mathf.Min(standRadius, h * 0.5f);
                cc.height = h;
                cc.center = new Vector3(0f, h * 0.5f, 0f);
            }
            if (animator && animator.runtimeAnimatorController)
            {
                animator.SetBool("Crouch", Current == StanceKind.Crouch);
                animator.SetBool("Prone", Current == StanceKind.Prone);
                // in deep water: tread water / swim the crawl (see StanceClips)
                bool swim = Water.Swimming(transform.position, out _);
                if (swim && Current != StanceKind.Stand) { Current = StanceKind.Stand; }
                animator.SetBool("Swim", swim);
            }
        }
    }
}
