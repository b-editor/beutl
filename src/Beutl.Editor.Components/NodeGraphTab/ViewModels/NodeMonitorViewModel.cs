using Beutl.Editor.Services;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public class NodeMonitorViewModel : NodeMemberViewModel
{
    private readonly CompositeDisposable _disposables = [];

    public NodeMonitorViewModel(
        INodeMonitor model, IPropertyEditorContext? propertyEditorContext, GraphNodeViewModel nodeViewModel)
        : base(model, propertyEditorContext, nodeViewModel)
    {
        Observable.FromEventPattern(
                h => model.ContentChanged += h,
                h => model.ContentChanged -= h)
            .ObserveOnUIDispatcher()
            .Subscribe(_ => UpdateDisplay())
            .DisposeWith(_disposables);

        // 再生中でなく展開中のときだけモニターを動かす。
        var previewPlayer = nodeViewModel.EditorContext.GetRequiredService<IPreviewPlayer>();

        previewPlayer.IsPlaying.CombineLatest(nodeViewModel.IsExpanded)
            .Select(t => t is { First: false, Second: true })
            .DistinctUntilChanged()
            .Do(enabled => model.IsEnabled = enabled)
            .ObserveOnUIDispatcher()
            .Where(v => v)
            .Subscribe(_ => UpdateDisplay())
            .DisposeWith(_disposables);
    }

    public new INodeMonitor? Model => base.Model as INodeMonitor;

    public ReactivePropertySlim<string?> DisplayText { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public ReactivePropertySlim<string?> BusyText { get; } = new();

    public Ref<Media.Bitmap>? DisplayBitmap { get; private set; }

    public event EventHandler? ImageInvalidated;

    private void UpdateDisplay()
    {
        IsBusy.Value = Model?.IsBusy ?? false;
        BusyText.Value = Model?.BusyText;
        switch (Model?.ContentKind)
        {
            case NodeMonitorContentKind.Text when Model is NodeMonitor<string?> textMonitor:
                DisplayText.Value = textMonitor.Value;
                break;
            case NodeMonitorContentKind.Image when Model is NodeMonitor<Ref<Media.Bitmap>?> imageMonitor:
                UpdateImage(imageMonitor.Value);
                break;
        }
    }

    private void UpdateImage(Ref<Media.Bitmap>? source)
    {
        if (source == null)
        {
            DisplayBitmap?.Dispose();
            DisplayBitmap = null;
            ImageInvalidated?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (source.TryClone() is not { } clone) return;

        var old = DisplayBitmap;
        DisplayBitmap = clone;
        old?.Dispose();

        ImageInvalidated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnDispose()
    {
        _disposables.Dispose();
        DisplayText.Dispose();
        IsBusy.Dispose();
        BusyText.Dispose();
        DisplayBitmap?.Dispose();
        DisplayBitmap = null;
        base.OnDispose();
    }
}
