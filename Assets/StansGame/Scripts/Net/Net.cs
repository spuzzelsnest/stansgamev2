using UnityEngine;

namespace StansGame
{
    /// <summary>
    /// One place for gameplay code to ask "am I the one simulating the world?".
    /// Works with and without the multiplayer packages (STANS_NET define):
    ///   - single player / no netcode: everything runs locally, as before;
    ///   - host: runs the world (AI, gliders, mission, damage) for everyone;
    ///   - client: only drives its own soldier; the world arrives from the host.
    /// </summary>
    public static class Net
    {
#if STANS_NET
        static Unity.Netcode.NetworkManager NM => Unity.Netcode.NetworkManager.Singleton;
        /// <summary>A network session is running (host or client).</summary>
        public static bool Active => NM && NM.IsListening;
        /// <summary>True while the start menu is up and no session has been started yet.</summary>
        public static bool InMenu => NM && !NM.IsListening && !NM.ShutdownInProgress;
        public static bool IsServer => Active && NM.IsServer;
        public static ulong LocalClientId => NM ? NM.LocalClientId : 0;
#else
        public static bool Active => false;
        public static bool InMenu => false;
        public static bool IsServer => false;
        public static ulong LocalClientId => 0;
#endif
        /// <summary>This machine runs the world: offline play or the host.</summary>
        public static bool Simulates => ForceSimulate || (!InMenu && (!Active || IsServer));
        /// <summary>The history film (HistoryFilm.cs) lets the world run under the main menu: sentries walk, gliders land, the AI fights.</summary>
        public static bool ForceSimulate;
        /// <summary>A client that is not the host: damage etc. must be sent to the host.</summary>
        public static bool IsRemoteClient => Active && !IsServer;
    }

    /// <summary>
    /// Hooks the gameplay code calls; the network layer (Net/*.cs, STANS_NET only) fills them in.
    /// With no network they are null and the gameplay code simply does everything locally.
    /// </summary>
    public static class NetHooks
    {
        /// <summary>A remote client hit something: ask the host to apply the damage.</summary>
        public static System.Action<Health, DamageInfo> RequestDamage;
        /// <summary>A shot was fired here: show the tracer on the other machines.</summary>
        public static System.Action<Vector3, Vector3> ShotFired;
        /// <summary>A projectile (PIAT bomb, tank shell) was launched here: replicate it.</summary>
        public static System.Action<Shell, Vector3, Vector3, GameObject> ProjectileLaunched;
        /// <summary>A remote client used a field dressing: ask the host to restore the hit points.</summary>
        public static System.Action<Health, float> RequestHeal;
        /// <summary>The host created a new object at runtime (a glider section): give it a network identity.</summary>
        public static System.Action<GameObject> Spawned;
        /// <summary>A remote client's player sent "Ham and Jam" on the wireless: tell the host.</summary>
        public static System.Action RequestSignal;
        /// <summary>A remote client's player worked the bridge controller: ask the host to raise (true) or lower it.</summary>
        public static System.Action<bool> RequestBridge;
    }
}
