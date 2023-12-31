using System.Text.Json.Nodes;

using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Editors;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class ObjectPropertyEditorViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _viewModel;
    // インデックスが大きい方が新しい
    private readonly List<PropertiesEditorViewModel> _cache = new(8);
    private readonly List<WeakReference<ICoreObject>> _backStack = new(32);
    private readonly ReactivePropertySlim<bool> _canBack = new();

    public ObjectPropertyEditorViewModel(EditViewModel viewModel)
    {
        _viewModel = viewModel;

        _viewModel.SelectedObject
            .Subscribe(obj => NavigateCore(obj, false))
            .DisposeWith(_disposables);
    }

    public ToolTabExtension Extension => ObjectPropertyTabExtension.Instance;

    public IEditorContext ParentContext => _viewModel;

    public ReactiveProperty<PropertiesEditorViewModel?> ChildContext { get; } = new();

    public IReadOnlyReactiveProperty<bool> CanBack => _canBack;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => Strings.Properties;

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    public void Back()
    {
        for (int i = _backStack.Count - 2; i >= 0; i--)
        {
            WeakReference<ICoreObject> item = _backStack[i];
            if (item.TryGetTarget(out ICoreObject? obj))
            {
                NavigateCore(obj, true);
                return;
            }
        }

        ChildContext.Value = null;
        _backStack.Clear();

        _canBack.Value = false;
    }

    public void NavigateCore(ICoreObject? obj, bool back)
    {
        ChildContext.Value = null;
        WeakReference<ICoreObject> weakRef = _backStack.Find(x => x.TryGetTarget(out ICoreObject? item) && ReferenceEquals(item, obj))
            ?? new WeakReference<ICoreObject>(obj!);

        if (obj != null)
        {
            PropertiesEditorViewModel? result = _cache.Find(x => ReferenceEquals(x.Target, obj));

            if (result != null)
            {
                ChildContext.Value = result;
                _cache.Remove(result);
                _cache.Add(result);
            }
            else
            {
                ChildContext.Value = new PropertiesEditorViewModel(obj);
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
        return _viewModel.GetService(serviceType);
    }
}
