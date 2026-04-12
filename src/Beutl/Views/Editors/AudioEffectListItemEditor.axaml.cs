using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Views;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class AudioEffectListItemEditor : UserControl, IListItemEditor
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(167));
    private CancellationTokenSource? _lastTransitionCts;
    private FallbackObjectView? _fallbackObjectView;

    public AudioEffectListItemEditor()
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

        this.GetObservable(DataContextProperty)
            .Select(x => x as AudioEffectEditorViewModel)
            .Select(x => x?.IsFallback.Select(_ => x) ?? Observable.ReturnThenNever<AudioEffectEditorViewModel?>(null))
            .Switch()
            .Where(v => v?.IsFallback.Value == true)
            .Take(1)
            .Subscribe(_ =>
            {
                _fallbackObjectView = new FallbackObjectView();
                content.Children.Add(_fallbackObjectView);
            });

        reorderHandle.ContextFlyout = new FAMenuFlyout();
        CopyPasteMenuHelper.AddMenus((FAMenuFlyout)reorderHandle.ContextFlyout!, this);
    }

    public Control? ReorderHandle => reorderHandle;

    public event EventHandler? DeleteRequested;

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }
}
