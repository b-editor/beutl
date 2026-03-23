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
        // ContentChanged->UI更新
        Observable.FromEventPattern(
                h => model.ContentChanged += h,
                h => model.ContentChanged -= h)
            .ObserveOnUIDispatcher()
            .Subscribe(_ => UpdateDisplay())
            .DisposeWith(_disposables);

        // IsPlaying, IsExpanded -> IsEnabled 連動
        var previewPlayer = nodeViewModel.EditorContext.GetRequiredService<IPreviewPlayer>();

        previewPlayer.IsPlaying.CombineLatest(nodeViewModel.IsExpanded)
            // IsPlayingがfalseで、かつIsExpandedがtrueのときのみ有効
            .Select(t => t is { First: false, Second: true })
            .DistinctUntilChanged()
            .Do(enabled => model.IsEnabled = enabled)
            .ObserveOnUIDispatcher()
            .Where(v => v) // 有効になったときのみ
            .Subscribe(_ => UpdateDisplay())
            .DisposeWith(_disposables);
    }

    public new INodeMonitor? Model => base.Model as INodeMonitor;

    public ReactivePropertySlim<string?> DisplayText { get; } = new();

    public Ref<Media.Bitmap>? DisplayBitmap { get; private set; }

    public event EventHandler? ImageInvalidated;

    private void UpdateDisplay()
    {
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

        try
        {
            var old = DisplayBitmap;
            DisplayBitmap = source.Clone();
            old?.Dispose();

            ImageInvalidated?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // 変換失敗時は何もしない
        }
    }

    protected override void OnDispose()
    {
        _disposables.Dispose();
        DisplayText.Dispose();
        DisplayBitmap?.Dispose();
        DisplayBitmap = null;
        base.OnDispose();
    }
}
