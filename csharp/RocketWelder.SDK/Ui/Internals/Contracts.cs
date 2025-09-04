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
        public Dictionary<string, string> Properties { get; init; } = new();
        public RegionName RegionName { get; init; }
    }

    [OutputStream("Ui.Commands")]
    public record DeleteControls
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public ImmutableHashSet<ControlId> ControlIds { get; init; } = ImmutableHashSet<ControlId>.Empty;
    }

    [OutputStream("Ui.Commands")]
    public record ChangeControls
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        public ImmutableDictionary<ControlId, ImmutableDictionary<string, string>> Updates { get; init; } = ImmutableDictionary<ControlId, ImmutableDictionary<string, string>>.Empty;
        // ControlId -> { PropertyId -> Value }
    }

    public record EventBase
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public ControlId ControlId { get; init; }
    }
    // UI → Container Events (Stream: ExternalEvents-{SessionId})
    [OutputStream("Ui.Events")]
    public record ButtonDown : EventBase
    {
        
       
    }

    [OutputStream("Ui.Events")]
    public record ButtonUp: EventBase
    {
        
    }

    [OutputStream("Ui.Events")]
    public record KeyDown : EventBase
    {
        
        public KeyCode Code { get; init; }
    }

    [OutputStream("Ui.Events")]
    public record KeyUp : EventBase
    {
        
        public KeyCode Code { get; init; }
    }

    [OutputStream("Ui.Events")]
    public record ArrowDown : EventBase
    {
        public ArrowDirection Direction { get; init; }
    }

    [OutputStream("Ui.Events")]
    public record ArrowUp : EventBase
    {
        public ArrowDirection Direction { get; init; }
    }

    public enum ControlType
    {
        IconButton,
        ArrowGrid,
        Label
    }

    public enum ArrowDirection
    {
        Up,
        Down,
        Left,
        Right
    }
}