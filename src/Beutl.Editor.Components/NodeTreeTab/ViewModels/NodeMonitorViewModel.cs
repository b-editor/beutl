using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.NodeTree;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public class NodeMonitorViewModel : NodeItemViewModel
{
    private readonly CompositeDisposable _disposables = [];

    public NodeMonitorViewModel(
        INodeMonitor model, IPropertyEditorContext? propertyEditorContext, NodeViewModel nodeViewModel)
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
            case NodeMonitorContentKind.Image when Model is NodeMonitor<Ref<IBitmap>?> imageMonitor:
                UpdateImage(imageMonitor.Value);
                break;
        }
    }

    private unsafe void UpdateImage(Ref<IBitmap>? source)
    {
        if (source == null)
        {
            DisplayBitmap = null;
            ImageInvalidated?.Invoke(this, EventArgs.Empty);
            return;
        }

        Bitmap<Bgra8888>? bitmap = null;
        bool shouldDispose = false;
        try
        {
            using var cloned = source.Clone();
            bitmap = cloned.Value as Bitmap<Bgra8888>;
            if (bitmap == null)
            {
                bitmap = cloned.Value.Convert<Bgra8888>();
                shouldDispose = true;
            }

            if (DisplayBitmap is { } existing
                && existing.PixelSize.Width == bitmap.Width
                && existing.PixelSize.Height == bitmap.Height)
            {
                // 画像サイズが同じなら再利用
            }
            else
            {
                DisplayBitmap = new WriteableBitmap(
                    new Avalonia.PixelSize(bitmap.Width, bitmap.Height),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul);
            }

            using (ILockedFramebuffer buf = DisplayBitmap.Lock())
            {
                int size = bitmap.ByteCount;
                Buffer.MemoryCopy((void*)bitmap.Data, (void*)buf.Address, size, size);
            }

            ImageInvalidated?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // 変換失敗時は何もしない
        }
        finally
        {
            if (shouldDispose)
                bitmap?.Dispose();
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
