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
        get => GetPropertyString(nameof(Icon));
        set => SetProperty(nameof(Icon), value);
    }
        
    public string? Text 
    { 
        get => GetPropertyString(nameof(Text));
        set => SetProperty(nameof(Text), value);
    }
        
    public Color? Color
    {
        get => GetProperty<Color>(nameof(Color));
        set => SetProperty(nameof(Color), value ?? Ui.Color.Primary);
    }
        
    public Size? Size
    {
        get => GetProperty<Size>(nameof(Size));
        set => SetProperty(nameof(Size), value ?? Ui.Size.Medium);
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