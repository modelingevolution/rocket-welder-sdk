using RocketWelder.SDK.Ui;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Text;

namespace RocketWelder.SDK.Ui
{
    public interface IUiControlFactory
    {
        IconButtonControl DefineIconButton(ControlId controlId, string icon, Dictionary<string, string>? properties = null);
        ArrowGridControl DefineArrowGrid(ControlId controlId, Dictionary<string, string>? properties = null);
        LabelControl DefineLabel(ControlId controlId, string text, Dictionary<string, string>? properties = null);
    }
}
