using System;
using Steamworks;
using UnityEngine;

namespace Game.Platform.Steam
{
    /// <summary>
    /// Steam API lifetime. <see cref="TryInitialize"/> is called by the composition root before any Steam
    /// service is created; when it fails (Steam not running, no steam_appid.txt) the game falls back to
    /// the local platform instead of crashing.
    /// </summary>
    public static class SteamClient
    {
        public const uint AppId = 480; // Spacewar test app.

        public static bool IsInitialized { get; private set; }

        public static bool TryInitialize(out string error)
        {
            error = null;
            if (IsInitialized)
            {
                return true;
            }

            try
            {
                if (!Packsize.Test() || !DllCheck.Test())
                {
                    error = "Steamworks.NET binaries mismatch.";
                    return false;
                }

                if (!SteamAPI.Init())
                {
                    error = "Steam is not running or the user is not logged in.";
                    return false;
                }
            }
            catch (DllNotFoundException exception)
            {
                error = "steam_api64.dll not found: " + exception.Message;
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            IsInitialized = true;
            var runner = new GameObject("SteamCallbacks").AddComponent<SteamCallbackRunner>();
            UnityEngine.Object.DontDestroyOnLoad(runner.gameObject);
            Debug.Log($"[Steam] Initialized as {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID()}).");
            return true;
        }

        internal static void Shutdown()
        {
            if (!IsInitialized)
            {
                return;
            }

            IsInitialized = false;
            SteamAPI.Shutdown();
        }

        /// <summary>Pumps Steam callbacks once per frame and shuts the API down on exit.</summary>
        private sealed class SteamCallbackRunner : MonoBehaviour
        {
            private void Update()
            {
                if (IsInitialized)
                {
                    SteamAPI.RunCallbacks();
                }
            }

            // OnDestroy runs after every OnApplicationQuit, i.e. after Netcode shut the Steam transport down
            // and the session left its lobby; shutting Steam down earlier makes those calls throw.
            private void OnDestroy() => Shutdown();
        }
    }
}
