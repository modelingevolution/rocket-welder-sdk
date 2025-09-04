using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Ui
{
    /// <summary>
    /// Strongly-typed color value for UI controls.
    /// </summary>
    [JsonConverter(typeof(JsonParsableConverter<Color>))]
    public readonly record struct Color : IParsable<Color>
    {
        private readonly string _value;

        private Color(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        // Implicit conversion from string to Color
        public static implicit operator Color(string value) => new Color(value);

        // Implicit conversion from Color to string
        public static implicit operator string(Color color) => color._value;

        // IParsable implementation
        public static Color Parse(string s, IFormatProvider? provider)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new FormatException("Color cannot be null or whitespace");
            
            return new Color(s);
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Color result)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                result = default;
                return false;
            }

            result = new Color(s);
            return true;
        }

        public override string ToString() => _value ?? string.Empty;

        // Predefined semantic colors
        public static readonly Color Primary = "Primary";
        public static readonly Color Secondary = "Secondary";
        public static readonly Color Error = "Error";
        public static readonly Color Warning = "Warning";
        public static readonly Color Info = "Info";
        public static readonly Color Success = "Success";
        public static readonly Color Default = "Default";
        
    }
}