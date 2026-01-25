namespace RocketWelder.SDK.Automation;

/// <summary>
/// Represents a workspace for grouping related programs.
/// </summary>
public readonly record struct Workspace : IParsable<Workspace>
{
    private readonly string _value;

    public Workspace(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    public static Workspace Parse(string s, IFormatProvider? provider) => new(s);

    public static bool TryParse(string? s, IFormatProvider? provider, out Workspace result)
    {
        if (string.IsNullOrEmpty(s))
        {
            result = default;
            return false;
        }
        result = new(s);
        return true;
    }

    public override string ToString() => _value;
    public static implicit operator string(Workspace w) => w._value;
}
