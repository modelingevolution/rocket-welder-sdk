using RocketWelder.SDK.Ui;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Design;
using System.Text;
using System.Threading.Tasks;
using RocketWelder.SDK.Ui.Internals;

namespace RocketWelder.SDK.Ui
{
    public interface IUiControlFactory
    {
        IconButtonControl DefineIconButton(ControlId controlId, string icon, Dictionary<string, string>? properties = null);
        ArrowGridControl DefineArrowGrid(ControlId controlId, Dictionary<string, string>? properties = null);
        LabelControl DefineLabel(ControlId controlId, string text, Dictionary<string, string>? properties = null);
    }
    
    public interface IItemsControl : IEnumerable<ControlBase>
    {
        void Add(ControlBase control);
    }
    
    public interface IUiService 
    {
        IUiControlFactory Factory { get; }
        IItemsControl this[RegionName r] { get; }
        Task Initialize();
    }

    internal sealed class ItemsControl : ObservableCollection<ControlBase>, IItemsControl
    {
        private readonly UiService _uiService;
        private readonly RegionName _regionName;
        
        internal ItemsControl(UiService uiService, RegionName regionName)
        {
            _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
            _regionName = regionName;
            CollectionChanged += OnCollectionChanged;
        }
        
        private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                    {
                        foreach (ControlBase control in e.NewItems)
                        {
                            _uiService.RegisterControl(control);
                            // Schedule DefineControl command
                            var controlType = control switch
                            {
                                IconButtonControl => ControlType.IconButton,
                                ArrowGridControl => ControlType.ArrowGrid,
                                LabelControl => ControlType.Label,
                                _ => throw new InvalidOperationException($"Unknown control type: {control.GetType().Name}")
                            };
                            _uiService.ScheduleDefineControl(control, _regionName, controlType);
                        }
                    }
                    break;
                    
                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (ControlBase control in e.OldItems)
                        {
                            _uiService.ScheduleDelete(control.Id);
                        }
                    }
                    break;
            }
        }
    }
}
