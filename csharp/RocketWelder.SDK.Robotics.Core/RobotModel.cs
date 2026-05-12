using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Immutable definition of a 6-DOF articulated robot arm.
/// Created once, shared across SimulatedRobot instances. Thread-safe.
/// </summary>
public sealed class RobotModel
{
    /// <summary>Name of this robot model (e.g., "Fairino FR5").</summary>
    public string Name { get; }

    /// <summary>Modified DH (Craig convention) parameters for all 6 joints.</summary>
    public IReadOnlyList<DhJoint> DhChain { get; }

    /// <summary>Joint angle limits for all 6 joints.</summary>
    public IReadOnlyList<JointLimit> JointLimits { get; }

    /// <summary>Home position (all joints at their default angles).</summary>
    public Joints6<double> HomePosition { get; }

    public RobotModel(
        string name,
        IReadOnlyList<DhJoint> dhChain,
        IReadOnlyList<JointLimit> jointLimits,
        Joints6<double> homePosition)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(dhChain);
        ArgumentNullException.ThrowIfNull(jointLimits);

        if (dhChain.Count != 6)
            throw new ArgumentException("DH chain must have exactly 6 joints.", nameof(dhChain));
        if (jointLimits.Count != 6)
            throw new ArgumentException("Joint limits must have exactly 6 entries.", nameof(jointLimits));

        Name = name;
        DhChain = dhChain;
        JointLimits = jointLimits;
        HomePosition = homePosition;
    }

    /// <summary>
    /// Validates joint angles against limits. Returns an empty list if all joints are within limits.
    /// </summary>
    public IReadOnlyList<JointLimitViolation> ValidateJoints(Joints6<double> joints)
    {
        var violations = new List<JointLimitViolation>();
        for (int i = 0; i < 6; i++)
        {
            var angleDeg = (double)joints[i];
            var limit = JointLimits[i];
            var overshoot = limit.Overshoot(angleDeg);
            if (overshoot != 0)
            {
                var limitValue = overshoot > 0 ? limit.MaxDeg : limit.MinDeg;
                violations.Add(new JointLimitViolation(i, limitValue, angleDeg, Math.Abs(overshoot)));
            }
        }
        return violations;
    }
}
