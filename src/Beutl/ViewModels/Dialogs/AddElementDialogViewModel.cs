using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Models;
using Beutl.Operation;
using Beutl.ProjectSystem;

using Reactive.Bindings;

using AColor = Avalonia.Media.Color;

namespace Beutl.ViewModels.Dialogs;

public sealed class AddElementDialogViewModel
{
    private readonly Scene _scene;
    private readonly ElementDescription _description;

    public AddElementDialogViewModel(Scene scene, ElementDescription desc)
    {
        _scene = scene;
        _description = desc;

        Color.Value = Colors.Teal.ToAvalonia();
        Layer.Value = desc.Layer;
        Start.Value = desc.Start;
        Duration.Value = desc.Length;
        Layer.SetValidateNotifyError(layer =>
        {
            if (layer < 0)
            {
                return Message.ValueLessThanZero;
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
                return Message.ValueLessThanZero;
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
                return Message.ValueLessThanOrEqualToZero;
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
                return layer >= 0 &&
                    start >= TimeSpan.Zero &&
                    length > TimeSpan.Zero;
            })
            .ToReadOnlyReactivePropertySlim();

        Add = new(CanAdd);

        Add.Subscribe(() =>
        {
            var element = new Element()
            {
                Name = Name.Value,
                Start = Start.Value,
                Length = Duration.Value,
                ZIndex = Layer.Value,
                AccentColor = new(Color.Value.A, Color.Value.R, Color.Value.G, Color.Value.B),
                FileName = RandomFileNameGenerator.Generate(Path.GetDirectoryName(_scene.FileName)!, Constants.ElementFileExtension)
            };

            if (_description.InitialOperator != null)
            {
                var op = (SourceOperator)Activator.CreateInstance(_description.InitialOperator)!;
                element.Operation.AddChild(op).Do();

                if (!_description.Position.IsDefault
                    && op.Properties.FirstOrDefault(v => v.PropertyType == typeof(ITransform)) is IAbstractProperty<ITransform?> transformp)
                {
                    ITransform? transform = transformp.GetValue();
                    var translate = new TranslateTransform(_description.Position);
                    if (transform is TransformGroup group)
                    {
                        group.Children.Add(translate);
                    }
                    else if (transform == null)
                    {
                        transformp.SetValue(translate);
                    }
                    else
                    {
                        transformp.SetValue(new TransformGroup
                        {
                            Children = { transform, translate }
                        });
                    }
                }
            }

            element.Save(element.FileName);
            _scene.AddChild(element).DoAndRecord(CommandRecorder.Default);
        });
    }

    public ReactivePropertySlim<string> Name { get; } = new();

    public ReactivePropertySlim<AColor> Color { get; } = new();

    public ReactiveProperty<TimeSpan> Start { get; } = new();

    public ReactiveProperty<TimeSpan> Duration { get; } = new();

    public ReactiveProperty<int> Layer { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanAdd { get; }

    public ReactiveCommand Add { get; }
}
