using System;

namespace Game.Domain.Session
{
    /// <summary>
    /// Stable, platform-provided player identity (local GUID or SteamID64).
    /// Unlike the transport-level client id, it survives reconnects and is used for duplicate detection.
    /// </summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public const int MaxLength = 64;

        public string Value { get; }

        public PlayerId(string value)
        {
            Value = value ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrWhiteSpace(Value) && Value.Length <= MaxLength;

        public bool Equals(PlayerId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PlayerId other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : Value.GetHashCode();

        public override string ToString() => Value;

        public static bool operator ==(PlayerId left, PlayerId right) => left.Equals(right);

        public static bool operator !=(PlayerId left, PlayerId right) => !left.Equals(right);
    }
}
