namespace RocketWelder.SDK.Automation;

/// <summary>
/// Describes a single configuration property for UI rendering (e.g., the "Add Device" dialog)
/// and write-path validation in the generic config aggregate.
/// Used by <see cref="DeviceTypeRegistry"/> to declare what properties each device type supports.
/// </summary>
/// <param name="Name">Key used in <see cref="ConfigSet"/> (case-insensitive, e.g., "Ip").</param>
/// <param name="DisplayName">Human-readable label shown in the UI (e.g., "IP Address").</param>
/// <param name="ValueType">JSON/parse type hint (e.g., "string", "int", "bool").</param>
/// <param name="Required">Whether the property must be present when creating a device.</param>
/// <param name="Default">Default value string (optional); shown as placeholder in the UI.</param>
/// <param name="Group">Optional group heading for multi-section UI layout.</param>
public record ConfigPropertySchema(
    string Name,
    string DisplayName,
    string ValueType,
    bool Required,
    string? Default = null,
    string? Group = null);
