namespace RocketWelder.SDK.Automation.Plugins;

/// <summary>
/// Marks a class as a RocketWelder plugin. The host's plugin loader discovers
/// all types decorated with this attribute and instantiates them as <see cref="IPlugin"/>
/// at startup. See device-extensibility.md §6.1 for the full contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RocketWelderPluginAttribute : Attribute
{
    /// <summary>
    /// Initializes the attribute with a display/diagnostic name used for logging and
    /// error messages (e.g., "Fronius", "RocketWelder.BuiltIn").
    /// </summary>
    public RocketWelderPluginAttribute(string name) => Name = name;

    /// <summary>Display/diagnostic name of the plugin (e.g., "Fronius").</summary>
    public string Name { get; }
}
