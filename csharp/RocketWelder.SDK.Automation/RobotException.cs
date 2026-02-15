namespace RocketWelder.SDK.Automation;

/// <summary>Base exception for all robot operations.</summary>
public class RobotException : Exception
{
    public RobotException() { }
    public RobotException(string message) : base(message) { }
    public RobotException(string message, Exception innerException) : base(message, innerException) { }
}
