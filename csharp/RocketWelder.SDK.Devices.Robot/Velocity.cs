using System.Globalization;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Devices.Robot;

public enum VelocityUnit { Percentage, MmPerSecond, CmPerMinute }

/// <summary>
/// Robot motion velocity with explicit unit. Prevents bare numeric ambiguity.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<Velocity>))]
public readonly record struct Velocity : IParsable<Velocity>
{
    public double Value { get; }
    public VelocityUnit Unit { get; }

    private Velocity(double value, VelocityUnit unit) => (Value, Unit) = (value, unit);

    public static Velocity Percentage(double percent) => new(percent, VelocityUnit.Percentage);
    public static Velocity MmPerSecond(double value) => new(value, VelocityUnit.MmPerSecond);
    public static Velocity CmPerMinute(double value) => new(value, VelocityUnit.CmPerMinute);

    /// <summary>Converts to mm/s. Throws if unit is Percentage (requires robot-specific max speed).</summary>
    public double ToMmPerSecond() => Unit switch
    {
        VelocityUnit.MmPerSecond => Value,
        VelocityUnit.CmPerMinute => Value * 10.0 / 60.0,
        VelocityUnit.Percentage => throw new InvalidOperationException(
            "Cannot convert Percentage to mm/s without robot-specific max speed."),
        _ => throw new ArgumentOutOfRangeException()
    };

    /// <summary>Formats as "20%", "100mm/s", or "50cm/min".</summary>
    public override string ToString() => Unit switch
    {
        VelocityUnit.Percentage => $"{Value}%",
        VelocityUnit.MmPerSecond => $"{Value}mm/s",
        VelocityUnit.CmPerMinute => $"{Value}cm/min",
        _ => Value.ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>Parses "20%", "100mm/s", "50cm/min". Throws FormatException on unknown format.</summary>
    public static Velocity Parse(string s, IFormatProvider? provider = null)
    {
        if (!TryParse(s, provider, out var result))
            throw new FormatException($"Unknown velocity format: '{s}'. Expected formats: '20%', '100mm/s', '50cm/min'.");
        return result;
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out Velocity result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        s = s.Trim();

        if (s.EndsWith('%'))
        {
            if (double.TryParse(s.AsSpan(0, s.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            {
                result = Percentage(pct);
                return true;
            }
        }
        else if (s.EndsWith("mm/s", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(s.AsSpan(0, s.Length - 4), NumberStyles.Float, CultureInfo.InvariantCulture, out var mms))
            {
                result = MmPerSecond(mms);
                return true;
            }
        }
        else if (s.EndsWith("cm/min", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(s.AsSpan(0, s.Length - 6), NumberStyles.Float, CultureInfo.InvariantCulture, out var cmm))
            {
                result = CmPerMinute(cmm);
                return true;
            }
        }

        return false;
    }
}
