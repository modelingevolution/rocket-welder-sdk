using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Automation;

/// <summary>Device health status as tracked by the read model.</summary>
public enum PeripheralDeviceStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>
/// Live state of one registered peripheral device as maintained by
/// <see cref="PeripheralDevicesReadModel"/>. Implements <see cref="INotifyPropertyChanged"/>
/// for Blazor/WPF binding.
///
/// <para>
/// The <see cref="Instance"/> property holds the device object created by
/// <see cref="DeviceTypeInfo.Factory"/>. The host can cast it to the appropriate
/// interface (e.g., <c>IWeldingMachine</c>) keyed on <see cref="InterfaceType"/>.
/// </para>
/// </summary>
public class PeripheralDeviceItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private int _number;
    private PeripheralDeviceStatus _status = PeripheralDeviceStatus.Disconnected;
    private string? _statusMessage;
    private ConfigSet _config = ConfigSet.Empty;
    private object? _instance;

    /// <summary>Stable identity of this device (minted at creation, never changes).</summary>
    public DeviceId Id { get; init; }

    /// <summary>The CLR type discriminator string (e.g., "FroniusWeldingMachine_TPS5000").</summary>
    public string DeviceType { get; init; } = string.Empty;

    /// <summary>The interface-role string (e.g., "IWeldingMachine").</summary>
    public string InterfaceType { get; init; } = string.Empty;

    /// <summary>Current config snapshot (updated by <c>PeripheralDeviceConfigured</c> events).</summary>
    public ConfigSet Config
    {
        get => _config;
        set => SetField(ref _config, value);
    }

    /// <summary>Operator-assigned name (unique within interface type).</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>1-based ordinal within the interface type (gap-fills on removal).</summary>
    public int Number
    {
        get => _number;
        set => SetField(ref _number, value);
    }

    /// <summary>Current device health as observed by the read model.</summary>
    public PeripheralDeviceStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    /// <summary>Human-readable status detail (error message, "Streaming", etc.).</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    /// <summary>
    /// The live device object created by <see cref="DeviceTypeInfo.Factory"/>.
    /// Replaced whenever the config changes (rebuild). Hosts cast to the concrete
    /// interface type keyed on <see cref="InterfaceType"/>.
    /// </summary>
    public object? Instance
    {
        get => _instance;
        set => SetField(ref _instance, value);
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
