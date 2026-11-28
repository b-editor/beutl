using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Editor.Components.Views;

internal sealed class BpmEstimateFlyout : FAPickerFlyoutBase
{
    private readonly BpmEstimateView _view;
    private readonly Scene _scene;
    private readonly Element _element;
    private readonly FontManager _fonts;
    private readonly DecoderRegistry? _decoders;
    private readonly Action<BpmGridOptions> _apply;
    private FAPickerFlyoutPresenter? _presenter;
    private Button? _confirmButton;
    private CancellationTokenSource? _cancellation;

    public BpmEstimateFlyout(
        Scene scene,
        Element element,
        FontManager fonts,
        DecoderRegistry? decoders,
        BpmGridOptions current,
        Action<BpmGridOptions> apply)
    {
        _scene = scene;
        _element = element;
        _fonts = fonts;
        _decoders = decoders;
        _apply = apply;
        _view = new BpmEstimateView(current);
        _view.StateChanged += (_, _) => UpdateConfirmButton();
        Closed += (_, _) => _cancellation?.Cancel();
    }

    internal BpmEstimateView EstimateView => _view;

    internal Button? ConfirmButton => _confirmButton ??= FindButton("AcceptButton");

    internal Button? DismissButton => FindButton("DismissButton");

    protected override Control CreatePresenter()
    {
        _presenter = new FAPickerFlyoutPresenter
        {
            Width = 240,
            Padding = new Thickness(8),
            Content = _view,
        };
        _presenter.Confirmed += (_, _) => OnConfirmed();
        _presenter.Dismissed += (_, _) => Hide();
        return _presenter;
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        UpdateConfirmButton();
        // The template can apply after this callback; recheck once the popup has laid out.
        Dispatcher.UIThread.Post(UpdateConfirmButton, DispatcherPriority.Background);
    }

    protected override void OnConfirmed()
    {
        if (_view.TryCreateOptions(out BpmGridOptions options))
        {
            _apply(options);
            Hide();
        }
    }

    protected override bool ShouldShowConfirmationButtons() => true;

    public async Task AnalyzeAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _view.ShowAnalyzing();
        UpdateConfirmButton();
        try
        {
            BpmEstimate? estimate = await BpmEstimateService.AnalyzeAsync(
                _scene, _element, _fonts, _decoders, cancellation.Token,
                new Progress<int>(_view.ShowProgress));
            if (!IsOpen)
            {
                return;
            }

            if (estimate is { } result)
            {
                _view.ShowResult(result.Bpm, result.FirstBeatOffsetSeconds);
            }
            else
            {
                _view.ShowError(Strings.BpmEstimate_NoBeats);
            }

            UpdateConfirmButton();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsOpen)
            {
                _view.ShowError(string.Format(Strings.BpmEstimate_Failed, ex.Message));
                UpdateConfirmButton();
            }
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
            }
        }
    }

    private void UpdateConfirmButton()
    {
        if (FindButton("AcceptButton") is { } button)
        {
            _confirmButton = button;
            button.IsEnabled = _view.CanConfirm;
        }
    }

    private Button? FindButton(string name)
    {
        return _presenter?
            .GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == name);
    }
}
