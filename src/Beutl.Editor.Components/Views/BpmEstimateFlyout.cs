using Avalonia;
using Avalonia.Controls;
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
        _view = new BpmEstimateView(current);
        _view.Applied += (_, options) =>
        {
            apply(options);
            Hide();
        };
        _view.Cancelled += (_, _) => Hide();
        Closed += (_, _) => _cancellation?.Cancel();
    }

    internal BpmEstimateView EstimateView => _view;

    protected override Control CreatePresenter()
    {
        return new FAPickerFlyoutPresenter
        {
            Width = 280,
            Padding = new Thickness(12),
            Content = _view,
        };
    }

    // Apply and Cancel live in the body: the estimate is editable before it reaches the grid.
    protected override bool ShouldShowConfirmationButtons() => false;

    protected override void OnConfirmed() => Hide();

    public async Task AnalyzeAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _view.ShowAnalyzing();
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
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsOpen)
            {
                _view.ShowError(string.Format(Strings.BpmEstimate_Failed, ex.Message));
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
}
