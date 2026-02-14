using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.ObjectPropertyTab.ViewModels;

public sealed class ObjectPropertyTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IEditorContext _editorContext;
    private readonly IPropertiesEditorFactory _factory;
    // インデックスが大きい方が新しい
    private readonly List<IPropertiesEditorViewModel> _cache = new(8);
    private readonly List<WeakReference<ICoreObject>> _backStack = new(32);
    private readonly ConditionalWeakTable<ICoreObject, IServiceProvider> _providers = new();
    private readonly ReactivePropertySlim<bool> _canBack = new();

    public ObjectPropertyTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        _factory = editorContext.GetRequiredService<IPropertiesEditorFactory>();

        editorContext.GetRequiredService<IEditorSelection>().SelectedObject
            .Subscribe(obj => NavigateCore(obj, false, null))
            .DisposeWith(_disposables);
    }

    public ToolTabExtension Extension => ObjectPropertyTabExtension.Instance;

    public IEditorContext ParentContext => _editorContext;

    public ReactiveProperty<IPropertiesEditorViewModel?> ChildContext { get; } = new();

    public IReadOnlyReactiveProperty<bool> CanBack => _canBack;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => Strings.Properties;

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.RightUpperBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public void Back()
    {
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
        ChildContext.Value = null;
        WeakReference<ICoreObject> weakRef = _backStack.Find(x => x.TryGetTarget(out ICoreObject? item) && ReferenceEquals(item, obj))
            ?? new WeakReference<ICoreObject>(obj!);

        if (obj != null)
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

            if (_cache.Count > 7)
            {
                int count = _cache.Count - 7;
                for (int i = 0; i < count; i++)
                {
                    _cache[i].Dispose();
                }
                _cache.RemoveRange(0, count);
            }
        }

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
        if (_backStack.Count > 31)
        {
            _backStack.RemoveRange(0, _backStack.Count - 31);
        }

        _canBack.Value = _backStack.Count > 0;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void WriteToJson(JsonObject json)
    {
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
            var visitor = new Visitor(provider ?? this);
            foreach (IPropertyEditorContext item in obj.Properties)
            {
                item.Accept(visitor);
            }
        }
    }

    private sealed record Visitor(IServiceProvider Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
