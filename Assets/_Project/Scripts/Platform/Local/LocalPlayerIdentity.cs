using System;
using System.Threading;
using Game.Application.Ports;
using Game.Domain.Session;
using UnityEngine;

namespace Game.Platform.Local
{
    public sealed class LocalIdentityOptions
    {
        /// <summary>Explicit profile slot (-profile N). Null = pick the first free slot automatically.</summary>
        public int? Profile { get; }
        public string NameOverride { get; }

        public LocalIdentityOptions(int? profile, string nameOverride)
        {
            Profile = profile;
            NameOverride = nameOverride;
        }
    }

    /// <summary>
    /// PlayerId = GUID persisted in PlayerPrefs. Several game instances on one PC share PlayerPrefs, so each
    /// running instance claims a profile slot through a named mutex and gets its own GUID; otherwise the
    /// host would reject the second instance as a duplicate player.
    /// </summary>
    public sealed class LocalPlayerIdentity : IPlayerIdentity, IDisposable
    {
        private const int MaxProfiles = 8;
        private const string MutexPrefix = "ArtDockCoopProfile_";

        private readonly int _profile;
        private Mutex _profileMutex;
        private string _displayName;

        public LocalPlayerIdentity(LocalIdentityOptions options)
        {
            _profile = options.Profile ?? ClaimFreeProfile();
            string idKey = $"player_id_{_profile}";
            string id = PlayerPrefs.GetString(idKey, string.Empty);
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(idKey, id);
                PlayerPrefs.Save();
            }

            PlayerId = new PlayerId(id);
            _displayName = !string.IsNullOrWhiteSpace(options.NameOverride)
                ? options.NameOverride
                : PlayerPrefs.GetString(NameKey, $"Player-{id.Substring(0, 4)}");
            Debug.Log($"[Identity] Profile {_profile}, PlayerId {id}.");
        }

        public PlayerId PlayerId { get; }
        public string DisplayName => _displayName;
        public bool CanRename => true;

        private string NameKey => $"player_name_{_profile}";

        public void Rename(string displayName)
        {
            _displayName = ApprovalPolicy.SanitizeName(displayName);
            PlayerPrefs.SetString(NameKey, _displayName);
            PlayerPrefs.Save();
        }

        public void Dispose()
        {
            if (_profileMutex == null)
            {
                return;
            }

            try
            {
                _profileMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned by this thread anymore; disposing the handle is enough.
            }

            _profileMutex.Dispose();
            _profileMutex = null;
        }

        private int ClaimFreeProfile()
        {
            for (int slot = 0; slot < MaxProfiles; slot++)
            {
                try
                {
                    var mutex = new Mutex(true, MutexPrefix + slot, out bool createdNew);
                    if (createdNew)
                    {
                        // Held for the process lifetime; the OS releases it if the process dies.
                        _profileMutex = mutex;
                        return slot;
                    }

                    mutex.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Identity] Profile mutex unavailable: {exception.Message}");
                    break;
                }
            }

            return 0;
        }
    }
}
