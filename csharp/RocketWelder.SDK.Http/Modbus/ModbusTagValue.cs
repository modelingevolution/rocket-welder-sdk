namespace RocketWelder.SDK.Http.Modbus;

/// <summary>
/// Value read from a named tag on a Modbus device. Wire-friendly shape — the
/// raw value is JSON-encoded and accompanied by a type hint so the caller can
/// deserialise without a separate schema lookup.
/// </summary>
/// <param name="DeviceName">Modbus device the tag belongs to.</param>
/// <param name="TagName">Tag name from the device's tag mapping.</param>
/// <param name="ValueJson">Raw value serialised as JSON (e.g. <c>"42"</c>, <c>"true"</c>, <c>"\"running\""</c>, <c>"3.14"</c>).</param>
/// <param name="TypeHint">CLR-ish type hint: <c>"int16"</c>, <c>"uint16"</c>, <c>"int32"</c>, <c>"float32"</c>, <c>"bool"</c>, <c>"string"</c>, ...</param>
/// <param name="ReadAtUtc">When the value was read off the wire, UTC.</param>
public sealed record ModbusTagValue(
    string DeviceName,
    string TagName,
    string ValueJson,
    string TypeHint,
    DateTime ReadAtUtc);
