using System;

namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Represents a confidence score for ML detection results.
/// Internally stored as ushort (0-65535) for full precision.
/// Implicitly converts to float (0.0-1.0) for easy usage.
/// </summary>
/// <remarks>
/// <code>
/// // Create from float (0.0-1.0)
/// Confidence c1 = 0.95f;
///
/// // Create from ushort (raw value)
/// Confidence c2 = (Confidence)65535;
///
/// // Use as float directly (implicit conversion)
/// float normalized = c1;  // 0.95f
///
/// // Compare with float
/// if (confidence > 0.9f) { ... }
///
/// // Get raw ushort value
/// ushort raw = (ushort)confidence;
/// </code>
/// </remarks>
public readonly record struct Confidence : IComparable<Confidence>, IComparable<float>
{
    /// <summary>
    /// Maximum raw value (ushort.MaxValue = 65535).
    /// </summary>
    public const ushort MaxRaw = ushort.MaxValue;

    /// <summary>
    /// The raw ushort value (0-65535).
    /// </summary>
    public ushort Raw { get; }

    /// <summary>
    /// Creates a Confidence from a raw ushort value.
    /// </summary>
    public Confidence(ushort raw) => Raw = raw;

    /// <summary>
    /// Gets the normalized float value (0.0-1.0).
    /// </summary>
    public float Normalized => Raw / (float)MaxRaw;

    /// <summary>
    /// Full confidence (1.0).
    /// </summary>
    public static Confidence Full => new(MaxRaw);

    /// <summary>
    /// Zero confidence (0.0).
    /// </summary>
    public static Confidence Zero => new(0);

    #region Operators

    /// <summary>
    /// Implicit conversion to float (normalized 0.0-1.0).
    /// </summary>
    public static implicit operator float(Confidence c) => c.Normalized;

    /// <summary>
    /// Implicit conversion from float (clamped to 0.0-1.0, then scaled to ushort).
    /// </summary>
    public static implicit operator Confidence(float normalized)
    {
        var clamped = Math.Clamp(normalized, 0f, 1f);
        return new Confidence((ushort)(clamped * MaxRaw));
    }

    /// <summary>
    /// Explicit conversion to ushort (raw value).
    /// </summary>
    public static explicit operator ushort(Confidence c) => c.Raw;

    /// <summary>
    /// Explicit conversion from ushort (raw value).
    /// </summary>
    public static explicit operator Confidence(ushort raw) => new(raw);

    /// <summary>
    /// Comparison with float.
    /// </summary>
    public static bool operator >(Confidence c, float f) => c.Normalized > f;
    public static bool operator <(Confidence c, float f) => c.Normalized < f;
    public static bool operator >=(Confidence c, float f) => c.Normalized >= f;
    public static bool operator <=(Confidence c, float f) => c.Normalized <= f;

    public static bool operator >(float f, Confidence c) => f > c.Normalized;
    public static bool operator <(float f, Confidence c) => f < c.Normalized;
    public static bool operator >=(float f, Confidence c) => f >= c.Normalized;
    public static bool operator <=(float f, Confidence c) => f <= c.Normalized;

    #endregion

    #region IComparable

    public int CompareTo(Confidence other) => Raw.CompareTo(other.Raw);
    public int CompareTo(float other) => Normalized.CompareTo(other);

    #endregion

    /// <summary>
    /// Returns the normalized value as a percentage string (e.g., "95.0%").
    /// </summary>
    public override string ToString() => $"{Normalized:P1}";
}
