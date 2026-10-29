using Beutl.Editor.Components.TimelineTab.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

public partial class SilenceSplitDialog : FAContentDialog
{
    private readonly SilenceSplitViewModel? _model;

    public SilenceSplitDialog() => InitializeComponent();

    public SilenceSplitDialog(SilenceSplitViewModel model) : this()
    {
        _model = model;
        DataContext = model;
        model.PreviewChanged += OnPreviewChanged;
        Opened += async (_, _) => await model.AnalyzeAsync();
        Closed += (_, _) =>
        {
            model.PreviewChanged -= OnPreviewChanged;
            model.Dispose();
        };
        OnPreviewChanged(this, EventArgs.Empty);
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private void OnPreviewChanged(object? sender, EventArgs e)
    {
        if (_model == null) return;
        Waveform.SetChunks(_model.PreviewChunks);
        Waveform.SetHighlights(_model.Ranges.Select(r => (
            (r.Start - _model.ClipRange.Start).ToSeconds() / _model.ClipRange.Duration.ToSeconds(),
            (r.End - _model.ClipRange.Start).ToSeconds() / _model.ClipRange.Duration.ToSeconds())).ToArray());
    }

    protected override async void OnPrimaryButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        if (_model?.CanApply.Value != true) { args.Cancel = true; return; }
        base.OnPrimaryButtonClick(args);
        if (args.Cancel) return;
        var deferral = args.GetDeferral();
        try { await _model.ApplyAsync(); }
        catch (Exception ex)
        {
            args.Cancel = true;
            _model.Status.Value = string.Format(Strings.SilenceSplit_Failed, ex.Message);
        }
        finally { deferral.Complete(); }
    }
}
