using System.ComponentModel;

namespace Beutl;

public sealed class PropertyChangeTracker : IDisposable
{
    private readonly List<CorePropertyChangedEventArgs> _changes = [];
    private readonly List<ICoreObject> _trackingElement = [];

    public PropertyChangeTracker(IEnumerable<ICoreObject> elements, int maxDepth = -1)
    {
        MaxDepth = maxDepth;

        foreach (ICoreObject item in elements)
        {
            AddHandlers(item, 0);
        }
    }

    ~PropertyChangeTracker()
    {
        Dispose();
    }

    public int MaxDepth { get; }

    public IReadOnlyList<ICoreObject> TrackingElements => _trackingElement;

    public bool IsDisposed { get; private set; }

    public IRecordableCommand ToCommand()
    {
        return new CommandImpl([.. _changes]);
    }

    private void AddHandlers(ICoreObject obj, int currentDepth)
    {
        if (MaxDepth == -1 || currentDepth <= MaxDepth)
        {
            _trackingElement.Add(obj);
            obj.PropertyChanged += OnPropertyChanged;

            if (obj is IHierarchical elm)
            {
                foreach (IHierarchical item in elm.HierarchicalChildren)
                {
                    AddHandlers(item, currentDepth + 1);
                }
            }
        }
    }

    private void AddHandlers(IHierarchical elm, int currentDepth)
    {
        if (MaxDepth == -1 || currentDepth <= MaxDepth)
        {
            if(elm is ICoreObject obj)
            {
                _trackingElement.Add(obj);
                obj.PropertyChanged += OnPropertyChanged;
            }

            foreach (IHierarchical item in elm.HierarchicalChildren)
            {
                AddHandlers(item, currentDepth + 1);
            }
        }
    }

    private void RemoveHandlers()
    {
        for (int i = 0; i < _trackingElement.Count; i++)
        {
            ICoreObject item = _trackingElement[i];
            item.PropertyChanged -= OnPropertyChanged;
        }

        _trackingElement.Clear();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is CorePropertyChangedEventArgs args)
        {
            _changes.Add(args);
        }
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            RemoveHandlers();
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private sealed class CommandImpl(CorePropertyChangedEventArgs[] changes) : IRecordableCommand
    {
        public void Do()
        {
            for (int i = 0; i < changes.Length; i++)
            {
                CorePropertyChangedEventArgs item = changes[i];

                item.Sender.SetValue(item.Property, item.NewValue);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            for (int i = changes.Length - 1; i >= 0; i--)
            {
                CorePropertyChangedEventArgs item = changes[i];

                item.Sender.SetValue(item.Property, item.OldValue);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
