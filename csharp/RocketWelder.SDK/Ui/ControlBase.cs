using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using RocketWelder.SDK.Ui.Internals;

namespace RocketWelder.SDK.Ui;

public abstract class ControlBase : IDisposable
{
    private ImmutableDictionary<string, string> _commitedSet = ImmutableDictionary<string, string>.Empty;
    private ImmutableDictionary<string, string> _workingSet = ImmutableDictionary<string, string>.Empty;
    private readonly ControlId _id;
    private readonly UiService _ui;

    internal ControlBase(ControlId id, UiService ui, Dictionary<string, string>? initialProperties = null)
    {
        _id = id;
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            
        if (initialProperties != null)
        {
            _workingSet = initialProperties.ToImmutableDictionary();
        }
    }

    internal abstract void HandleEvent(EventBase evt);
    public ControlId Id => _id;
    public bool IsDirty => !_workingSet.Equals(_commitedSet);
    protected void SetProperty<T>(string key, T? value) where T : struct, IParsable<T>
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Property key cannot be null or empty", nameof(key));
                
        _workingSet = value == null 
            ? _workingSet.Remove(key) 
            : _workingSet.SetItem(key, value.Value.ToString()!);
    }
        
    protected void SetProperty(string key, string? value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Property key cannot be null or empty", nameof(key));
                
        _workingSet = value == null 
            ? _workingSet.Remove(key) 
            : _workingSet.SetItem(key, value);
    }

    protected T? GetProperty<T>(string key) where T : struct, IParsable<T> => _workingSet.TryGetValue(key, out var str) ? T.Parse(str, null) : default(T?);
    protected string? GetPropertyString(string key) => _workingSet.TryGetValue(key, out var str) ? str : null;
    public ImmutableDictionary<string, string> Changed
    {
        get
        {
            var builder = ImmutableDictionary.CreateBuilder<string, string>();
                
            // Add new or modified properties
            foreach (var kvp in _workingSet)
            {
                if (!_commitedSet.TryGetValue(kvp.Key, out var committedValue) || committedValue != kvp.Value)
                {
                    builder[kvp.Key] = kvp.Value;
                }
            }
                
            // Add removed properties as null
            foreach (var key in _commitedSet.Keys)
            {
                if (!_workingSet.ContainsKey(key))
                {
                    builder[key] = null!;
                }
            }
                
            return builder.ToImmutable();
        }
    }
    // Invoked by uiService
    internal void CommitChanges()
    {
        _commitedSet = _workingSet;
    }
    private bool _disposed;
        
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
        
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Schedule the delete command to be sent on next UI loop iteration
                // This ensures proper async handling without blocking
                _ui.ScheduleDelete(_id);
            }
            _disposed = true;
        }
    }
}