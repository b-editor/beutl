using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia.Input;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;
using Dock.Model.Core;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public enum TabSwitcherGroup
{
    Tools,
    Documents,
    NewTools,
}

public sealed record TabSwitcherItem(
    string Title,
    string Description,
    EditorTabItem? Document = null,
    IDockable? Tool = null,
    ToolTabExtension? Extension = null,
    FAIconSource? Icon = null);

public sealed class TabSwitcherViewModel : IDisposable
{
    private sealed class ActivationStamp
    {
        public long Order;
    }

    private readonly EditorService _editorService;
    private readonly ConditionalWeakTable<EditorTabItem, ActivationStamp> _recentDocuments = new();
    private readonly CompositeDisposable _subscriptions = [];
    private long _activationOrder;
    private readonly int[] _selectedIndices = new int[3];
    private EditViewModel? _editor;
    private IToolDock? _targetDock;

    internal TabSwitcherViewModel(EditorService editorService)
    {
        _editorService = editorService;
        _subscriptions.Add(editorService.SelectedTabItem.Subscribe(tab =>
        {
            Close();
            if (tab is not null)
                _recentDocuments.GetValue(tab, _ => new()).Order = ++_activationOrder;
        }));
        _subscriptions.Add(editorService.LifecycleActivity.Subscribe(activity =>
        {
            if (activity != ProjectLifecycleActivity.None)
                Close();
        }));
        editorService.TabItems.CollectionChanged += OnDocumentsChanged;

        _subscriptions.Add(SelectedDocument.Subscribe(item => OnSelectionChanged(TabSwitcherGroup.Documents, item)));
        _subscriptions.Add(SelectedTool.Subscribe(item => OnSelectionChanged(TabSwitcherGroup.Tools, item)));
        _subscriptions.Add(SelectedNewTool.Subscribe(item => OnSelectionChanged(TabSwitcherGroup.NewTools, item)));
    }

    public ReactivePropertySlim<bool> IsOpen { get; } = new();
    public ReactivePropertySlim<bool> IsCreating { get; } = new();
    public ReactivePropertySlim<TabSwitcherGroup> SelectedGroup { get; } = new();
    public ReactivePropertySlim<TabSwitcherItem?> SelectedDocument { get; } = new();
    public ReactivePropertySlim<TabSwitcherItem?> SelectedTool { get; } = new();
    public ReactivePropertySlim<TabSwitcherItem?> SelectedNewTool { get; } = new();

    public ObservableCollection<TabSwitcherItem> Documents { get; } = [];
    public ObservableCollection<TabSwitcherItem> Tools { get; } = [];
    public ObservableCollection<TabSwitcherItem> NewTools { get; } = [];

    internal KeyModifiers HeldModifiers { get; private set; }
    internal TabSwitcherItem? SelectedItem => SelectedDocument.Value ?? SelectedTool.Value ?? SelectedNewTool.Value;
    internal bool CanOpen => _editorService.LifecycleActivity.Value == ProjectLifecycleActivity.None
                             && _editorService.TabItems.Count > 0
                             && _editorService.SelectedTabItem.Value?.Context.Value is not EditViewModel { IsEnabled.Value: false };

    internal static bool IsNavigationCommand(string name) => name is
        MainViewExtension.NextTabCommandName or MainViewExtension.PreviousTabCommandName
        or MainViewExtension.NextToolTabCommandName or MainViewExtension.PreviousToolTabCommandName
        or MainViewExtension.CreateToolTabCommandName;

    internal static bool IsTextInputGesture(KeyEventArgs? args) =>
        args is not null
        && (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == KeyModifiers.None
        && ContextCommandInput.IsFromTextInput(args);

    internal bool ExecuteCommand(ContextCommandExecution execution)
    {
        TabSwitcherGroup group = execution.CommandName switch
        {
            MainViewExtension.CreateToolTabCommandName => TabSwitcherGroup.NewTools,
            _ => TabSwitcherGroup.Tools,
        };
        int direction = execution.CommandName is MainViewExtension.PreviousTabCommandName
            or MainViewExtension.PreviousToolTabCommandName ? -1 : 1;
        KeyModifiers modifiers = execution.KeyEventArgs?.KeyModifiers ?? KeyModifiers.None;
        KeyModifiers primaryModifiers = modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta);
        modifiers = execution.CommandName == MainViewExtension.CreateToolTabCommandName
            ? KeyModifiers.None
            : primaryModifiers != KeyModifiers.None ? primaryModifiers : modifiers & KeyModifiers.Shift;
        return Begin(group, direction, modifiers);
    }

