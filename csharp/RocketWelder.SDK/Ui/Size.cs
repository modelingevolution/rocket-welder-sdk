using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Ui
{
    /// <summary>
    /// Strongly-typed size value for UI controls.
    /// </summary>
    [JsonConverter(typeof(JsonParsableConverter<Size>))]
    public readonly record struct Size : IParsable<Size>
    {
        private readonly string _value;

        private Size(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        // Implicit conversion from string to Size
        public static implicit operator Size(string value) => new Size(value);

        // Implicit conversion from Size to string
        public static implicit operator string(Size size) => size._value;

        // IParsable implementation
        public static Size Parse(string s, IFormatProvider? provider)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new FormatException("Size cannot be null or whitespace");
            
            return new Size(s);
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Size result)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                result = default;
                return false;
            }

            result = new Size(s);
            return true;
        }

        public override string ToString() => _value ?? string.Empty;

        // Predefined sizes (following MudBlazor pattern)
        public static readonly Size Small = "Small";
        public static readonly Size Medium = "Medium";
        public static readonly Size Large = "Large";
        
        // Additional common sizes
        public static readonly Size ExtraSmall = "ExtraSmall";
        public static readonly Size ExtraLarge = "ExtraLarge";
        
        // Numeric sizes for specific use cases
        public static readonly Size Size8 = "8";
        public static readonly Size Size16 = "16";
        public static readonly Size Size24 = "24";
        public static readonly Size Size32 = "32";
        public static readonly Size Size40 = "40";
        public static readonly Size Size48 = "48";
        public static readonly Size Size56 = "56";
        public static readonly Size Size64 = "64";
        public static readonly Size Size128 = "128";
    }
}