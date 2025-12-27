using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Shared;

/// <summary>
/// Strongly-typed hostname identifier with case-insensitive comparison.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<HostName>))]
[DataContract]
public readonly struct HostName : IEquatable<HostName>, IComparable<HostName>,
    IComparable, IParsable<HostName>
{
    public static readonly HostName Empty = new(string.Empty);
    public static readonly HostName Localhost = new("localhost");

    [DataMember(Order = 1)]
    private readonly string _name;

    private HostName(string n) => _name = n;

    public static explicit operator HostName(string hostname) => new(hostname);
    public static implicit operator string(HostName hostname) => hostname.ToString();
    public static HostName From(string name) => new(name);

    public bool Equals(HostName other) =>
        string.Equals(_name, other._name, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is HostName other && Equals(other);

    public override int GetHashCode() =>
        _name != null ? _name.GetHashCode(StringComparison.OrdinalIgnoreCase) : 0;

    public override string ToString() => _name ?? string.Empty;

    public static bool operator ==(HostName left, HostName right) => left.Equals(right);
    public static bool operator !=(HostName left, HostName right) => !left.Equals(right);

    public int CompareTo(HostName other) =>
        string.Compare(_name, other._name, StringComparison.OrdinalIgnoreCase);

    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        return obj is HostName other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(HostName)}");
    }

    public static HostName Parse(string s, IFormatProvider? provider = null) => From(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out HostName result)
    {
        result = From(s ?? string.Empty);
        return true;
    }
}
