using Beutl.Media;
using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using AM = Avalonia.Media;

namespace Beutl.ViewModels.Editors;

public class GradientStopsEditorViewModel : BaseEditorViewModel<ICoreList<GradientStop>>
{
    private IDisposable? _disposable;

    public GradientStopsEditorViewModel(IPropertyAdapter<ICoreList<GradientStop>> property)
        : base(property)
    {
        Value = property.GetObservable()!
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables)!;

        Value.Subscribe(v =>
        {
            _disposable?.Dispose();

            var t = v.ToAvaGradientStopsSync(CurrentTime);
            _disposable = t.Item2;
            Stops.Value = t.Item1;
        }).DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<ICoreList<GradientStop>> Value { get; }

    public ReactivePropertySlim<AM.GradientStops> Stops { get; } = new();

    public ReactivePropertySlim<AM.GradientStop?> SelectedItem { get; } = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _disposable?.Dispose();
    }

    public void InsertGradientStop(int index, GradientStop item)
    {
        if (Value.Value is { } list)
        {
            list.Insert(index, item);
            Commit();
        }
    }

    public void RemoveGradientStop(int index)
    {
        if (Value.Value is { } list)
        {
            list.RemoveAt(index);
            Commit();
        }
    }

    public void ConfirmeGradientStop(
        int oldIndex, int newIndex,
        GradientStop.Resource oldObject, GradientStop obj)
    {
        if (Value.Value is { } list)
        {
            if (oldIndex != newIndex)
            {
                list.Move(oldIndex, newIndex);
            }

            Commit();
        }
    }

}
