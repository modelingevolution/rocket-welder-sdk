using System;
using System.Collections.ObjectModel;
using System.Reflection;
using RocketWelder.SDK.Ui.Internals;

namespace RocketWelder.SDK.Ui;

internal sealed class ItemsControl : Collection<ControlBase>, IItemsControl
{
    private readonly UiService _uiService;
    private readonly RegionName _regionName;
        
    internal ItemsControl(UiService uiService, RegionName regionName)
    {
        _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
        _regionName = regionName;
    }
    
    protected override void InsertItem(int index, ControlBase item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));
        
        // Get control type from attribute
        var controlType = GetControlType(item);
        
        // Schedule DefineControl command
        _uiService.ScheduleDefineControl(item, _regionName, controlType);
        
        base.InsertItem(index, item);
    }
    
    protected override void RemoveItem(int index)
    {
        var control = this[index];
        
        // Schedule deletion
        _uiService.ScheduleDelete(control.Id);
        
        base.RemoveItem(index);
    }
    
    protected override void SetItem(int index, ControlBase item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));
            
        // Remove old control
        var oldControl = this[index];
        _uiService.ScheduleDelete(oldControl.Id);
        
        // Add new control
        var controlType = GetControlType(item);
        _uiService.ScheduleDefineControl(item, _regionName, controlType);
        
        base.SetItem(index, item);
    }
    
    protected override void ClearItems()
    {
        // Schedule deletion for all controls
        foreach (var control in this)
        {
            _uiService.ScheduleDelete(control.Id);
        }
        
        base.ClearItems();
    }
    
    private static ControlType GetControlType(ControlBase control)
    {
        var type = control.GetType();
        var attribute = type.GetCustomAttribute<ControlTypeAttribute>();
        
        if (attribute == null)
        {
            throw new InvalidOperationException(
                $"Control type {type.Name} is missing the {nameof(ControlTypeAttribute)}");
        }
        
        return attribute.Type;
    }
}