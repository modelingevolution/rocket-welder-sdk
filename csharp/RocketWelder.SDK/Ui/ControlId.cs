using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Ui
{
    /// <summary>
    /// Strongly-typed identifier for external controls.
    /// </summary>
    [JsonConverter(typeof(JsonParsableConverter<ControlId>))]
    public readonly record struct ControlId : IParsable<ControlId>
    {
        private readonly string _value;

        private ControlId(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        // Implicit conversion from string to ControlId
        public static implicit operator ControlId(string value) => new ControlId(value);

        // Implicit conversion from ControlId to string
        public static implicit operator string(ControlId controlId) => controlId._value;

        // IParsable implementation
        public static ControlId Parse(string s, IFormatProvider? provider)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new FormatException("ControlId cannot be null or whitespace");
            
            return new ControlId(s);
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out ControlId result)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                result = default;
                return false;
            }

            result = new ControlId(s);
            return true;
        }

        public override string ToString() => _value ?? string.Empty;
    }
}