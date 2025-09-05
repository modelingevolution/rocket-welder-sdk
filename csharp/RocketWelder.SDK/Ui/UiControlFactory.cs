using System;
using System.Collections.Generic;

namespace RocketWelder.SDK.Ui;

internal sealed class UiControlFactory : IUiControlFactory
{
    private readonly UiService _uiService;
        
    internal UiControlFactory(UiService uiService)
    {
        _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
    }
        
    public IconButtonControl DefineIconButton(ControlId controlId, string icon, Dictionary<string, string>? properties = null)
    {
        if (string.IsNullOrWhiteSpace(icon))
            throw new ArgumentException("Icon cannot be null or whitespace", nameof(icon));
            
        var mergedProperties = CreatePropertiesWithDefaults(properties, ("Icon", icon));
        var control = new IconButtonControl(controlId, _uiService, mergedProperties);
        return control;
    }
        
    public ArrowGridControl DefineArrowGrid(ControlId controlId, Dictionary<string, string>? properties = null)
    {
        var mergedProperties = properties ?? new Dictionary<string, string>();
        var control = new ArrowGridControl(controlId, _uiService, mergedProperties);
        return control;
    }
        
    public LabelControl DefineLabel(ControlId controlId, string text, Dictionary<string, string>? properties = null)
    {
        if (text == null)
            throw new ArgumentNullException(nameof(text));
            
        var mergedProperties = CreatePropertiesWithDefaults(properties, ("Text", text));
        var control = new LabelControl(controlId, _uiService, mergedProperties);
        return control;
    }
        
    private static Dictionary<string, string> CreatePropertiesWithDefaults(
        Dictionary<string, string>? properties,
        params (string key, string value)[] defaults)
    {
        var result = new Dictionary<string, string>(properties ?? new Dictionary<string, string>());
            
        foreach (var (key, value) in defaults)
        {
            result[key] = value;
        }
            
        return result;
    }
}