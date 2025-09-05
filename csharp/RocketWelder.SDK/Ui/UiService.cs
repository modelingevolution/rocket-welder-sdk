using MicroPlumberd;
using MicroPlumberd.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RocketWelder.SDK.Ui.Internals;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Ui;

public class UiService : IUiService
{
    private IPlumberInstance _plumber;
    private ICommandBus _bus;
    private readonly Guid _sessionId;
    private readonly Dictionary<ControlId, ControlBase> _index = new();
    private readonly EventProjection _eventQueue = new();
    private ImmutableHashSet<ControlId> _scheduledDeletions = ImmutableHashSet<ControlId>.Empty;
    private ImmutableList<(ControlBase control, RegionName region, ControlType type)> _scheduledDefinitions = ImmutableList<(ControlBase, RegionName, ControlType)>.Empty;
    private readonly Dictionary<RegionName, ItemsControl> _regions = new();
    private IServiceProvider? _sp;
    private IAsyncDisposable _token;
    internal Task EnqueueEvent(object evt)=> _eventQueue.Given(new Metadata(), evt);

    public static IUiService FromSessionId(Guid sessionId) => new UiService(sessionId);
    public static IUiService From(IConfiguration configuration) => new UiService(Guid.Parse(configuration["SessionId"] ?? throw new ArgumentNullException("SessionId")));

    internal UiService(Guid sessionId)
    {
        _sessionId = sessionId;
            
        Factory = new UiControlFactory(this);
    }
        
    
    /// <summary>
    /// Send commands in single thread loop.
    /// </summary>
    /// <returns></returns>
    public async Task Do()
    {
        DispatchEvents();
        await ProcessScheduledDefinitions();
        await ProcessScheduledDeletions();
        await SendPropertyUpdates();
    }
        
    private async Task ProcessScheduledDefinitions()
    {
        var toDefine = _scheduledDefinitions;
        if (!toDefine.IsEmpty)
        {
            _scheduledDefinitions = ImmutableList<(ControlBase, RegionName, ControlType)>.Empty;
            
            // Send DefineControl commands
            foreach (var (control, region, type) in toDefine)
            {
                // Add to index when actually defining the control
                _index[control.Id] = control;
                
                var defineCommand = new DefineControl
                {
                    ControlId = control.Id,
                    Type = type,
                    Properties = control.Changed,
                    RegionName = region
                };
                
                await _bus.SendAsync(_sessionId, defineCommand, fireAndForget: true);
                control.CommitChanges();
            }
        }
    }
    
    private async Task ProcessScheduledDeletions()
    {
        var toDelete = _scheduledDeletions;
        if (!toDelete.IsEmpty)
        {
            _scheduledDeletions = ImmutableHashSet<ControlId>.Empty;
                
            // Send batch delete command
            await _bus.SendAsync(_sessionId, new DeleteControls { ControlIds = toDelete }, fireAndForget: true);
                
            // Remove from local index and regions
            foreach (var controlId in toDelete)
            {
                if (!_index.Remove(controlId, out var control)) continue;

                foreach (var region in _regions.Values.Where(region => region.Contains(control))) region.Remove(control);
            }
        }
    }

    private void DispatchEvents()
    {
        foreach (var e in _eventQueue.GetBatch())
        {
            if (_index.TryGetValue(e.ControlId, out var control))
            {
                control.HandleEvent(e);
            }
        }
    }

    private async Task SendPropertyUpdates()
    {
        ImmutableDictionary<ControlId, ImmutableDictionary<string, string> > updates = ImmutableDictionary<ControlId, ImmutableDictionary<string, string>>.Empty;
        foreach (var c in _regions.Values.SelectMany(r=>r).Where(x=>x.IsDirty)) updates = updates.Add(c.Id, c.Changed);
        if (updates.Count > 0)
        {
            await _bus.SendAsync(_sessionId,new ChangeControls { Updates = updates },fireAndForget:true);
            foreach(var i in updates.Keys) _index[i].CommitChanges();
        }
    }

    public Guid SessionId => _sessionId;
    public IUiControlFactory Factory { get; }
        
    public IItemsControl this[RegionName r]
    {
        get
        {
            if(!_regions.TryGetValue(r, out var ret))
                return _regions.TryAdd(r,ret = new ItemsControl(this, r)) ? ret : _regions[r];
            return ret;
        }
    }

    public async Task<(IUiService, IHost)> BuildUiHost( Action<HostBuilderContext, IServiceCollection>? onConfigureServices = null)
    {
        IHostBuilder builder= Host.CreateDefaultBuilder(_sessionId != Guid.Empty ? [$"SessionId={_sessionId}"] : []);
        builder.ConfigureServices((context, services) =>
        {
            onConfigureServices?.Invoke(context,services);
            services.AddRocketWelderUi();
        });
        
        var host = builder.Build();
        await host.StartAsync();
        return (host.Services.GetRequiredService<IUiService>(), host);
    }

    public async Task<IUiService> Initialize(IServiceProvider sp)
    {
        _sp = sp;
        var p = sp.GetRequiredService<IPlumberInstance>();
        var b  = sp.GetRequiredService<ICommandBus>();
        await InitializeWith(p, b);
        return this;
    }

    internal async Task InitializeWith(IPlumberInstance plumberd, ICommandBus bus)
    {
        _plumber = plumberd;
        _bus = bus;
        _token = await _plumber.SubscribeEventHandler(_eventQueue, $"Ui.Events-{_sessionId}");
    }
    internal void ScheduleDelete(ControlId controlId)
    {
        // Thread-safe addition using immutable collection
        // Multiple threads can call Dispose simultaneously
        ImmutableHashSet<ControlId> original;
        ImmutableHashSet<ControlId> updated;
        do
        {
            original = _scheduledDeletions;
            updated = original.Add(controlId);
        } while (Interlocked.CompareExchange(ref _scheduledDeletions, updated, original) != original);
    }
        
    internal void ScheduleDefineControl(ControlBase control, RegionName region, ControlType type)
    {
        if (control == null)
            throw new ArgumentNullException(nameof(control));
        
        // Thread-safe addition using immutable collection
        ImmutableList<(ControlBase, RegionName, ControlType)> original;
        ImmutableList<(ControlBase, RegionName, ControlType)> updated;
        do
        {
            original = _scheduledDefinitions;
            updated = original.Add((control, region, type));
        } while (Interlocked.CompareExchange(ref _scheduledDefinitions, updated, original) != original);
    }

    public async ValueTask DisposeAsync()
    {
        await _token.DisposeAsync();
    }
}