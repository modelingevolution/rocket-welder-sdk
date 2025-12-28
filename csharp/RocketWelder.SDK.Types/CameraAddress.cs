using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Types;

/// <summary>
/// Represents a camera address in the form of {HostName}/{CameraNumber} or just {HostName}.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<CameraAddress>))]
[DataContract]
public readonly struct CameraAddress : IParsable<CameraAddress>, IEquatable<CameraAddress>
{
    [DataMember(Order = 1)]
    public HostName HostName { get; init; }

    [DataMember(Order = 2)]
    public int? CameraNumber { get; init; }

    public CameraAddress() { }

    public CameraAddress(string hostName, int? cameraNr)
    {
        HostName = HostName.Parse(hostName);
        CameraNumber = cameraNr;
    }

    public CameraAddress(HostName hostName, int? cameraNr)
    {
        HostName = hostName;
        CameraNumber = cameraNr;
    }

    public override string ToString() =>
        CameraNumber != null ? $"{HostName}/{CameraNumber}" : HostName;

    public static CameraAddress Parse(string s, IFormatProvider? provider = null)
    {
        int b = s.LastIndexOf('/');
        if (b == -1)
            return new CameraAddress { HostName = HostName.Parse(s) };

        string h = s[..b];
        int nr = int.Parse(s[(b + 1)..]);
        return new CameraAddress { HostName = HostName.Parse(h), CameraNumber = nr };
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CameraAddress result)
    {
        result = default;
        if (string.IsNullOrEmpty(s))
            return false;

        int b = s.LastIndexOf('/');
        if (b == -1)
        {
            result = new CameraAddress { HostName = HostName.Parse(s) };
            return true;
        }

        string h = s[..b];
        if (int.TryParse(s[(b + 1)..], out var c))
        {
            result = new CameraAddress { HostName = HostName.Parse(h), CameraNumber = c };
            return true;
        }

        result = new CameraAddress { HostName = HostName.Parse(s) };
        return false;
    }

    public override bool Equals(object? obj) => obj is CameraAddress address && Equals(address);

    public bool Equals(CameraAddress other) =>
        HostName.Equals(other.HostName) && CameraNumber == other.CameraNumber;

    public override int GetHashCode() => HashCode.Combine(HostName, CameraNumber);

    public static bool operator ==(CameraAddress left, CameraAddress right) => left.Equals(right);
    public static bool operator !=(CameraAddress left, CameraAddress right) => !(left == right);
}
