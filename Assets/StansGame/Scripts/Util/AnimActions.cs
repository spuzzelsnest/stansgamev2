namespace StansGame
{
    /// <summary>
    /// The things a character can do besides moving, as the "Action" number on the Soldier and Civilian animators
    /// (Editor/AnimationSets.cs builds them). 0 = none: walk, run, crouch, crawl, swim as the movement says.
    /// Set it with animator.SetInteger("Action", n); set it back to 0 to carry on. One-shots (salute, throw,
    /// reload, fire) play once and return by themselves.
    /// </summary>
    public static class AnimActions
    {
        // soldiers
        public const int Rest = 1, Smoke = 2, Salute = 3, LookAround = 4, Talk = 5, Defend = 6, LieDown = 7, Throw = 8, Reload = 9, Fire = 10, KneelFire = 11, Kneel = 12, Hurting = 13, Stumble = 14, WaveHello = 15, Driving = 16, Seated = 17, EnterCar = 18, ExitCar = 19;
        // civilians
        public const int Toast = 21, Chat = 22, Chat2 = 23, SitTalk = 24, SitDrink = 25, Meeting = 26, Dig = 27, Watercooler = 28, Wave = 29, Wave2 = 30, Wave3 = 31, Watering = 32, PickFruit = 33, PullWeeds = 34, Planting = 35, Gardening = 36;

        /// <summary>The ones a soldier at ease does now and then (the Proving Ground, rear areas).</summary>
        public static readonly int[] SoldierAtEase = { Rest, Smoke, Salute, LookAround, Talk, Defend, LieDown, Kneel, WaveHello };
        /// <summary>The ones a civilian does now and then.</summary>
        public static readonly int[] CivilianAtEase = { Toast, Chat, Chat2, SitTalk, SitDrink, Meeting, Dig, Watercooler, Wave, Wave2, Wave3, Watering, PickFruit, PullWeeds, Planting, Gardening };
        /// <summary>Played once, then back to moving.</summary>
        public static bool OneShot(int a) => a == Salute || a == Throw || a == Reload || a == Fire || a == EnterCar || a == ExitCar;
    }
}
