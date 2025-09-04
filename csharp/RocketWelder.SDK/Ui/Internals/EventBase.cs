using System;

namespace RocketWelder.SDK.Ui.Internals;

public record EventBase
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ControlId ControlId { get; init; }
}