using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace StansGame.EditorTools
{
    /// <summary>
    /// Menu: Stan's Game > Install Multiplayer Packages.
    /// Adds Netcode for GameObjects (2.x, the line for Unity 6.6) and the Multiplayer Services SDK (Relay join codes),
    /// then switches on the STANS_NET scripting define so the multiplayer code compiles. Until then the game builds
    /// as the single-player version, so a missing package can never break the project.
    /// </summary>
    public static class NetPackagesInstaller
    {
        const string Define = "STANS_NET";
        static AddAndRemoveRequest request;

        [MenuItem("Stan's Game/Install Multiplayer Packages", priority = 30)]
        public static void Install()
        {
            if (request != null && !request.IsCompleted) { Debug.Log("Stan's Game: package install already running..."); return; }
            Debug.Log("Stan's Game: installing Netcode for GameObjects and Multiplayer Services...");
            request = Client.AddAndRemove(new[] { "com.unity.netcode.gameobjects@2.14.0", "com.unity.services.multiplayer" }, null);
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (request == null || !request.IsCompleted) return;
            EditorApplication.update -= Poll;
            if (request.Status == StatusCode.Success)
            {
                foreach (var p in request.Result)
                    if (p.name.Contains("netcode") || p.name.Contains("multiplayer") || p.name.Contains("transport"))
                        Debug.Log($"Stan's Game: package {p.name} {p.version}");
                SetDefine(true);
                Debug.Log("Stan's Game: multiplayer packages installed and STANS_NET switched on. Unity recompiles now; then run Build Pegasus Bridge Level.");
            }
            else Debug.LogError("Stan's Game: package install failed: " + request.Error?.message);
            request = null;
        }

        /// <summary>The package install reloads the editor, which can cut the callback above short: finish the job here.</summary>
        [InitializeOnLoadMethod]
        static void FinishAfterReload()
        {
            EditorApplication.delayCall += () =>
            {
                bool hasNgo = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.netcode.gameobjects") != null;
                bool hasMps = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.services.multiplayer") != null;
                PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone, out string[] defines);
                if (hasNgo && hasMps && System.Array.IndexOf(defines, Define) < 0 && !SessionState.GetBool("stans.netoff", false))
                {
                    Debug.Log("Stan's Game: multiplayer packages found - switching on the multiplayer code (STANS_NET).");
                    SetDefine(true);
                }
            };
        }

        [MenuItem("Stan's Game/Switch Multiplayer Code On", priority = 31)]
        public static void On() { SessionState.SetBool("stans.netoff", false); SetDefine(true); }

        [MenuItem("Stan's Game/Switch Multiplayer Code Off", priority = 32)]
        public static void Off() { SessionState.SetBool("stans.netoff", true); SetDefine(false); }

        static void SetDefine(bool on)
        {
            var target = NamedBuildTarget.Standalone;
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
            var list = new System.Collections.Generic.List<string>(defines);
            if (on && !list.Contains(Define)) list.Add(Define);
            if (!on) list.Remove(Define);
            PlayerSettings.SetScriptingDefineSymbols(target, list.ToArray());
        }
    }
}
