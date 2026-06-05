namespace RocketWelder.SDK.Runtime;

/// <summary>
/// Represents a program that can be executed by the automation system.
/// </summary>
public interface IProgram
{
    /// <summary>
    /// Executes the program.
    /// </summary>
    /// <param name="ctx">The program context providing access to data sources and devices.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ExecuteAsync(IProgramContext ctx, CancellationToken ct = default);
}
