using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// A main character on show (the Proving Ground, beside his army's table): he stays on his spot and goes through
    /// everything he can do except walking - standing, aiming, crouching, kneeling, lying prone or down, sitting,
    /// driving, smoking, saluting, talking, waving, throwing, reloading, firing, wounded - a few seconds each, in a
    /// random order, with the name of what he is doing over his head. For looking at the animations closely.
    /// </summary>
    public class AnimationShowcase : MonoBehaviour
    {
        public Animator animator;
        public string title = "Player";
        public TextMesh label;
        public Vector2 hold = new Vector2(5f, 8f);
        [Tooltip("A box to sit on, shown only for the sitting poses (resting, riding, driving)")] public GameObject seat;

        struct Pose
        {
            public string name; public int action; public bool crouch, prone, aim, injured, hands;
            public Pose(string n, int a = 0, bool hands = true, bool crouch = false, bool prone = false, bool aim = false, bool injured = false)
            { name = n; action = a; this.hands = hands; this.crouch = crouch; this.prone = prone; this.aim = aim; this.injured = injured; }
        }

        static readonly Pose[] Poses =
        {
            new Pose("Standing"), new Pose("Aiming", aim: true), new Pose("Crouching", crouch: true), new Pose("Prone", prone: true),
            new Pose("Kneeling on guard", AnimActions.Kneel), new Pose("Defending at a corner", AnimActions.Defend),
            new Pose("Firing", AnimActions.Fire), new Pose("Kneeling and firing", AnimActions.KneelFire),
            new Pose("Reloading", AnimActions.Reload), new Pose("Throwing a grenade", AnimActions.Throw),
            new Pose("Resting (sitting)", AnimActions.Rest, false), new Pose("Sitting (riding)", AnimActions.Seated, false),
            new Pose("Driving", AnimActions.Driving, false), new Pose("Lying down", AnimActions.LieDown, false),
            new Pose("Smoking", AnimActions.Smoke, false), new Pose("Saluting", AnimActions.Salute, false),
            new Pose("Looking around", AnimActions.LookAround, false), new Pose("Talking", AnimActions.Talk, false),
            new Pose("Waving", AnimActions.WaveHello, false), new Pose("Wounded", injured: true, hands: false),
            new Pose("Hurting", AnimActions.Hurting, false), new Pose("Stumbling", AnimActions.Stumble, false),
        };

        int current = -1; float until;
        Vector3 home; Quaternion homeRot;
        Stance stance; HandIK hands;

        void Start()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (animator) animator.applyRootMotion = false;
            stance = GetComponent<Stance>();
            hands = GetComponentInChildren<HandIK>();
            home = transform.position; homeRot = transform.rotation;
            until = Time.time + Random.Range(0f, 2f);   // the three don't change all at once
        }

        void Update()
        {
            if (!animator) return;
            if (Time.time >= until) Next();
            // stays on his spot whatever the clip does
            transform.SetPositionAndRotation(home, homeRot);
            Locomotion.Set(animator, Vector3.zero, 0.1f, true);
            Set("Grounded", true);
        }

        void Next()
        {
            int i; do i = Random.Range(0, Poses.Length); while (i == current && Poses.Length > 1);
            current = i;
            var p = Poses[i];
            animator.SetInteger("Action", p.action);
            var sk = p.prone ? StanceKind.Prone : p.crouch ? StanceKind.Crouch : StanceKind.Stand;
            if (stance) stance.Set(sk); else { Set("Crouch", p.crouch); Set("Prone", p.prone); }
            Set("Aiming", p.aim); Set("Injured", p.injured);
            if (hands) hands.enabled = p.hands;
            if (seat) seat.SetActive(p.action == AnimActions.Rest || p.action == AnimActions.Seated || p.action == AnimActions.Driving);
            if (label) label.text = $"{title}\n{p.name}";
            until = Time.time + Random.Range(hold.x, hold.y);
        }

        void Set(string param, bool v)
        {
            foreach (var pr in animator.parameters) if (pr.name == param && pr.type == AnimatorControllerParameterType.Bool) { animator.SetBool(pr.nameHash, v); return; }
        }

        void LateUpdate()
        {
            if (!label) return;
            var cam = Camera.main; if (!cam) return;
            Vector3 d = label.transform.position - cam.transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) label.transform.rotation = Quaternion.LookRotation(d);
            label.gameObject.SetActive(d.sqrMagnitude < 40f * 40f);
        }
    }
}
