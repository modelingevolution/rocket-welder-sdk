using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Ui
{
    [JsonConverter(typeof(JsonParsableConverter<ControlType>))]
    public readonly record struct ControlType : IParsable<ControlType>
    {
        private readonly string _value;

        private ControlType(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }
        public static ControlType From(String value) => new ControlType(value);
        // Implicit conversion from string to RegionName
        public static implicit operator ControlType(string value) => new ControlType(value);

        // Implicit conversion from RegionName to string
        public static implicit operator string(ControlType regionName) => regionName._value;

        // IParsable implementation
        public static ControlType Parse(string s, IFormatProvider? provider)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new FormatException("RegionName cannot be null or whitespace");

            return new ControlType(s);
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out ControlType result)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                result = default;
                return false;
            }

            result = new ControlType(s);
            return true;
        }

        public override string ToString() => _value ?? string.Empty;

        public const string IconButton = "IconButton";
        public const string ArrowGrid = "ArrowGrid";
        public const string Label = "Label";
    }

    /// <summary>
    /// Strongly-typed region name for UI control placement.
    /// </summary>
    [JsonConverter(typeof(JsonParsableConverter<RegionName>))]
    public readonly record struct RegionName : IParsable<RegionName>
    {
        private readonly string _value;

        private RegionName(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }
        public static RegionName From(String value) => new RegionName(value);
        // Implicit conversion from string to RegionName
        public static implicit operator RegionName(string value) => new RegionName(value);

        // Implicit conversion from RegionName to string
        public static implicit operator string(RegionName regionName) => regionName._value;

        // IParsable implementation
        public static RegionName Parse(string s, IFormatProvider? provider)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new FormatException("RegionName cannot be null or whitespace");
            
            return new RegionName(s);
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out RegionName result)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                result = default;
                return false;
            }

            result = new RegionName(s);
            return true;
        }

        public override string ToString() => _value ?? string.Empty;

        // Predefined region names
        public static readonly RegionName Top = "Top";
        public static readonly RegionName TopLeft = "TopLeft";
        public static readonly RegionName TopRight = "TopRight";
        public static readonly RegionName BottomLeft = "BottomLeft";
        public static readonly RegionName BottomRight = "BottomRight";
        public static readonly RegionName Bottom = "Bottom";
        
        // Legacy names for compatibility
        public static readonly RegionName PreviewTop = "preview-top";
        public static readonly RegionName PreviewTopLeft = "preview-top-left";
        public static readonly RegionName PreviewTopRight = "preview-top-right";
        public static readonly RegionName PreviewBottomLeft = "preview-bottom-left";
        public static readonly RegionName PreviewBottomRight = "preview-bottom-right";
        public static readonly RegionName PreviewBottom = "preview-bottom";
        public static readonly RegionName PreviewBottomCenter = "preview-bottom-center";
    }
}