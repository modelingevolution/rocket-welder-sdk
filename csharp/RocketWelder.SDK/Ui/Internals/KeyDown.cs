using MicroPlumberd;

namespace RocketWelder.SDK.Ui.Internals;

[OutputStream("Ui.Events")]
public record KeyDown : EventBase
{
        
    public KeyCode Code { get; init; }
}