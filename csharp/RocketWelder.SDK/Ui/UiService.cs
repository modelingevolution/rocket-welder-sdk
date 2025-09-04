using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;
using RocketWelder.SDK.Ui.Internals;

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
    private ServiceProvider? _sp;
    internal Task EnqueueEvent(object evt)=> _eventQueue.Given(new Metadata(), evt);
    public UiService(Guid sessionId)
    {
        _sessionId = sessionId;
            
        // Initialize regions with predefined names
        InitializeRegion(RegionName.Top);
        InitializeRegion(RegionName.TopLeft);
        InitializeRegion(RegionName.TopRight);
        InitializeRegion(RegionName.BottomLeft);
        InitializeRegion(RegionName.BottomRight);
        InitializeRegion(RegionName.Bottom);
            
        Factory = new UiControlFactory(this);
    }
        
    private void InitializeRegion(RegionName regionName)
    {
        _regions[regionName] = new ItemsControl(this, regionName);
    }
    /// <summary>
    /// Send commands in single thread loop.
    /// </summary>
    /// <returns></returns>
    internal async Task Do()
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
                var defineCommand = new DefineControl
                {
                    ControlId = control.Id,
                    Type = type,
                    Properties = control.Changed.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
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
                if (_index.Remove(controlId, out var control))
                {
                    foreach (var region in _regions.Values)
                    {
                        if (region.Contains(control))
                        {
                            region.Remove(control);
                        }
                    }
                }
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

    public IUiControlFactory Factory { get; }
        
    public IItemsControl this[RegionName r] => _regions[r];
    public async Task Initialize()
    {
        ServiceCollection sc = new ServiceCollection();
        sc.AddPlumberd();
        _sp = sc.BuildServiceProvider();
        var p = _sp.GetRequiredService<IPlumberInstance>();
        var b  = _sp.GetRequiredService<ICommandBus>();
        await InitializeWith(p, b);
    }

    internal async Task InitializeWith(IPlumberInstance plumberd, ICommandBus bus)
    {
        _plumber = plumberd;
        _bus = bus;
        await _plumber.SubscribeEventHandler(_eventQueue, $"Ui.Events-{_sessionId}");
    }
    internal void RegisterControl(ControlBase control)
    {
        if (control == null)
            throw new ArgumentNullException(nameof(control));
                
        _index[control.Id] = control;
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
        
}