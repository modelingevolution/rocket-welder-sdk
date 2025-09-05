using MicroPlumberd;

namespace RocketWelder.SDK.Ui.Internals;

[OutputStream("Ui.Events")]
public record KeyUp : EventBase
{
        
    public KeyCode Code { get; init; }
}