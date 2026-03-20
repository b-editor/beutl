using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Controls;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Pixel;
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

    public WriteableBitmap? DisplayBitmap { get; private set; }

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

    private unsafe void UpdateImage(Ref<Media.Bitmap>? source)
    {
        if (source == null)
        {
            DisplayBitmap = null;
            ImageInvalidated?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            using var cloned = source.Clone();

            DisplayBitmap = cloned.Value.ToAvaWriteableBitmap(DisplayBitmap);

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
