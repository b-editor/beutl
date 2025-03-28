using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public abstract partial class CoreObjectListItemEditor : UserControl, IListItemEditor
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(167));
    private CancellationTokenSource? _lastTransitionCts;

    public CoreObjectListItemEditor()
    {
        InitializeComponent();
        reorderHandle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                if (v == true)
                {
                    await s_transition.Start(null, content, localToken);
                }
                else
                {
                    await s_transition.Start(content, null, localToken);
                }
            });
    }

    public Control? ReorderHandle => reorderHandle;

    public event EventHandler? DeleteRequested;

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NewClick(object? sender, RoutedEventArgs e)
    {
        OnNew();
    }

    protected abstract void OnNew();
}

public sealed class CoreObjectListItemEditor<T> : CoreObjectListItemEditor
    where T : ICoreObject
{
    protected override async void OnNew()
    {
        //progress.IsVisible = true;
        if (DataContext is not CoreObjectEditorViewModel<T> { IsDisposed: false } viewModel) return;

        await Task.Run(async () =>
        {
            Type type = viewModel.PropertyAdapter.PropertyType;
            Type[] types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => x.GetTypes())
                .Where(x => !x.IsAbstract
                            && x.IsPublic
                            && x.IsAssignableTo(type)
                            && x.GetConstructor([]) != null)
                .ToArray();
            Type? type2 = null;
            ConstructorInfo? constructorInfo = null;

            if (types.Length == 1)
            {
                type2 = types[0];
            }
            else if (types.Length > 1)
            {
                type2 = await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var combobox = new ComboBox { ItemsSource = types, SelectedIndex = 0 };

                    var dialog = new ContentDialog
                    {
                        Content = combobox,
                        Title = Message.MultipleTypesAreAvailable,
                        PrimaryButtonText = Strings.OK,
                        CloseButtonText = Strings.Cancel
                    };

                    if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    {
                        return combobox.SelectedItem as Type;
                    }
                    else
                    {
                        return null;
                    }
                });
            }
            else if (type.IsSealed)
            {
                type2 = type;
            }

            constructorInfo = type2?.GetConstructor([]);

            if (constructorInfo?.Invoke(null) is T typed)
            {
                await Dispatcher.UIThread.InvokeAsync(() => viewModel.SetValue(viewModel.Value.Value, typed));
            }
        });

        //progress.IsVisible = false;
    }
}
