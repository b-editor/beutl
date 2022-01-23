using System.ComponentModel;

namespace BeUtl;

public sealed class PropertyChangeTracker : IDisposable
{
    private readonly List<CorePropertyChangedEventArgs> _changes = new();
    private readonly List<IElement> _trackingElement = new();

    public PropertyChangeTracker(IEnumerable<IElement> elements, int maxDepth = -1)
    {
        MaxDepth = maxDepth;

        foreach (IElement item in elements)
        {
            AddHandlers(item, 0);
        }
    }

    ~PropertyChangeTracker()
    {
        Dispose();
    }

    public int MaxDepth { get; }

    public IReadOnlyList<IElement> TrackingElements => _trackingElement;

    public bool IsDisposed { get; private set; }

    public IRecordableCommand ToCommand()
    {
        return new CommandImpl(_changes.ToArray());
    }

    private void AddHandlers(IElement element, int currentDepth)
    {
        if (MaxDepth == -1 || currentDepth <= MaxDepth)
        {
            _trackingElement.Add(element);
            element.PropertyChanged += OnPropertyChanged;

            foreach (IElement item in element.LogicalChildren)
            {
                AddHandlers(item, currentDepth + 1);
            }
        }
    }

    private void RemoveHandlers()
    {
        for (int i = 0; i < _trackingElement.Count; i++)
        {
            IElement item = _trackingElement[i];
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

    private sealed class CommandImpl : IRecordableCommand
    {
        private readonly CorePropertyChangedEventArgs[] _changes;

        public CommandImpl(CorePropertyChangedEventArgs[] changes)
        {
            _changes = changes;
        }

        public void Do()
        {
            for (int i = 0; i < _changes.Length; i++)
            {
                CorePropertyChangedEventArgs item = _changes[i];

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
            for (int i = _changes.Length - 1; i >= 0; i--)
            {
                CorePropertyChangedEventArgs item = _changes[i];

                item.Sender.SetValue(item.Property, item.OldValue);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
