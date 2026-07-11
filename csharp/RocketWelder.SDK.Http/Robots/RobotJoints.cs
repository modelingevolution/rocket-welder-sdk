namespace RocketWelder.SDK.Http.Robots;

/// <summary>
/// Joint positions in radians. <see cref="Values"/> typically has length 6 for
/// industrial arms (J1..J6); the server projects from <c>Joints6&lt;double&gt;</c>
/// but the wire shape is an index-based array to keep the protocol independent
/// of any specific kinematic structure.
/// </summary>
public sealed record RobotJoints(IReadOnlyList<double> Values);
