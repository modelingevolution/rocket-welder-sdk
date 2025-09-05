using System;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Ui;

[JsonConverter(typeof(JsonParsableConverter<KeyCode>))]
public readonly record struct KeyCode : IParsable<KeyCode>
{
    private readonly string _value;
    private KeyCode(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
    public static implicit operator KeyCode(string value) => new KeyCode(value);
    public static implicit operator string(KeyCode keyCode) => keyCode._value;
    public override string ToString() => _value ?? string.Empty;
    public static KeyCode Parse(string s, IFormatProvider? provider)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new FormatException("KeyValue cannot be null or whitespace");
        return new KeyCode(s);
    }
    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out KeyCode result)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            result = default;
            return false;
        }
        result = new KeyCode(s);
        return true;
    }
    public static readonly KeyCode ArrowUp = "ArrowUp";
    public static readonly KeyCode ArrowDown = "ArrowDown";
    public static readonly KeyCode ArrowLeft = "ArrowLeft";
    public static readonly KeyCode ArrowRight = "ArrowRight";
    public static readonly KeyCode Enter = "Enter";
    public static readonly KeyCode Escape = "Escape";
    public static readonly KeyCode Space = "Space";
    public static readonly KeyCode Tab = "Tab";
    public static readonly KeyCode Backspace = "Backspace";
    public static readonly KeyCode Delete = "Delete";
    public static readonly KeyCode Shift = "Shift";
    public static readonly KeyCode Control = "Control";
    public static readonly KeyCode Alt = "Alt";
    public static readonly KeyCode Meta = "Meta"; // Windows key or Command key
    public static readonly KeyCode KeyA = "KeyA";
    public static readonly KeyCode KeyB = "KeyB";

}