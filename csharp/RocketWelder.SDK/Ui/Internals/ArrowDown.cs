using MicroPlumberd;

namespace RocketWelder.SDK.Ui.Internals;

[OutputStream("Ui.Events")]
public record ArrowDown : EventBase
{
    public ArrowDirection Direction { get; init; }
}