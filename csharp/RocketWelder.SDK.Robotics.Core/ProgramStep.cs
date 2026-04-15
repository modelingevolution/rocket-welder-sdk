using System.Text.Json.Serialization;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Base type for a single step in a <see cref="RobotProgram"/>. Immutable; step variants are records.
/// JSON discriminator: <c>type</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MoveJointStep), "moveJoint")]
[JsonDerivedType(typeof(MoveLinStep), "moveLin")]
public abstract record ProgramStep;

/// <summary>Move to the given joint configuration (degrees).</summary>
public sealed record MoveJointStep(Joints6<double> Target) : ProgramStep;

/// <summary>Move the TCP linearly to the given pose (mm, degrees).</summary>
public sealed record MoveLinStep(Pose3<double> Target) : ProgramStep;