    internal bool Begin(TabSwitcherGroup group, int direction, KeyModifiers modifiers)
    {
        if (!CanOpen)
            return false;

        if (IsOpen.Value)
        {
            if (group == TabSwitcherGroup.NewTools)
            {
                if (NewTools.Count == 0) return false;
                HeldModifiers = KeyModifiers.None;
                IsCreating.Value = true;
                Select(group, 0);
            }
            else if (IsCreating.Value)
            {
                if (Items(group).Count == 0) return false;
                HeldModifiers = modifiers;
                IsCreating.Value = false;
                Select(group, InitialIndex(group, direction));
            }
            else
            {
                MoveSelection(direction);
            }
            return true;
        }

        _editor = _editorService.SelectedTabItem.Value?.Context.Value as EditViewModel;
        Array.Clear(_selectedIndices);
        Documents.Clear();
        foreach (EditorTabItem tab in _editorService.TabItems
                     .Where(tab => tab.Context.Value is not null)
                     .OrderByDescending(tab => ReferenceEquals(tab, _editorService.SelectedTabItem.Value))
                     .ThenByDescending(tab => _recentDocuments.TryGetValue(tab, out var stamp) ? stamp.Order : 0))
        {
            Documents.Add(new(tab.FileName.Value ?? tab.Extension.Value.DisplayName, tab.FilePath.Value ?? "",
                Document: tab, Icon: tab.Extension.Value.GetIcon()));
        }

        if (_editor is not null)
        {
            BeutlDockFactory factory = _editor.DockHost.Factory;
            IDockable? focused = factory.CurrentDockable ?? _editor.DockHost.Layout.Value.FocusedDockable;
            _targetDock = focused?.Owner as IToolDock ?? factory.FindFirstToolDock();
            foreach (IDockable tool in EnumerateTools(_editor)
                         .OrderByDescending(tool => ReferenceEquals(tool, focused))
                         .ThenByDescending(factory.GetActivationOrder))
            {
                Tools.Add(CreateToolItem(tool));
            }

            foreach (ToolTabExtension extension in factory.EnumerateToolTabExtensions()
                         .Where(extension => extension.CanMultiple || !factory.IsToolTabOpen(extension)))
            {
                NewTools.Add(new(extension.Header ?? extension.DisplayName, extension.DisplayName,
                    Extension: extension, Icon: extension.GetIcon()));
            }
        }

        if (group == TabSwitcherGroup.NewTools && NewTools.Count == 0)
        {
            Close();
            return false;
        }
        if (Items(group).Count == 0)
            group = Tools.Count > 0 ? TabSwitcherGroup.Tools : TabSwitcherGroup.Documents;
        if (Items(group).Count == 0)
        {
            Close();
            return false;
        }

        HeldModifiers = modifiers;
        IsCreating.Value = group == TabSwitcherGroup.NewTools;
        // Freeze MRU ordering for the whole gesture. Selection alone must not activate a tab.
        Select(group, InitialIndex(group, direction));
        IsOpen.Value = true;
        return true;
    }

    private int InitialIndex(TabSwitcherGroup group, int direction) =>
        group == TabSwitcherGroup.NewTools || Items(group).Count == 1
            ? 0 : direction > 0 ? 1 : Items(group).Count - 1;

    internal void MoveSelection(int direction)
    {
        ObservableCollection<TabSwitcherItem> items = Items(SelectedGroup.Value);
        if (items.Count == 0) return;
        int index = SelectedItem is { } current ? items.IndexOf(current) : 0;
        Select(SelectedGroup.Value, ((index + direction) % items.Count + items.Count) % items.Count);
    }

    internal void MoveGroup(int direction)
    {
        if (IsCreating.Value) return;
        TabSwitcherGroup group = direction < 0 ? TabSwitcherGroup.Tools : TabSwitcherGroup.Documents;
        Select(group, Math.Min(_selectedIndices[(int)group], Items(group).Count - 1));
    }

    internal void Select(TabSwitcherGroup group, int index)
    {
        if (index < 0 || index >= Items(group).Count) return;
        switch (group)
        {
            case TabSwitcherGroup.Documents: SelectedDocument.Value = Documents[index]; break;
            case TabSwitcherGroup.Tools: SelectedTool.Value = Tools[index]; break;
            case TabSwitcherGroup.NewTools: SelectedNewTool.Value = NewTools[index]; break;
        }
    }

