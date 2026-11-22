#if STANS_NET
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StansGame
{
    /// <summary>
    /// Start menu and session handling for multiplayer co-op, on the NetworkManager object.
    ///   Play alone          - hosts on this PC only.
    ///   Home network (LAN)  - host, or join the host's IP address (port 7777).
    ///   Over the internet   - host gets a join code (Unity Relay, no port forwarding); friends type the code.
    /// The host runs the world; every player gets an Ox &amp; Bucks soldier, respawned 8 s after being killed.
    /// </summary>
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public class NetSession : MonoBehaviour
    {
        public static NetSession Instance { get; private set; }

        public GameObject playerPrefab;
        [Tooltip("Sainte-Mère-Église: the US paratrooper every player gets there.")]
        public GameObject playerPrefabUS;
        [Tooltip("Prefabs the host creates during play (British soldiers). Must be the same in every copy of the game.")]
        public GameObject[] networkPrefabs;
        public ushort port = 7777;
        public int maxPlayers = 8;

        NetworkManager nm;
        UnityTransport transport;
        ISession session;
        string ipText = "192.168.", codeText = "", status = "", mode = "";
        string pending;                      // "alone", "lan", "online": the map comes next

        bool busy, showMenu;
        readonly Dictionary<ulong, NetworkObject> players = new Dictionary<ulong, NetworkObject>();

        void Awake()
        {
            Instance = this;
            nm = GetComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();
            nm.NetworkConfig.EnableSceneManagement = true;    // needed so the level's own soldiers, gliders and tanks match up
            nm.NetworkConfig.PlayerPrefab = null;              // players are spawned here, at the glider
            if (playerPrefab) Register(playerPrefab);
            if (playerPrefabUS) Register(playerPrefabUS);
            if (networkPrefabs != null) foreach (var p in networkPrefabs) if (p) Register(p);
            nm.OnServerStarted += OnServerStarted;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            ipText = PlayerPrefs.GetString("stans.lastIp", ipText);
        }

        void Register(GameObject prefab)
        {
            // Netcode's own DefaultNetworkPrefabs list usually has them already; only add what is missing
            var prefabs = nm.NetworkConfig.Prefabs;
            if (prefabs.NetworkPrefabsLists != null)
                foreach (var list in prefabs.NetworkPrefabsLists)
                    if (list) foreach (var entry in list.PrefabList) if (entry.Prefab == prefab) return;
            if (!prefabs.Contains(prefab)) nm.AddNetworkPrefab(prefab);
        }

        void OnDestroy()
        {
            if (!nm) return;
            nm.OnServerStarted -= OnServerStarted;
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        // ------------------------------------------------------------------ starting

        void PlayAlone()
        {
            transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
            mode = "Playing alone";
            if (!nm.StartHost()) status = "Could not start.";
        }

        void HostLan()
        {
            transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
            mode = $"Hosting on your home network - friends join: {LocalIp()}";
            if (!nm.StartHost()) status = $"Could not host on port {port}. Is another copy of the game already hosting?";
        }

        void JoinLan()
        {
            string ip = ipText.Trim();
            PlayerPrefs.SetString("stans.lastIp", ip);
            transport.SetConnectionData(ip, port);
            mode = $"Joined {ip}";
            status = $"Connecting to {ip}...";
            if (!nm.StartClient()) status = "Could not start the connection.";
        }

        async void HostOnline()
        {
            busy = true; status = "Signing in to Unity Relay...";
            try
            {
                await SignIn();
                status = "Creating the game...";
                var options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
                codeText = session.Code;
                mode = $"Hosting online - join code: {session.Code}";
                status = "";
            }
            catch (System.Exception e) { status = "Online hosting failed: " + Friendly(e); Debug.LogException(e); }
            busy = false;
        }

        async void JoinOnline()
        {
            busy = true; status = "Signing in to Unity Relay...";
            try
            {
                await SignIn();
                string code = codeText.Trim().ToUpperInvariant();
                status = $"Joining {code}...";
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                mode = $"Joined online game {code}";
                status = "";
            }
            catch (System.Exception e) { status = "Joining failed: " + Friendly(e); Debug.LogException(e); }
            busy = false;
        }

        static async System.Threading.Tasks.Task SignIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        static string Friendly(System.Exception e)
        {
            string m = e.Message;
            if (m.Contains("project") && (m.Contains("ID") || m.Contains("Id") || m.Contains("link")))
                return "this copy of the game is not linked to a Unity Cloud project yet (see README).";
            return m;
        }

        bool leaving;
        public void Leave()
        {
            if (leaving) return;
            leaving = true;
            StartCoroutine(LeaveRoutine());
        }

        IEnumerator LeaveRoutine()
        {
            if (session != null) { var t = session.LeaveAsync(); while (!t.IsCompleted) yield return null; session = null; }
            if (nm.IsListening) nm.Shutdown();
            while (nm.ShutdownInProgress) yield return null;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Destroy(nm.gameObject);                                   // a fresh NetworkManager comes with the reloaded level
            SceneManager.LoadScene(0);   // the start menu lives in the first scene (Pegasus Bridge)
        }

        // ------------------------------------------------------------------ players (host)

        void OnServerStarted()
        {
            // every copy already has the level loaded: let joining players keep theirs instead of reloading it
            if (nm.SceneManager != null && !string.IsNullOrEmpty(targetScene) && targetScene != SceneManager.GetActiveScene().name)
            {
                // hosting another chapter: everyone (and every later joiner) loads it in place of this one
                nm.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Single);
                loadingLevel = true;
                nm.SceneManager.OnLoadEventCompleted += OnLevelLoaded;
                nm.SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
            }
            else if (nm.SceneManager != null) nm.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
            Debug.Log("Stan's Game: host started");
        }

        string targetScene;
        bool loadingLevel;

        /// <summary>"The History" from the level select (each level's own film, HistoryFilm.cs): close the screen and play it.
        /// When it ends the level reloads and the menu comes back.</summary>
        void PlayHistory(LevelSelect.Mission m)
        {
            if (!LevelSelect.HasHistory(m) || busy) return;
            pending = null;
            HistoryFilm.Instance.Play();
        }

        void OnLevelLoaded(string sceneName, LoadSceneMode mode, List<ulong> done, List<ulong> timedOut)
        {
            nm.SceneManager.OnLoadEventCompleted -= OnLevelLoaded;
            loadingLevel = false;
            players.Clear();   // the soldiers spawned with the old level went with it
            foreach (var id in nm.ConnectedClientsIds) if (!players.ContainsKey(id)) SpawnPlayer(id);
        }

        void OnClientConnected(ulong clientId)
        {
            if (nm.IsServer && !loadingLevel && !players.ContainsKey(clientId)) SpawnPlayer(clientId);
            if (!nm.IsServer && clientId == nm.LocalClientId) status = "";
        }

        void OnClientDisconnected(ulong clientId)
        {
            players.Remove(clientId);
            if (!nm.IsServer && clientId == nm.LocalClientId)
            {
                // we were a client and the host has gone (or refused us): back to a fresh start menu
                Debug.Log("Stan's Game: disconnected from the host - " + nm.DisconnectReason);
                Leave();
            }
        }

        void SpawnPlayer(ulong clientId)
        {
            var sme = SainteMereMission.Instance;
            var prefab = sme && playerPrefabUS ? playerPrefabUS : playerPrefab;
            if (!prefab) { Debug.LogError("Stan's Game: NetSession has no player prefab"); return; }
            var md = MissionDirector.Instance;
            Vector3 pos = new Vector3(70f, 2f, -30f); Quaternion rot = Quaternion.identity;
            if (sme) { pos = sme.SpawnPoint(clientId); rot = Quaternion.Euler(0f, 90f, 0f); md = null; }
            var ben = BenouvilleMission.Instance;
            if (ben) { pos = ben.SpawnPoint(clientId); rot = Quaternion.Euler(0f, -90f, 0f); md = null; }
            if (md)
            {
                Transform at = md.CurrentPhase >= 1 && md.bridgeObjective ? md.bridgeObjective
                    : (md.gliderSectionSpawns != null && md.gliderSectionSpawns.Length > 0) ? md.gliderSectionSpawns[0] : null;
                if (at) { pos = at.position + at.right * ((int)(clientId % 6) - 2.5f) * 1.6f + Vector3.up * 0.5f; rot = Quaternion.Euler(0f, at.eulerAngles.y, 0f); }
            }
            var go = Instantiate(prefab, pos, rot);
            var no = go.GetComponent<NetworkObject>();
            no.SpawnAsPlayerObject(clientId, true);
            players[clientId] = no;
        }

        /// <summary>Host: bring a killed player back after a short wait.</summary>
        public void RespawnLater(ulong clientId, NetworkObject dead) => StartCoroutine(Respawn(clientId, dead));

        IEnumerator Respawn(ulong clientId, NetworkObject dead)
        {
            yield return new WaitForSeconds(8f);
            if (!nm.IsServer || !nm.ConnectedClients.ContainsKey(clientId)) yield break;
            if (dead && dead.IsSpawned) dead.Despawn(true);
            players.Remove(clientId);
            SpawnPlayer(clientId);
        }

        // ------------------------------------------------------------------ UI

        void Update()
        {
            if (Net.InMenu && pending != null && !busy) LevelSelect.Keys(pending == "alone", StartMap, () => pending = null, PlayHistory);
            if (Net.Active && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                showMenu = !showMenu;
                Cursor.lockState = showMenu ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = showMenu;
            }
        }

        static string LocalIp()
        {
            var list = new List<string>();
            try
            {
                foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) list.Add(a.ToString());
            }
            catch { }
            return list.Count > 0 ? string.Join("  or  ", list) : "(your PC's IP address)";
        }

        void OnGUI()
        {
            GUI.skin.label.richText = true;
            if (Net.InMenu) { if (HistoryFilm.Playing) return; StartMenu(); return; }   // the history film draws its own frame

            // in game: who's connected and how to join
            var small = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperRight, richText = true };
            int count = nm.IsServer ? nm.ConnectedClientsIds.Count : 0;
            string who = nm.IsServer ? $"{count} player{(count == 1 ? "" : "s")}" : "connected";
            GUI.Label(new Rect(Screen.width - 620, 12, 600, 22), $"{mode}   ({who})   Esc: menu", small);
            if (!string.IsNullOrEmpty(status)) GUI.Label(new Rect(Screen.width - 620, 34, 600, 22), status, small);

            if (showMenu)
            {
                GUILayout.BeginArea(new Rect(Screen.width / 2 - 160, Screen.height / 2 - 70, 320, 140), GUI.skin.box);
                GUILayout.Label("<b>Stan's Game</b>");
                if (GUILayout.Button("Back to the fight", GUILayout.Height(32)))
                {
                    showMenu = false; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
                }
                if (GUILayout.Button(nm.IsServer ? "End the game (everyone leaves)" : "Leave the game", GUILayout.Height(32))) Leave();
                GUILayout.EndArea();
            }
        }

        /// <summary>Second step after Play alone / Host: the level select screen (Scripts/UI/LevelSelect.cs).</summary>
        string ModeLabel => pending == "alone" ? "Play alone" : pending == "lan" ? "Host  ·  home network" : "Host  ·  over the internet";

        void StartMap(string scene)
        {
            string how = pending; pending = null;
            MissionProgress.MarkStart();
            targetScene = null;
            if (scene != SceneManager.GetActiveScene().name && how != "alone")
            {
                // hosting another chapter: start the host here, then everyone loads it (OnServerStarted)
                targetScene = scene;
                if (how == "lan") HostLan(); else HostOnline();
                return;
            }
            if (scene != SceneManager.GetActiveScene().name)
            {
                // another chapter, alone: leave the network behind and load it
                SceneManager.LoadScene(scene);
                Destroy(nm.gameObject);
                return;
            }
            if (how == "alone") PlayAlone();
            else if (how == "lan") HostLan();
            else HostOnline();
        }

        /// <summary>The training field (Scenes/ProvingGround.unity): alone, no network, no mission.</summary>
        void OpenProvingGround()
        {
            if (!Application.CanStreamedLevelBeLoaded("ProvingGround")) { status = "The Proving Ground isn't built yet (Stan's Game > Build Proving Ground)"; return; }
            SceneManager.LoadScene("ProvingGround");
            Destroy(nm.gameObject);
        }

        void StartMenu()
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (pending != null)
            {
                LevelSelect.Draw(ModeLabel, pending == "alone", busy, StartMap, () => pending = null, PlayHistory);
                if (!string.IsNullOrEmpty(status)) GUI.Label(new Rect(30, Screen.height - 60, 900, 24), status);
                return;
            }
            var title = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, richText = true };
            var head = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);
            GUILayout.BeginArea(new Rect(Screen.width / 2 - 230, Screen.height / 2 - 285, 460, 590));
            GUILayout.Label("<b>STAN'S GAME</b>", title);
            GUILayout.Label("<i>Normandy - 6 June 1944</i>", new GUIStyle(title) { fontSize = 16 });
            GUILayout.Space(14);
            GUI.enabled = !busy;

            if (GUILayout.Button("Play alone", GUILayout.Height(36))) pending = "alone";
            GUILayout.Space(6);
            GUILayout.Space(12);

            GUILayout.Label("<b>Home network</b>", head);
            if (GUILayout.Button("Host a game", GUILayout.Height(32))) pending = "lan";
            GUILayout.BeginHorizontal();
            GUILayout.Label("Host's IP:", GUILayout.Width(70));
            ipText = GUILayout.TextField(ipText, 40, GUILayout.Height(26));
            if (GUILayout.Button("Join", GUILayout.Width(90), GUILayout.Height(26))) JoinLan();
            GUILayout.EndHorizontal();
            GUILayout.Label($"<size=12>This PC: {LocalIp()}</size>", head);
            GUILayout.Space(12);

            GUILayout.Label("<b>Over the internet</b>", head);
            if (GUILayout.Button("Host online (get a join code)", GUILayout.Height(32))) pending = "online";
            GUILayout.BeginHorizontal();
            GUILayout.Label("Join code:", GUILayout.Width(70));
            codeText = GUILayout.TextField(codeText, 12, GUILayout.Height(26));
            if (GUILayout.Button("Join online", GUILayout.Width(90), GUILayout.Height(26))) JoinOnline();
            GUILayout.EndHorizontal();

            GUILayout.Space(16);
            if (GUILayout.Button("Proving Ground  (try the guns, vehicles and characters)", GUILayout.Height(32))) OpenProvingGround();

            GUI.enabled = true;
            GUILayout.Space(10);
            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status, head);
            GUILayout.EndArea();
        }
    }
}
#endif
