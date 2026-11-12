using System;
using Game.Domain.Session;
using Unity.Collections;
using Unity.Netcode;

namespace Game.Net.Session
{
    /// <summary>Binary codec for the connection approval payload.</summary>
    public static class ConnectionPayloadCodec
    {
        private const byte FormatVersion = 1;

        public static byte[] Encode(in ConnectionRequest request)
        {
            using (var writer = new FastBufferWriter(256, Allocator.Temp, 1024))
            {
                writer.WriteValueSafe(FormatVersion);
                writer.WriteValueSafe(request.BuildVersion);
                writer.WriteValueSafe(request.PlayerId.Value);
                writer.WriteValueSafe(request.DisplayName);
                return writer.ToArray();
            }
        }

        public static bool TryDecode(byte[] payload, out ConnectionRequest request)
        {
            request = default;
            if (payload == null || payload.Length == 0 || payload.Length > 1024)
            {
                return false;
            }

            try
            {
                using (var reader = new FastBufferReader(payload, Allocator.Temp))
                {
                    reader.ReadValueSafe(out byte format);
                    if (format != FormatVersion)
                    {
                        return false;
                    }

                    reader.ReadValueSafe(out string buildVersion);
                    reader.ReadValueSafe(out string playerId);
                    reader.ReadValueSafe(out string displayName);
                    request = new ConnectionRequest(buildVersion, new PlayerId(playerId), displayName);
                    return true;
                }
            }
            catch (Exception)
            {
                // Malformed payload from an untrusted peer.
                return false;
            }
        }
    }

    /// <summary>
    /// Netcode carries the disconnect reason as a free-form string. We prefix our enum so we can tell
    /// our own reasons apart from library/transport messages.
    /// </summary>
    public static class DisconnectReasonCodec
    {
        private const string Prefix = "ADG:";

        public static string Encode(DisconnectReason reason) => Prefix + ((byte)reason).ToString();

        public static bool TryDecode(string text, out DisconnectReason reason)
        {
            reason = DisconnectReason.None;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int index = text.IndexOf(Prefix, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            int start = index + Prefix.Length;
            int end = start;
            while (end < text.Length && char.IsDigit(text[end]))
            {
                end++;
            }

            if (end == start || !byte.TryParse(text.Substring(start, end - start), out byte value)
                || !Enum.IsDefined(typeof(DisconnectReason), value))
            {
                return false;
            }

            reason = (DisconnectReason)value;
            return true;
        }
    }
}
