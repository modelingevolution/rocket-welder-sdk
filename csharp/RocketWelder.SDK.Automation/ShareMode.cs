namespace RocketWelder.SDK.Automation;

/// <summary>
/// Visibility of a <see cref="IProgramContext.GetData{T}"/> / <see cref="IProgramContext.SetData{T}"/>
/// value. Only one instance of any given <see cref="IProgram"/> runs at a time, so
/// <see cref="Program"/> identifies a single writer unambiguously.
/// </summary>
public enum ShareMode
{
    /// <summary>
    /// Visible to all programs (default). Use for inter-program data exchange.
    /// </summary>
    Global,

    /// <summary>
    /// Visible only to the writing <see cref="IProgram"/>.
    /// </summary>
    Program
}
