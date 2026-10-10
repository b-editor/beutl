using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.ObjectPropertyTab.ViewModels;

public sealed class ObjectPropertyTabViewModel : IPinnableToolContext
{
    private const int MaxCachedEditors = 7;
    private const int MaxBackStackLength = 31;
    private const string PinnedObjectJsonKey = "pinnedObjectId";
    private readonly CompositeDisposable _disposables = [];
    private readonly IEditorContext _editorContext;
    private readonly IPropertiesEditorFactory _factory;
    // インデックスが大きい方が新しい
    private readonly List<IPropertiesEditorViewModel> _cache = new(MaxCachedEditors + 1);
    private readonly List<WeakReference<ICoreObject>> _backStack = new(MaxBackStackLength + 1);
    private readonly ConditionalWeakTable<ICoreObject, IServiceProvider> _providers = new();
    private readonly ReactivePropertySlim<bool> _canBack = new();
    private readonly ToolTabPin _pin;
    private IHierarchical? _watchedTarget;

    public ObjectPropertyTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        _factory = editorContext.GetRequiredService<IPropertiesEditorFactory>();
        _pin = new ToolTabPin(ChildContext.Select(child => child != null)).DisposeWith(_disposables);

        // Going back would change what a pinned tab shows.
        CanBack = _canBack
            .CombineLatest(_pin.IsPinned, (canBack, pinned) => canBack && !pinned)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Header = _pin.IsPinned
            .CombineLatest(ChildContext, (pinned, child) => pinned ? child?.Target as CoreObject : null)
            .Select(ToolTabHeaderHelper.ObserveObjectLabel)
            .Switch()
            .Select(label => ToolTabHeaderHelper.Compose(Strings.Properties, label))
            .ToReadOnlyReactivePropertySlim(Strings.Properties)
            .DisposeWith(_disposables)!;

        ChildContext.Subscribe(child => WatchTarget(child?.Target as IHierarchical))
            .DisposeWith(_disposables);

