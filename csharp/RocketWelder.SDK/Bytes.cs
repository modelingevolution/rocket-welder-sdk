using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK;

[JsonConverter(typeof(JsonParsableConverter<Bytes>))]
public struct Bytes : IComparable<Bytes>, IEquatable<Bytes>, IParsable<Bytes>
{
    public static readonly Bytes Zero = new Bytes(0);
    private string? _text;
    
    private readonly long _value;
    private readonly sbyte _precision;
    public Bytes(long value, sbyte precision = 0)
    {
        _value = value;
        _text = null; // Will be computed on demand
        _precision = precision;
    }

    public static Bytes FromFile(string path) => new(new FileInfo(path).Length);

    private string Text
    {
        get
        {
            if (_text != null) return _text;
            _text = _value.WithSizeSuffix(_precision);
            return _text;
        }
    }

    public int CompareTo(Bytes other) => _value.CompareTo(other._value);

    public bool Equals(Bytes other) => _value == other._value;

    public override bool Equals(object? obj) => obj is Bytes other && Equals(other);

    public override int GetHashCode() => _value.GetHashCode();

    public static bool operator ==(Bytes left, Bytes right) => left.Equals(right);

    public static bool operator !=(Bytes left, Bytes right) => !left.Equals(right);

    public static Bytes operator +(Bytes a, Bytes b) => new(a._value + b._value, a._precision);

    public static Bytes operator -(Bytes a, Bytes b) => new(a._value - b._value, a._precision);
    public static implicit operator Bytes(ulong value) => new((long)value);
    public static implicit operator Bytes(long value) => new(value);
    public static implicit operator Bytes(int value) => new(value);
    public static implicit operator Bytes(string value) => Parse(value);
    public static implicit operator double(Bytes value) => value._value;
    public static implicit operator long(Bytes value) => value._value;
    public override string ToString() => Text;

    /// <summary>
    /// Parses a string representation of bytes with optional size suffix (B, KB, MB, GB, TB, PB)
    /// </summary>
    public static Bytes Parse(string s, IFormatProvider? provider = null)
    {
        if (TryParse(s, provider, out var result))
            return result;

        throw new FormatException($"Unable to parse '{s}' as Bytes. Expected format: number with optional suffix (B, KB, MB, GB, TB, PB)");
    }

    /// <summary>
    /// Tries to parse a string representation of bytes with optional size suffix
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Bytes result)
    {
        result = Zero;

        if (string.IsNullOrWhiteSpace(s))
            return false;

        s = s.Trim();

        var culture = provider as CultureInfo ?? CultureInfo.InvariantCulture;
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator[0];
        var groupSeparator = culture.NumberFormat.NumberGroupSeparator[0];

        // Find where the number ends and suffix begins
        int i;
        for (i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (!char.IsDigit(c) && c != decimalSeparator && c != groupSeparator && c != '-')
                break;
        }

        var numberPart = s[..i];
        var suffix = s[i..].ToUpperInvariant().Trim();

        if (!double.TryParse(numberPart, NumberStyles.Number, culture, out var value))
            return false;

        // Remove trailing 'B' if present
        if (suffix.EndsWith("B"))
            suffix = suffix[..^1];

        var multiplier = suffix switch
        {
            "" => 1L,
            "K" => 1024L,
            "M" => 1024L * 1024,
            "G" => 1024L * 1024 * 1024,
            "T" => 1024L * 1024 * 1024 * 1024,
            "P" => 1024L * 1024 * 1024 * 1024 * 1024,
            "E" => 1024L * 1024 * 1024 * 1024 * 1024 * 1024,
            _ => 0L
        };

        if (multiplier == 0)
            return false;

        var bytes = (long)(value * multiplier);
        result = new Bytes(bytes);
        return true;
    }
}