using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using MicroPlumberd;

namespace RocketWelder.SDK.Ui.Internals
{

    

    // Container → UI Commands (Stream: ExternalCommands-{SessionId})
    [OutputStream("Ui.Commands")]
    public record DefineControl
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public ControlType Type { get; init; }
        public ControlId ControlId { get; init; }
        public ImmutableDictionary<string, string> Properties { get; init; } = ImmutableDictionary<string, string>.Empty;
        public RegionName RegionName { get; init; }
    }

    // UI → Container Events (Stream: ExternalEvents-{SessionId})
}