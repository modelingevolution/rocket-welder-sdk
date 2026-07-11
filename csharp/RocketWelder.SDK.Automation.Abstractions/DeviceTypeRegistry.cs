namespace RocketWelder.SDK.Automation;

/// <summary>
/// Registry of all known peripheral device types. Populated at startup by the plugin loader
/// calling each plugin's <c>Configure(IPluginContext)</c>.
///
/// <para>
/// <see cref="Items"/> is a plain sorted <see cref="IReadOnlyList{T}"/>, ordered by
/// <see cref="DeviceTypeInfo.InterfaceType"/> then <see cref="DeviceTypeInfo.DisplayName"/>.
/// Not an <c>ObservableCollection</c> — hosts that need change notification can subscribe to
/// the <see cref="Registered"/> event.
/// </para>
/// </summary>
public class DeviceTypeRegistry
{
    private readonly Dictionary<string, DeviceTypeInfo> _byDeviceType = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<DeviceTypeInfo>> _byInterfaceType = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DeviceTypeInfo> _sorted = [];

    /// <summary>
    /// All registered device types, sorted by <see cref="DeviceTypeInfo.InterfaceType"/>
    /// then <see cref="DeviceTypeInfo.DisplayName"/>. Stable snapshot — obtain a fresh
    /// reference if registrations happen concurrently (startup-only in practice).
    /// </summary>
    public IReadOnlyList<DeviceTypeInfo> Items => _sorted;

    /// <summary>Raised each time a device type is registered (or re-registered).</summary>
    public event Action<DeviceTypeInfo>? Registered;

    /// <summary>
    /// Registers or replaces a device type. If a type with the same
    /// <see cref="DeviceTypeInfo.DeviceType"/> was already registered, it is replaced
    /// and the sorted list is updated in-place.
    /// </summary>
    public void Register(DeviceTypeInfo info)
    {
        // Remove old entry if re-registering
        if (_byDeviceType.TryGetValue(info.DeviceType, out var old))
        {
            _sorted.Remove(old);
            if (_byInterfaceType.TryGetValue(old.InterfaceType, out var oldList))
                oldList.RemoveAll(x => x.DeviceType == old.DeviceType);
        }

        _byDeviceType[info.DeviceType] = info;

        // Insert sorted
        var index = _sorted.BinarySearch(info);
        _sorted.Insert(index < 0 ? ~index : index, info);

        if (!_byInterfaceType.TryGetValue(info.InterfaceType, out var list))
        {
            list = [];
            _byInterfaceType[info.InterfaceType] = list;
        }
        list.RemoveAll(x => x.DeviceType == info.DeviceType);
        list.Add(info);

        Registered?.Invoke(info);
    }

    /// <summary>Returns the <see cref="DeviceTypeInfo"/> for the given <paramref name="deviceType"/>, or <see langword="null"/>.</summary>
    public DeviceTypeInfo? Get(string deviceType)
        => _byDeviceType.TryGetValue(deviceType, out var info) ? info : null;

    /// <summary>Returns all device types registered for the given <paramref name="interfaceType"/>.</summary>
    public IReadOnlyList<DeviceTypeInfo> GetByInterface(string interfaceType)
        => _byInterfaceType.TryGetValue(interfaceType, out var list) ? list : [];

    /// <summary>Returns the set of distinct interface type strings that have been registered.</summary>
    public IReadOnlyCollection<string> GetInterfaceTypes() => _byInterfaceType.Keys;
}
