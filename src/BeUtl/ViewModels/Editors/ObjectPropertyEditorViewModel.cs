using System.Runtime.CompilerServices;

using BeUtl.Framework;
using BeUtl.Services;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public sealed class ObjectPropertyEditorViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = new();
    private readonly EditViewModel _viewModel;
    // インデックスが大きい方が新しい
    private readonly List<(CoreObject, BaseEditorViewModel[])> _cache = new(8);
    private readonly List<WeakReference<CoreObject>> _backStack = new(32);
    private readonly ReactivePropertySlim<bool> _canBack = new();

    public ObjectPropertyEditorViewModel(EditViewModel viewModel)
    {
        _viewModel = viewModel;
        Header = StringResources.Common.PropertiesObservable
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        _viewModel.SelectedObject
            .Subscribe(obj => NavigateCore(obj, false))
            .DisposeWith(_disposables);
    }

    public ToolTabExtension Extension => ObjectPropertyTabExtension.Instance;

    public CoreList<BaseEditorViewModel> Properties { get; } = new();

    public IReadOnlyReactiveProperty<bool> CanBack => _canBack;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    public void Back()
    {
        for (int i = _backStack.Count - 2; i >= 0; i--)
        {
            WeakReference<CoreObject> item = _backStack[i];
            if (item.TryGetTarget(out CoreObject? obj))
            {
                NavigateCore(obj, true);
                return;
            }
        }

        Properties.Clear();
        _backStack.Clear();

        _canBack.Value = false;
    }

    public void NavigateCore(CoreObject? obj, bool back)
    {
        Properties.Clear();
        WeakReference<CoreObject> weakRef = _backStack.Find(x => x.TryGetTarget(out CoreObject? item) && ReferenceEquals(item, obj))
            ?? new WeakReference<CoreObject>(obj!);

        if (obj != null)
        {
            (CoreObject, BaseEditorViewModel[]) result = _cache.Find(x => ReferenceEquals(x.Item1, obj));

            if (result.Item2 != null)
            {
                Properties.AddRange(result.Item2);
                _cache.Remove(result);
                _cache.Add(result);
            }
            else
            {
                Type objType = obj.GetType();
                Type wrapperType = typeof(CorePropertyWrapper<>);

                IReadOnlyList<CoreProperty> props = PropertyRegistry.GetRegistered(objType);
                if (Properties.Capacity < props.Count)
                {
                    Properties.Capacity = props.Count;
                }

                for (int i = 0; i < props.Count; i++)
                {
                    CoreProperty item = props[i];
                    Type wrapperGType = wrapperType.MakeGenericType(item.PropertyType);
                    var wrapper = (IWrappedProperty)Activator.CreateInstance(wrapperGType, item, obj)!;

                    BaseEditorViewModel? itemViewModel = PropertyEditorService.CreateEditorViewModel(wrapper);
                    if (itemViewModel == null && item.PropertyType.IsAssignableTo(typeof(CoreObject)))
                    {
                        Type viewModelType = typeof(NavigationButtonViewModel<>);
                        viewModelType = viewModelType.MakeGenericType(item.PropertyType);
                        itemViewModel = (BaseEditorViewModel)Activator.CreateInstance(viewModelType, wrapper)!;
                    }

                    if (itemViewModel != null)
                    {
                        Properties.Add(itemViewModel);
                    }
                }

                _cache.Add((obj, Properties.AsSpan().ToArray()));
            }

            if (_cache.Count > 7)
            {
                int count = _cache.Count - 7;
                for (int i = 0; i < count; i++)
                {
                    (CoreObject, BaseEditorViewModel[]) item = _cache[i];
                    for (int i1 = 0; i1 < item.Item2.Length; i1++)
                    {
                        BaseEditorViewModel editor = item.Item2[i1];
                        editor.Dispose();
                    }
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
                if (_backStack[i].TryGetTarget(out CoreObject? item) && ReferenceEquals(item, obj))
                {
                    start = i;
                    break;
                }
                count++;
            }

            _backStack.RemoveRange(start, count);
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

    public interface INavigationButtonViewModel
    {
        CoreObject? GetObject();
    }

    public sealed class NavigationButtonViewModel<T> : BaseEditorViewModel<T>, INavigationButtonViewModel
        where T : CoreObject
    {
        public NavigationButtonViewModel(IWrappedProperty<T> property)
            : base(property)
        {
        }

        public CoreObject? GetObject()
        {
            return WrappedProperty.GetValue();
        }
    }
}
