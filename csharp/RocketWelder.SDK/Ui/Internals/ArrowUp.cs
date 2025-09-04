using MicroPlumberd;

namespace RocketWelder.SDK.Ui.Internals;

[OutputStream("Ui.Events")]
public record ArrowUp : EventBase
{
    public ArrowDirection Direction { get; init; }
}