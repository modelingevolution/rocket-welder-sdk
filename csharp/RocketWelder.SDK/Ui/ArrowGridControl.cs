using System;
using System.Collections.Generic;
using RocketWelder.SDK.Ui.Internals;

namespace RocketWelder.SDK.Ui;

[ControlType(ControlType.ArrowGrid)]
public sealed class ArrowGridControl : ControlBase
{
    public event EventHandler<ArrowDirection>? ArrowDown;
    public event EventHandler<ArrowDirection>? ArrowUp;
        
    private static readonly Dictionary<KeyCode, ArrowDirection> KeyToDirectionMap = new()
    {
        [KeyCode.ArrowUp] = ArrowDirection.Up,
        [KeyCode.ArrowDown] = ArrowDirection.Down,
        [KeyCode.ArrowLeft] = ArrowDirection.Left,
        [KeyCode.ArrowRight] = ArrowDirection.Right
    };
        
    internal ArrowGridControl(ControlId id, UiService ui, Dictionary<string, string>? properties = null) 
        : base(id, ui, properties)
    {
    }
        
    public Size? Size
    {
        get => GetProperty<Size>("size");
        set => SetProperty("size", value ?? Ui.Size.Medium);
    }
        
    public Color? Color
    {
        get => GetProperty<Color>("color");
        set => SetProperty("color", value ?? Ui.Color.Primary);
    }
        
    internal override void HandleEvent(EventBase evt)
    {
        switch (evt)
        {
            case Internals.KeyDown keyDown when TryGetDirection(keyDown.Code, out var directionDown):
                ArrowDown?.Invoke(this, directionDown);
                break;
            case Internals.KeyUp keyUp when TryGetDirection(keyUp.Code, out var directionUp):
                ArrowUp?.Invoke(this, directionUp);
                break;
        }
    }
        
    private static bool TryGetDirection(KeyCode keyCode, out ArrowDirection direction)
    {
        return KeyToDirectionMap.TryGetValue(keyCode, out direction);
    }
}