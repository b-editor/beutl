using System.Collections;

using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Commands;
using BeUtl.ProjectSystem;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.ViewModels.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.AnimationEditors;

public abstract class AnimationEditorViewModel : IDisposable
{
    protected CompositeDisposable Disposables = new();
    private bool _disposedValue;

    protected AnimationEditorViewModel(IAnimationSpan animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
    {
        Animation = animation;
        Description = description;
        OptionsProvider = optionsProvider;

        Width = animation.GetObservable(AnimationSpan.DurationProperty)
            .CombineLatest(optionsProvider.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReactiveProperty()
            .AddTo(Disposables);

        Width.Subscribe(w => animation.Duration = w.ToTimeSpan(optionsProvider.Options.Value.Scale)).AddTo(Disposables);

        RemoveCommand.Subscribe(() => RemoveItem()).AddTo(Disposables);

        Header = WrappedProperty.Header
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    ~AnimationEditorViewModel()
    {
        if (!_disposedValue)
            Dispose(false);
    }

    public IAnimationSpan Animation { get; }

    public IWrappedProperty.IAnimatable WrappedProperty => (IWrappedProperty.IAnimatable)Description.WrappedProperty;

    public EditorViewModelDescription Description { get; }

    public bool CanReset => WrappedProperty.GetDefaultValue() != null;

    public ReadOnlyReactivePropertySlim<string?> Header { get; }

    public ReactiveProperty<double> Width { get; }

    public ReactiveCommand RemoveCommand { get; } = new();

    public ITimelineOptionsProvider OptionsProvider { get; }

    public void SetDuration(TimeSpan old, TimeSpan @new)
    {
        if (OptionsProvider.Scene.Parent is Project proj)
        {
            @new = @new.RoundToRate(proj.GetFrameRate());
        }

        CommandRecorder.Default.DoAndPush(
            new ChangePropertyCommand<TimeSpan>(Animation, AnimationSpan.DurationProperty, @new, old));
    }

    public void SetEasing(Easing old, Easing @new)
    {
        CommandRecorder.Default.DoAndPush(new ChangePropertyCommand<Easing>(Animation, AnimationSpan.EasingProperty, @new, old));
    }

    public void Move(int newIndex, int oldIndex)
    {
        if (WrappedProperty.Animation.Children is IList list)
        {
            new MoveCommand(list, newIndex, oldIndex).DoAndRecord(CommandRecorder.Default);
        }
    }

    public void InsertForward(Easing easing)
    {
        if (WrappedProperty.Animation.Children is IList list)
        {
            int index = list.IndexOf(Animation);
            Type type = typeof(AnimationSpan<>).MakeGenericType(WrappedProperty.AssociatedProperty.PropertyType);

            if (Activator.CreateInstance(type) is IAnimationSpan animation)
            {
                animation.Easing = easing;
                animation.Duration = TimeSpan.FromSeconds(2);
                object? value = WrappedProperty.GetValue();

                if (value != null)
                {
                    animation.Previous = value;
                    animation.Next = value;
                }

                InsertItem(index, animation);
            }
        }
    }

    public void InsertBackward(Easing easing)
    {
        if (WrappedProperty.Animation.Children is IList list)
        {
            int index = list.IndexOf(Animation);
            Type type = typeof(AnimationSpan<>).MakeGenericType(WrappedProperty.AssociatedProperty.PropertyType);

            if (Activator.CreateInstance(type) is IAnimationSpan animation)
            {
                animation.Easing = easing;
                animation.Duration = TimeSpan.FromSeconds(2);
                object? value = WrappedProperty.GetValue();

                if (value != null)
                {
                    animation.Previous = value;
                    animation.Next = value;
                }

                InsertItem(index + 1, animation);
            }
        }
    }

    public void Dispose()
    {
        if (!_disposedValue)
        {
            Dispose(true);
            _disposedValue = true;
            GC.SuppressFinalize(this);
        }
    }

    private void InsertItem(int index, IAnimationSpan item)
    {
        if (WrappedProperty.Animation.Children is IList list)
        {
            new AddCommand(list, item, index).DoAndRecord(CommandRecorder.Default);
        }
    }

    private void RemoveItem()
    {
        if (WrappedProperty.Animation.Children is IList list)
        {
            new RemoveCommand(list, Animation).DoAndRecord(CommandRecorder.Default);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        Disposables.Dispose();
    }
}

public class AnimationEditorViewModel<T> : AnimationEditorViewModel
{
    public AnimationEditorViewModel(AnimationSpan<T> animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
        : base(animation, description, optionsProvider)
    {
    }

    internal AnimationEditorViewModel(IAnimationSpan animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
        : base(animation, description, optionsProvider)
    {
    }

    public new AnimationSpan<T> Animation => (AnimationSpan<T>)base.Animation;

    public new IWrappedProperty<T>.IAnimatable WrappedProperty => (IWrappedProperty<T>.IAnimatable)base.WrappedProperty;
}
