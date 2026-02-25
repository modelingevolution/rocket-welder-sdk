namespace RocketWelder.SDK.Automation;

/// <summary>
/// Thrown when a program reads/writes a PLC tag that has no mapping
/// in the platform-side tag map.
/// </summary>
public class UnknownTagException(string tagName)
    : InvalidOperationException($"PLC tag '{tagName}' is not mapped.")
{
    public string TagName { get; } = tagName;
}
