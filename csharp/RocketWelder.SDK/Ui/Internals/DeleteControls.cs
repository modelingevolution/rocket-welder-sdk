using System;
using System.Collections.Immutable;
using MicroPlumberd;

namespace RocketWelder.SDK.Ui.Internals;

[OutputStream("Ui.Commands")]
public record DeleteControls
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ImmutableHashSet<ControlId> ControlIds { get; init; } = ImmutableHashSet<ControlId>.Empty;
}