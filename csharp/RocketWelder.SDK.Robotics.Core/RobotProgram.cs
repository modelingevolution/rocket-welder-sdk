using System.Text.Json;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Mutable ordered sequence of <see cref="ProgramStep"/>s. Not thread-safe: one instance per agent.
/// </summary>
public sealed class RobotProgram
{
    private readonly List<ProgramStep> _steps = new();

    /// <summary>All steps in order.</summary>
    public IReadOnlyList<ProgramStep> Steps => _steps;

    /// <summary>Step count.</summary>
    public int Count => _steps.Count;

    /// <summary>Append a joint-space move.</summary>
    public void AddMoveJoint(Joints6<double> target) => _steps.Add(new MoveJointStep(target));

    /// <summary>Append a linear-pose move.</summary>
    public void AddMoveLin(Pose3<double> target) => _steps.Add(new MoveLinStep(target));

    /// <summary>Append a prebuilt step.</summary>
    public void Add(ProgramStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(step);
    }

    /// <summary>Insert a step at the given index.</summary>
    public void InsertAt(int index, ProgramStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (index < 0 || index > _steps.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _steps.Insert(index, step);
    }

    /// <summary>Remove the step at the given index.</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _steps.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _steps.RemoveAt(index);
    }

    /// <summary>Replace the step at the given index.</summary>
    public void ReplaceAt(int index, ProgramStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (index < 0 || index >= _steps.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _steps[index] = step;
    }

    /// <summary>Serialize this program to JSON.</summary>
    public string ToJson(JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(_steps, options ?? DefaultJson);

    /// <summary>Parse a program from JSON.</summary>
    public static RobotProgram FromJson(string json, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        var steps = JsonSerializer.Deserialize<List<ProgramStep>>(json, options ?? DefaultJson)
                    ?? throw new InvalidOperationException("Program JSON deserialised to null.");
        var program = new RobotProgram();
        foreach (var step in steps)
            program.Add(step);
        return program;
    }

    internal static readonly JsonSerializerOptions DefaultJson = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