    private void OnSelectionChanged(TabSwitcherGroup group, TabSwitcherItem? item)
    {
        if (item is null) return;
        _selectedIndices[(int)group] = Items(group).IndexOf(item);
        if (group != TabSwitcherGroup.Documents) SelectedDocument.Value = null;
        if (group != TabSwitcherGroup.Tools) SelectedTool.Value = null;
        if (group != TabSwitcherGroup.NewTools) SelectedNewTool.Value = null;
        SelectedGroup.Value = group;
    }

    internal ObservableCollection<TabSwitcherItem> Items(TabSwitcherGroup group) => group switch
    {
        TabSwitcherGroup.Documents => Documents,
        TabSwitcherGroup.Tools => Tools,
        _ => NewTools,
    };

    internal TabSwitcherItem? Commit()
    {
        TabSwitcherItem? item = SelectedItem;
        EditViewModel? editor = _editor;
        IToolDock? target = _targetDock;
        bool canOpen = CanOpen;
        Close();

        if (!canOpen || item is null) return null;
        if (item.Document is { } document)
        {
            if (!_editorService.TabItems.Contains(document) || document.Context.Value is null) return null;
            _editorService.ActivateTabItem(document.Context.Value.Object);
            return item;
        }

        if (editor is null || !_editorService.TabItems.Any(tab => ReferenceEquals(tab.Context.Value, editor))
                           || !ReferenceEquals(_editorService.SelectedTabItem.Value?.Context.Value, editor)
                           || !editor.IsEnabled.Value)
            return null;

        BeutlDockFactory factory = editor.DockHost.Factory;
        if (item.Extension is { } extension)
        {
            if (!factory.EnumerateToolTabExtensions().Contains(extension)) return null;
            // Another UI surface may have opened a singleton since the snapshot was built.
            IDockable? existing = !extension.CanMultiple
                ? factory.EnumerateTools().FirstOrDefault(tool => tool.ToolContext.Extension == extension)
                : null;
            if (existing is null)
            {
                if (target is null || !BeutlDockFactory.Traverse(editor.DockHost.Layout.Value).Contains(target)) return null;
                if (!factory.OpenToolTab(extension, target))
                {
                    NotificationService.ShowError(Strings.TabSwitcher_Create, MessageStrings.OperationFailed);
                    return null;
                }
                existing = factory.EnumerateTools()
                    .Where(tool => tool.ToolContext.Extension == extension)
                    .MaxBy(factory.GetActivationOrder);
            }
            if (existing is null) return null;
            item = CreateToolItem(existing);
        }

        if (item.Tool is not { } dockable || !EnumerateTools(editor).Contains(dockable)) return null;
        if (factory.IsDockablePinned(dockable))
            factory.PreviewPinnedDockable(dockable);
        else
            factory.RestoreDockable(dockable);
        factory.SetActiveDockable(dockable);
        factory.SetFocusedDockable(dockable.Owner as IDock ?? editor.DockHost.Layout.Value, dockable);
        factory.ActivateWindow(dockable);
        return item;
    }

    private static IEnumerable<IDockable> EnumerateTools(EditViewModel editor) =>
        BeutlDockFactory.Traverse(editor.DockHost.Layout.Value)
            .Where(dockable => dockable is BeutlToolDockable or PlayerToolDockable or NewToolTabDockable)
            .Distinct();

    private static TabSwitcherItem CreateToolItem(IDockable tool) => tool switch
    {
        BeutlToolDockable tab => new(tool.Title ?? "", tab.ToolContext.Extension.DisplayName, Tool: tool, Icon: tab.Icon),
        NewToolTabDockable newTab => new(tool.Title ?? "", Strings.NewTab, Tool: tool, Icon: newTab.Icon),
        _ => new(tool.Title ?? "", Strings.Preview, Tool: tool,
            Icon: new FluentIcons.Avalonia.Fluent.FluentIconSource { Icon = FluentIcons.Common.Icon.Play }),
    };

    public void Close()
    {
        IsOpen.Value = false;
        HeldModifiers = KeyModifiers.None;
        IsCreating.Value = false;
        SelectedDocument.Value = null;
        SelectedTool.Value = null;
        SelectedNewTool.Value = null;
        Documents.Clear();
        Tools.Clear();
        NewTools.Clear();
        _editor = null;
        _targetDock = null;
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Close();

    public void Dispose()
    {
        Close();
        _editorService.TabItems.CollectionChanged -= OnDocumentsChanged;
        _subscriptions.Dispose();
        IsOpen.Dispose();
        IsCreating.Dispose();
        SelectedGroup.Dispose();
        SelectedDocument.Dispose();
        SelectedTool.Dispose();
        SelectedNewTool.Dispose();
    }
}
