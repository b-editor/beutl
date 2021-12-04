using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;

using BEditorNext.Media;
using BEditorNext.Models;
using BEditorNext.ProjectSystem;

using FluentAvalonia.UI.Media;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Dialogs;

public sealed class AddLayerViewModel
{
    private readonly Scene _scene;
    private readonly LayerDescription _layerDescription;

    public AddLayerViewModel(Scene scene, LayerDescription desc)
    {
        _scene = scene;
        _layerDescription = desc;

        Color.Value = (desc.InitialOperation == null ? Colors.Teal : desc.InitialOperation.AccentColor).ToAvalonia();
        Layer.Value = desc.Layer;
        Start.Value = desc.Start;
        Duration.Value = desc.Length;
        Layer.SetValidateNotifyError(layer =>
        {
            if (ExistsLayer(layer))
            {
                return (string?)Application.Current.FindResource("ThisLayerNumberIsAlreadyInUseString");
            }
            else
            {
                return null;
            }
        });
        Start.SetValidateNotifyError(start =>
        {
            if (start < TimeSpan.Zero)
            {
                return (string?)Application.Current.FindResource("CannotSpecifyValueLessThanString");
            }
            else
            {
                return null;
            }
        });
        Duration.SetValidateNotifyError(length =>
        {
            if (length <= TimeSpan.Zero)
            {
                return (string?)Application.Current.FindResource("CannotSpecifyValueLessThanOrEqualToZeroString");
            }
            else
            {
                return null;
            }
        });

        CanAdd = Layer.CombineLatest(Start, Duration)
            .Select(item =>
            {
                (int layer, TimeSpan start, TimeSpan length) = item;
                return !ExistsLayer(layer) &&
                    start >= TimeSpan.Zero &&
                    length > TimeSpan.Zero;
            })
            .ToReadOnlyReactivePropertySlim();

        Add = new(CanAdd);

        Add.Subscribe(() =>
        {
            var sLayer = new SceneLayer(_scene)
            {
                Start = Start.Value,
                Length = Duration.Value,
                Layer = Layer.Value,
                AccentColor = new(Color.Value.A, Color.Value.R, Color.Value.G, Color.Value.B)
            };

            if (_layerDescription.InitialOperation != null)
            {
                sLayer.AddChild((RenderOperation)(Activator.CreateInstance(_layerDescription.InitialOperation.Type)!));
            }

            _scene.AddChild(sLayer);
        });
    }

    public ReactivePropertySlim<string> Name { get; } = new();

    public ReactivePropertySlim<Color2> Color { get; } = new();

    public ReactiveProperty<TimeSpan> Start { get; } = new();

    public ReactiveProperty<TimeSpan> Duration { get; } = new();

    public ReactiveProperty<int> Layer { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanAdd { get; }

    public ReactiveCommand Add { get; }

    private bool ExistsLayer(int layer)
    {
        foreach (SceneLayer item in _scene.Layers)
        {
            if (item.Layer == layer)
            {
                return true;
            }
        }

        return false;
    }
}
