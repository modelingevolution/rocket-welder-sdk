namespace RocketWelder.SDK.Devices.Robot;

/// <summary>
/// Oscillation shape a weaving robot traces while a weave is active. The integer value is
/// <b>load-bearing</b>: it equals the Fairino controller's <c>weaveType</c> code, so a driver can map a
/// <see cref="WeaveProfile"/> straight onto <c>WeaveSetPara</c> without a translation table. Do not renumber.
/// Fairino's code 7 (a second vertical sine) is not offered by the pendant editor and is intentionally absent.
/// </summary>
public enum WeaveSwingType
{
    /// <summary>Triangular wave swing (LIN/ARC). Fairino <c>weaveType</c> 0.</summary>
    Triangular = 0,

    /// <summary>Vertical L-shaped triangular wave swing (LIN/ARC). Fairino <c>weaveType</c> 1.</summary>
    VerticalLTriangular = 1,

    /// <summary>Circular oscillation, clockwise (LIN). Fairino <c>weaveType</c> 2.</summary>
    CircularCw = 2,

    /// <summary>Circular oscillation, counter-clockwise (LIN). Fairino <c>weaveType</c> 3.</summary>
    CircularCcw = 3,

    /// <summary>Sine wave swing (LIN/ARC). Fairino <c>weaveType</c> 4.</summary>
    Sine = 4,

    /// <summary>Vertical L-shaped sine wave swing (LIN/ARC). Fairino <c>weaveType</c> 5.</summary>
    VerticalLSine = 5,

    /// <summary>Vertical welding triangle swing. Fairino <c>weaveType</c> 6.</summary>
    VerticalTriangle = 6
}
