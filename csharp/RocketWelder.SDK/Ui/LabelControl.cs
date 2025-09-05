using System.Collections.Generic;
using RocketWelder.SDK.Ui.Internals;

namespace RocketWelder.SDK.Ui;

[ControlType(ControlType.Label)]
public sealed class LabelControl : ControlBase
{
    internal LabelControl(ControlId id, UiService ui, Dictionary<string, string>? properties = null) 
        : base(id, ui, properties)
    {
    }
        
    public string Text 
    { 
        get => GetPropertyString(nameof(Text)) ?? string.Empty; 
        set => SetProperty(nameof(Text), value ?? string.Empty); 
    }
        
    public Typography? Typography
    {
        get => GetProperty<Typography>(nameof(Typography));
        set => SetProperty(nameof(Typography), value ?? Ui.Typography.Body1);
    }
        
    public Color? Color
    {
        get => GetProperty<Color>(nameof(Color));
        set => SetProperty(nameof(Color), value ?? Ui.Color.Default);
    }
        
    internal override void HandleEvent(EventBase evt)
    {
        // Labels typically don't handle events
    }
}