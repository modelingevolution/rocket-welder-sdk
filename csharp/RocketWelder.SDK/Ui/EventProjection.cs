using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using MicroPlumberd;
using RocketWelder.SDK.Ui.Internals;

namespace RocketWelder.SDK.Ui;

[EventHandler]
internal partial class EventProjection
{
    private ImmutableQueue<EventBase> _index = ImmutableQueue<EventBase>.Empty;
        
    private Task Given(Metadata m, ButtonDown ev) { _index = _index.Enqueue(ev); return Task.CompletedTask; }
    private Task Given(Metadata m, ButtonUp ev) { _index = _index.Enqueue(ev); return Task.CompletedTask; }
    private Task Given(Metadata m, KeyDown ev) { _index = _index.Enqueue(ev); return Task.CompletedTask; }
    private Task Given(Metadata m, KeyUp ev) { _index = _index.Enqueue(ev); return Task.CompletedTask; }
        
    public ImmutableQueue<EventBase> GetBatch()
    {
        var batch = _index;
        _index = ImmutableQueue<EventBase>.Empty;
        return batch;
    }
}