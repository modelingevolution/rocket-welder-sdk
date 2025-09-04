using System;
using System.Collections.Generic;
using RocketWelder.SDK.Ui.Internals;

namespace RocketWelder.SDK.Ui;

[ControlType(ControlType.IconButton)]
public sealed class IconButtonControl : ControlBase
{
    public event EventHandler? ButtonDown;
    public event EventHandler? ButtonUp;
        
    internal IconButtonControl(ControlId id, UiService ui, Dictionary<string, string>? properties = null) 
        : base(id, ui, properties)
    {
    }
        
    public string? Icon 
    { 
        get => GetPropertyString("icon");
        set => SetProperty("icon", value);
    }
        
    public string? Text 
    { 
        get => GetPropertyString("text");
        set => SetProperty("text", value);
    }
        
    public Color? Color
    {
        get => GetProperty<Color>("color");
        set => SetProperty("color", value ?? Ui.Color.Primary);
    }
        
    public Size? Size
    {
        get => GetProperty<Size>("size");
        set => SetProperty("size", value ?? Ui.Size.Medium);
    }
        
    internal override void HandleEvent(EventBase evt)
    {
        switch (evt)
        {
            case Internals.ButtonDown:
                ButtonDown?.Invoke(this, EventArgs.Empty);
                break;
            case Internals.ButtonUp:
                ButtonUp?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}