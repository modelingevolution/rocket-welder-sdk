namespace RocketWelder.SDK.Automation;

/// <summary>
/// Strongly-typed, named tag for PLC I/O. Programs define tags as constants;
/// the platform maps them to protocol-specific addresses.
/// </summary>
/// <typeparam name="T">The CLR type of the tag value (bool, ushort, short, int, uint, float).</typeparam>
public readonly record struct PlcTag<T>(string Name) where T : unmanaged
{
    public static implicit operator PlcTag<T>(string name) => new(name);

    public override string ToString() => Name;
}