        // A pinned tab outlives the property editor that opened its object, so its editors stop taking services
        // from that editor and take them from the tab, which finds the owning element itself.
        _pin.IsPinned.Where(pinned => pinned)
            .Subscribe(_ =>
            {
                if (ChildContext.Value is not { } child) return;
                _providers.Remove(child.Target);
                AcceptChildren(child, null);
            })
            .DisposeWith(_disposables);
    }

    public ToolTabExtension Extension => ObjectPropertyTabExtension.Instance;

    public ReactiveProperty<IPropertiesEditorViewModel?> ChildContext { get; } = new();

    public IReadOnlyReactiveProperty<bool> CanBack { get; }

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public IReactiveProperty<bool> IsPinned => _pin.IsPinned;

    public IReadOnlyReactiveProperty<bool> HasTarget => _pin.HasTarget;

    /// <summary>
    /// Finds the tab to show <paramref name="target"/> in: the one already showing it, else an unpinned one.
    /// </summary>
    public static ObjectPropertyTabViewModel? FindReusable(IEditorContext editorContext, ICoreObject? target)
    {
        return ToolTabReuse.Find<ObjectPropertyTabViewModel>(
            editorContext,
            t => ReferenceEquals(t.ChildContext.Value?.Target, target),
            t => t.ChildContext.Value is null,
            retargetAnyOpen: true);
    }

    public void Back()
    {
        if (IsPinned.Value) return;

        for (int i = _backStack.Count - 2; i >= 0; i--)
        {
            WeakReference<ICoreObject> item = _backStack[i];
            if (item.TryGetTarget(out ICoreObject? obj))
            {
                IServiceProvider? provider = _providers.TryGetValue(obj, out IServiceProvider? p) ? p : null; ;
                NavigateCore(obj, true, provider);
                return;
            }
        }

        ChildContext.Value = null;
        _backStack.Clear();

        _canBack.Value = false;
    }

    public void NavigateCore(ICoreObject? obj, bool back, IServiceProvider? provider)
    {
        // Showing the same object again would only clear it for a moment, which releases a pin.
        if (obj != null && ReferenceEquals(ChildContext.Value?.Target, obj)) return;

        ChildContext.Value = null;
        WeakReference<ICoreObject> weakRef = _backStack.Find(x => x.TryGetTarget(out ICoreObject? item) && ReferenceEquals(item, obj))
            ?? new WeakReference<ICoreObject>(obj!);

        if (obj != null)
        {
            ShowEditor(obj, provider);
            TrimEditorCache();
        }

        RecordNavigation(weakRef, obj, back);
    }

    private void ShowEditor(ICoreObject obj, IServiceProvider? provider)
    {
        IPropertiesEditorViewModel? result = _cache.Find(x => ReferenceEquals(x.Target, obj));

        if (result != null)
        {
            ChildContext.Value = result;
            _cache.Remove(result);
            _cache.Add(result);
        }
        else
        {
            ChildContext.Value = _factory.Create(obj);
            if (provider != null)
            {
                _providers.AddOrUpdate(obj, provider);
            }
            AcceptChildren(ChildContext.Value, provider);
            _cache.Add(ChildContext.Value);
        }
    }

    private void TrimEditorCache()
    {
        if (_cache.Count > MaxCachedEditors)
        {
            int count = _cache.Count - MaxCachedEditors;
            for (int i = 0; i < count; i++)
            {
                _cache[i].Dispose();
            }
            _cache.RemoveRange(0, count);
        }
    }

    private void RecordNavigation(WeakReference<ICoreObject> weakRef, ICoreObject? obj, bool back)
    {
        if (!back)
        {
            _backStack.Add(weakRef);
        }
        else
        {
            int start = 0;
            int count = 1;
            for (int i = _backStack.Count - 2; i >= 0; i--)
            {
                if (_backStack[i].TryGetTarget(out ICoreObject? item) && ReferenceEquals(item, obj))
                {
                    start = i;
                    break;
                }
                count++;
            }

            _backStack.RemoveRange(start + 1, count);
        }

        _backStack.RemoveAll(x => !x.TryGetTarget(out _));
        if (_backStack.Count > MaxBackStackLength)
        {
            _backStack.RemoveRange(0, _backStack.Count - MaxBackStackLength);
        }

        _canBack.Value = _backStack.Count > 0;
    }

    private void WatchTarget(IHierarchical? target)
    {
        if (_watchedTarget != null)
            _watchedTarget.DetachedFromHierarchy -= OnTargetDetached;

        _watchedTarget = target;
        if (target != null)
            target.DetachedFromHierarchy += OnTargetDetached;
    }

    // A removed object leaves the tab empty, which also releases the pin.
    private void OnTargetDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        ChildContext.Value = null;
    }

    public void Dispose()
    {
        _disposables.Dispose();
        WatchTarget(null);
    }

    public void ReadFromJson(JsonObject json)
    {
        if (ToolTabPin.WasPinned(json)
            && json.TryGetPropertyValueAsJsonValue(PinnedObjectJsonKey, out Guid id)
            && _editorContext.GetService<Scene>() is { } scene
            && new ObjectSearcher(scene, o => o is CoreObject obj && obj.Id == id).Search() is ICoreObject target)
        {
            NavigateCore(target, false, null);
            _pin.IsPinned.Value = true;
        }
    }

    public void WriteToJson(JsonObject json)
    {
        _pin.WriteToJson(json);
        if (IsPinned.Value && ChildContext.Value?.Target is { } target)
            json[PinnedObjectJsonKey] = target.Id;
        else
            json.Remove(PinnedObjectJsonKey);
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Element))
        {
            return (ChildContext.Value?.Target as IHierarchical)?.FindHierarchicalParent<Element>();
        }
        return _editorContext.GetService(serviceType);
    }

    private void AcceptChildren(IPropertiesEditorViewModel? obj, IServiceProvider? provider)
    {
        if (obj != null)
        {
            var visitor = new ChildVisitor(provider ?? this);
            foreach (IPropertyEditorContext item in obj.Properties)
            {
                item.Accept(visitor);
            }
        }
    }
}
