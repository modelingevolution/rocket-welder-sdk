using RocketWelder.SDK.Ui;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MicroPlumberd;
using RocketWelder.SDK.Ui.Internals;

namespace RocketWelder.SDK.Ui
{
    public interface IUiControlFactory
    {
        IconButtonControl DefineIconButton(ControlId controlId, string icon,  Dictionary<string, string>? properties = null);
        ArrowGridControl DefineArrowGrid(ControlId controlId, Dictionary<string, string>? properties = null);
        LabelControl DefineLabel(ControlId controlId, string text,  Dictionary<string, string>? properties = null);
    }
    public interface IUiService 
    {
        public IUiControlFactory Factory { get; }
        public IItemsControl this[RegionName r] { get; }
    }

    public enum RegionName
    {
        Top,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Bottom
    }
    public interface IItemsControl : IEnumerable<ControlBase>
    {
        void Add(ControlBase control);
    }

    internal class UiControlFactory : IUiControlFactory
    {

    }
    [EventHandler]
    internal partial class EventProjection
    {
        public ImmutableQueue<EventBase> _index = ImmutableQueue<EventBase>.Empty;
        private async Task Given(Metadata m, ButtonDown ev) => _index = _index.Enqueue(ev);
        public ImmutableQueue<EventBase> GetBatch()
        {
            var q = _index;
            _index = ImmutableQueue<EventBase>.Empty;
            return q;
        }
    }
    internal class UiService : IUiService
    {
        private readonly IPlumberInstance _plumber;
        private readonly Guid _sessionId;
        private readonly Dictionary<ControlId, ControlBase> _index = new();
        private readonly EventProjection _eventQueue = new();
        class ItemsControl:ObservableCollection<ControlBase>, IItemsControl {}
        private readonly Dictionary<RegionName, ItemsControl> _regions = new();
        public UiService(IPlumberInstance plumber, Guid sessionId)
        {
            _plumber = plumber;
            _sessionId = sessionId;
            foreach (RegionName r in Enum.GetValues(typeof(RegionName)))
            {
                _regions[r] = new ItemsControl();
            }
            Factory = new UiControlFactory(this);
        }
        /// <summary>
        /// Send commands in single thread loop.
        /// </summary>
        /// <returns></returns>
        internal async Task Do()
        {
            DispatchEvents();
            await SendPropertyUpdates();
        }

        private void DispatchEvents()
        {
            foreach (var e in _eventQueue.GetBatch())
            {
                _index[e.ControlId].HandleEvent(e);
            }
        }

        private async Task SendPropertyUpdates()
        {
            ImmutableDictionary<ControlId, ImmutableDictionary<string, string> > updates = ImmutableDictionary<ControlId, ImmutableDictionary<string, string>>.Empty;
            foreach (var c in _regions.Values.SelectMany(r=>r).Where(x=>x.IsDirty)) updates = updates.Add(c.Id, c.Changed);
            if (updates.Count > 0)
            {
                await _plumber.AppendEvent(new ChangeControls { Updates = updates }, _sessionId);
                foreach(var i in updates.Keys) _index[i].CommitChanges();
            }
        }

        public IUiControlFactory Factory { get; }

        public IItemsControl this[RegionName r] => _regions[r];
    }
    public abstract class ControlBase : IDisposable
    {
        private ImmutableDictionary<string, string> _commitedSet = ImmutableDictionary<string, string>.Empty;
        private ImmutableDictionary<string, string> _workingSet = ImmutableDictionary<string, string>.Empty;
        private readonly ControlId _id;
        private readonly UiService _ui;

        internal ControlBase(ControlId id, UiService ui)
        {
            _id = id;
            _ui = ui;
        }

        internal abstract void HandleEvent(EventBase evt);
        public ControlId Id => _id;
        public bool IsDirty => !_workingSet.Equals(_commitedSet);
        protected void SetProperty(string key, string value) => _workingSet = _workingSet.SetItem(key, value);

        protected T? GetProperty<T>(string key) where T : IParsable<T> => _workingSet.TryGetValue(key, out var str) ? T.Parse(str, null) : default(T);
        public ImmutableDictionary<string,string> Changed { get; } // Calculate diff between _commitedSet and _workingSet
        // Invoked by uiService
        internal void CommitChanges()
        {
            _commitedSet = _workingSet;
        }
        public void Dispose()
        {
            throw new NotImplementedException(); // propagate to UiService and Publishes DeleteControl
        }
    }
    public class IconButtonControl(ControlId Id) : ControlBase(Id)
    {
        public event EventHandler? ButtonDown;
        public event EventHandler? ButtonUp;

       
    }

    public class ArrowGridControl(ControlId Id) : ControlBase(Id)
    {
        public event EventHandler<ArrowDirection>? ArrowDown;
        public event EventHandler<ArrowDirection>? ArrowUp;
        
    }

    public class LabelControl(ControlId Id) : ControlBase(Id)
    {
        public string Text { get => GetProperty<string>("Text") ?? string.Empty; set => SetProperty("Text", value); }
    }
}
