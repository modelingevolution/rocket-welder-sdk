using System.Collections.Generic;

namespace RocketWelder.SDK.Ui;

public interface IItemsControl : IEnumerable<ControlBase>
{
    void Add(ControlBase control);
    bool Remove(ControlBase control);
}