using System;
using System.Collections.Immutable;
using MicroPlumberd;

namespace RocketWelder.SDK.Ui.Internals;

[OutputStream("Ui.Commands")]
public record ChangeControls
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public ImmutableDictionary<ControlId, ImmutableDictionary<string, string>> Updates { get; init; } = ImmutableDictionary<ControlId, ImmutableDictionary<string, string>>.Empty;
    // ControlId -> { PropertyId -> Value }
}