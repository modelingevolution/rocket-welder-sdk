using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Stable identity of one host-registered device, opaque to the SDK. Host applications
/// mint and assign these (rocket-welder2 derives them from the device-add UI's
/// <c>CreatePeripheralDevice</c> command). Carried on every device-typed interface
/// (e.g. <see cref="IRobot.Id"/>) so SDK consumers — the adaptive-point catalogue,
/// calibration store, telemetry — can scope per-device without depending on any
/// host-specific identity type.
///
/// <para>
/// Format: <c>{DeviceType}/{Guid}</c> where <c>DeviceType</c> is a short kind label
/// (typically the vendor or category, e.g. <c>"fairino"</c>, <c>"abb-egm"</c>,
/// <c>"camera"</c>) and <c>Guid</c> is the stable identifier minted at registration.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<DeviceId>))]
public readonly record struct DeviceId : IParsable<DeviceId>
{
    public string DeviceType { get; }
    public Guid Value { get; }

    public DeviceId(string deviceType, Guid value)
    {
        DeviceType = deviceType;
        Value = value;
    }

    /// <summary>Mints a new <see cref="DeviceId"/> for the given device-type label.</summary>
    public static DeviceId New(string deviceType) => new(deviceType, Guid.NewGuid());

    /// <summary>Parses a bare <c>"DeviceType"</c> (Guid.Empty) or a full <c>"DeviceType/Guid"</c>.</summary>
    public static DeviceId Parse(string s) =>
        s.Contains('/') ? Parse(s, null) : new(s, Guid.Empty);

    /// <inheritdoc/>
    public override string ToString() => $"{DeviceType}/{Value}";

    /// <inheritdoc/>
    public static DeviceId Parse(string s, IFormatProvider? provider)
    {
        var idx = s.IndexOf('/');
        if (idx < 0)
            throw new FormatException($"Invalid DeviceId format: '{s}'. Expected 'DeviceType/Guid'.");

        var deviceType = s[..idx];
        var guid = Guid.Parse(s[(idx + 1)..]);
        return new DeviceId(deviceType, guid);
    }

    /// <inheritdoc/>
    public static bool TryParse(string? s, IFormatProvider? provider, out DeviceId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;

        var idx = s.IndexOf('/');
        if (idx < 0) return false;

        if (!Guid.TryParse(s[(idx + 1)..], out var guid)) return false;

        result = new DeviceId(s[..idx], guid);
        return true;
    }
}
