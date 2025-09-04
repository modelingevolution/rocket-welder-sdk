using System.Collections.Generic;
using RocketWelder.SDK.Ui.Internals;

namespace RocketWelder.SDK.Ui;

public sealed class LabelControl : ControlBase
{
    internal LabelControl(ControlId id, UiService ui, Dictionary<string, string>? properties = null) 
        : base(id, ui, properties)
    {
    }
        
    public string Text 
    { 
        get => GetPropertyString("text") ?? string.Empty; 
        set => SetProperty("text", value ?? string.Empty); 
    }
        
    public Typography? Typography
    {
        get => GetProperty<Typography>("typo");
        set => SetProperty("typo", value ?? Ui.Typography.Body1);
    }
        
    public Color? Color
    {
        get => GetProperty<Color>("color");
        set => SetProperty("color", value ?? Ui.Color.Default);
    }
        
    internal override void HandleEvent(EventBase evt)
    {
        // Labels typically don't handle events
    }
}