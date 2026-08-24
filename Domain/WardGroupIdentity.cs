using System;

namespace STUWard;

internal readonly struct WardGroupIdentity : IEquatable<WardGroupIdentity>
{
    internal WardGroupIdentity(string provider, string id, string name)
    {
        Provider = provider?.Trim() ?? string.Empty;
        Id = id?.Trim() ?? string.Empty;
        Name = name?.Trim() ?? string.Empty;
    }

    internal string Provider { get; }
    internal string Id { get; }
    internal string Name { get; }
    internal bool IsValid => !string.IsNullOrWhiteSpace(Provider) && !string.IsNullOrWhiteSpace(Id);

    public bool Equals(WardGroupIdentity other)
    {
        return string.Equals(Provider, other.Provider, StringComparison.Ordinal) &&
               string.Equals(Id, other.Id, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is WardGroupIdentity other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(Provider ?? string.Empty) * 397) ^
                   StringComparer.Ordinal.GetHashCode(Id ?? string.Empty);
        }
    }

    public static bool operator ==(WardGroupIdentity left, WardGroupIdentity right) => left.Equals(right);
    public static bool operator !=(WardGroupIdentity left, WardGroupIdentity right) => !left.Equals(right);
}
