using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Ui
{
    /// <summary>
    /// Strongly-typed typography value for UI controls.
    /// </summary>
    [JsonConverter(typeof(JsonParsableConverter<Typography>))]
    public readonly record struct Typography : IParsable<Typography>
    {
        private readonly string _value;

        private Typography(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        // Implicit conversion from string to Typography
        public static implicit operator Typography(string value) => new Typography(value);

        // Implicit conversion from Typography to string
        public static implicit operator string(Typography typography) => typography._value;

        // IParsable implementation
        public static Typography Parse(string s, IFormatProvider? provider)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new FormatException("Typography cannot be null or whitespace");
            
            return new Typography(s);
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Typography result)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                result = default;
                return false;
            }

            result = new Typography(s);
            return true;
        }

        public override string ToString() => _value ?? string.Empty;

        // Predefined typography values
        public static readonly Typography H1 = "h1";
        public static readonly Typography H2 = "h2";
        public static readonly Typography H3 = "h3";
        public static readonly Typography H4 = "h4";
        public static readonly Typography H5 = "h5";
        public static readonly Typography H6 = "h6";
        public static readonly Typography Subtitle1 = "subtitle1";
        public static readonly Typography Subtitle2 = "subtitle2";
        public static readonly Typography Body1 = "body1";
        public static readonly Typography Body2 = "body2";
        public static readonly Typography Caption = "caption";
        public static readonly Typography Button = "button";
        public static readonly Typography Overline = "overline";
    }
}