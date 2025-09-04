using System;
using RocketWelder.SDK.Ui.Internals;

namespace RocketWelder.SDK.Ui;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal class ControlTypeAttribute : Attribute
{
    public ControlType Type { get; }
    
    public ControlTypeAttribute(ControlType type)
    {
        Type = type;
    }
}