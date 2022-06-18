using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Threading;

using BeUtl.Collections;
using BeUtl.Framework.Service;
using BeUtl.Models.Extensions.Develop;
using BeUtl.Models.ExtensionsPages.DevelopPages;
using BeUtl.Services;

using DynamicData;

using Firebase.Storage;

using Google.Cloud.Firestore;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using SkiaSharp;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageSettingsPageViewModel : IDisposable
{
    private readonly PackageController _packageController = ServiceLocator.Current.GetRequiredService<PackageController>();
    private readonly HttpClient _httpClient = ServiceLocator.Current.GetRequiredService<HttpClient>();
    private readonly CompositeDisposable _disposables = new();
    private readonly WeakReference<PackageDetailsPageViewModel?> _parentWeak;
    private readonly string _imagesPath;

    public PackageSettingsPageViewModel(DocumentReference docRef, PackageDetailsPageViewModel parent)
    {
        Reference = docRef;
        _imagesPath = $"users/{Reference.Parent.Parent.Id}/packages/{Reference.Id}/images";
        _parentWeak = new WeakReference<PackageDetailsPageViewModel?>(parent);

        // 入力用プロパティの作成（オリジナルが変更されたら同期する）
        Name = parent.Package.Select(p => p.Name)
            .ToReactiveProperty("")
            .DisposeWith(_disposables);
        DisplayName = parent.Package.Select(p => p.DisplayName)
            .ToReactiveProperty("")
            .DisposeWith(_disposables);
        Description = parent.Package.Select(p => p.Description)
            .ToReactiveProperty("")
            .DisposeWith(_disposables);
        ShortDescription = parent.Package.Select(p => p.ShortDescription)
            .ToReactiveProperty("")
            .DisposeWith(_disposables);
        LogoImageLink = parent.Package.Select(i => i.LogoImage)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        LogoImage = LogoImageLink
            .Do(_ => IsLogoLoading.Value = true)
            .SelectMany(async st => st != null ? await st.TryGetBitmapAsync() : null)
            .Do(_ => IsLogoLoading.Value = false)
            .ToReadOnlyReactivePropertySlim(null)
            .DisposeWith(_disposables);

        ScreenshotsArray = parent.Package
            .Select(i => i.Screenshots.Select(ii => ii.Name).ToArray())
            .ToReactiveProperty(Array.Empty<string>())
            .DisposeWith(_disposables);
        ScreenshotsArray.Subscribe(async array =>
        {
            IsScreenshotLoading.Value = true;
            var list = new List<ImageModel>(array.Length);
            foreach (string item in array)
            {
                ImageModel? exits = Screenshots.FirstOrDefault(i => i.Name == item);

                if (exits != null)
                {
                    list.Add(exits);
                }
                else
                {
                    MemoryStream? stream = await _packageController.GetPackageImageStream(Reference.Id, item);
                    if (stream != null)
                    {
                        var bitmap = new Bitmap(stream);
                        list.Add(new ImageModel(stream, bitmap, item));
                    }
                }
            }

            ImageModel[] excepted = Screenshots.Except(list).ToArray();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Screenshots.Clear();
                Screenshots.AddRange(list);
            });
            IsScreenshotLoading.Value = false;

            foreach (ImageModel item in excepted)
            {
                item.Stream.Dispose();
                item.Bitmap.Dispose();
            }
        }).DisposeWith(_disposables);

        // 値が変更されるか
        IsChanging = Name.CombineLatest(parent.Package).Select(t => t.First == t.Second.Name)
            .CombineLatest(
                DisplayName.CombineLatest(parent.Package).Select(t => t.First == t.Second.DisplayName),
                Description.CombineLatest(parent.Package).Select(t => t.First == t.Second.Description),
                ShortDescription.CombineLatest(parent.Package).Select(t => t.First == t.Second.ShortDescription),
                LogoImageLink.CombineLatest(parent.Package).Select(t => t.First == t.Second.LogoImage),
                ScreenshotsArray.CombineLatest(Screenshots.ToCollectionChanged<ImageModel>().Select(_ => Screenshots).Publish(Screenshots).RefCount())
                    .Select(t => t.First.SequenceEqual(t.Second.Select(i => i.Name))))
            .Select(t => !(t.First && t.Second && t.Third && t.Fourth && t.Fifth && t.Sixth))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // データ検証を設定
        Name.SetValidateNotifyError(NotNullOrWhitespace);
        DisplayName.SetValidateNotifyError(NotNullOrWhitespace);
        Description.SetValidateNotifyError(NotNullOrWhitespace);
        ShortDescription.SetValidateNotifyError(NotNullOrWhitespace);

        // コマンドを初期化
        // Todo: IsLogoLoading, IsScreenshotLoadingの場合、実行できないようにする。
        Save = new AsyncReactiveCommand(Name.ObserveHasErrors
            .CombineLatest(
                DisplayName.ObserveHasErrors,
                Description.ObserveHasErrors,
                ShortDescription.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third || t.Fourth)))
            .DisposeWith(_disposables);
        Save.Subscribe(async () =>
        {
            var newValue = new PackageInfo(
                DisplayName: Name.Value,
                Name: DisplayName.Value,
                Description: Description.Value,
                ShortDescription: ShortDescription.Value,
                IsVisible: Parent.IsPublic.Value,
                LogoImage: LogoImageLink.Value,
                Screenshots: Screenshots.Select(i => ImageLink.Open(_imagesPath, i.Name)).ToArray());

            ImageLink? newLogo = LogoImageLink.Value;
            ImageLink? oldLogo = Parent.Package.Value.LogoImage;
            if (newLogo != oldLogo && oldLogo != null)
            {
                await oldLogo.DeleteAsync();
            }

            string[] oldScreenshots = ScreenshotsArray.Value;
            ImageModel[] newScreenshots = Screenshots.ToArray();

            // 作成
            foreach (ImageModel item in newScreenshots.ExceptBy(oldScreenshots, i => i.Name))
            {
                item.Stream.Position = 0;
                await _packageController.GetPackageImageRef(Reference.Id, item.Name)
                    .PutAsync(item.Stream, default, "image/jpeg");
            }

            await Parent.Package.Value.SyncronizeToAsync(newValue, PackageInfoFields.None);

            // 削除
            foreach (string item in oldScreenshots.Except(newScreenshots.Select(i => i.Name)))
            {
                await _packageController.GetPackageImageRef(Reference.Id, item)
                    .DeleteAsync();
            }
        }).DisposeWith(_disposables);

        DiscardChanges.Subscribe(async () =>
        {
            IPackage.ILink link = Parent.Package.Value;
            Name.Value = link.Name;
            DisplayName.Value = link.DisplayName;
            Description.Value = link.Description;
            ShortDescription.Value = link.ShortDescription;

            ImageLink? newLogo = LogoImageLink.Value;
            if (link.LogoImage != newLogo && newLogo != null)
            {
                await newLogo.DeleteAsync();
            }
            LogoImageLink.Value = link.LogoImage;

            ScreenshotsArray.ForceNotify();
        }).DisposeWith(_disposables);

        Delete.Subscribe(async () =>
        {
            // Todo: コレクションやストレージのファイルを削除する
            await Parent.Package.Value.PermanentlyDeleteAsync();
        }).DisposeWith(_disposables);

        MakePublic.Subscribe(async () => await Parent.Package.Value.ChangeVisibility(true)).DisposeWith(_disposables);

        MakePrivate.Subscribe(async () => await Parent.Package.Value.ChangeVisibility(false)).DisposeWith(_disposables);

        SetLogo.Subscribe(async file =>
        {
            IsLogoLoading.Value = true;
            if (File.Exists(file))
            {
                const int SIZE = 400;
                var dstBmp = new SKBitmap(SIZE, SIZE, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using (var srcBmp = SKBitmap.Decode(file))
                using (var canvas = new SKCanvas(dstBmp))
                {
                    float x = SIZE / (float)srcBmp.Width;
                    float y = SIZE / (float)srcBmp.Height;
                    float w = srcBmp.Width * MathF.Max(x, y);
                    float h = srcBmp.Height * MathF.Max(x, y);
                    Rect rect = new Rect(0, 0, SIZE, SIZE)
                        .CenterRect(new Rect(0, 0, w, h));
                    canvas.DrawBitmap(srcBmp, rect.ToSKRect());
                    canvas.Flush();
                }

                var stream = new MemoryStream();
                dstBmp.Encode(stream, SKEncodedImageFormat.Jpeg, 100);
                dstBmp.Dispose();
                stream.Position = 0;

                LogoImageLink.Value = await ImageLink.UploadAsync(_imagesPath, Guid.NewGuid().ToString(), stream.GetBuffer());
            }
            IsLogoLoading.Value = false;
        }).DisposeWith(_disposables);

        CanAddScreenshot = Screenshots.ObserveProperty(i => i.Count)
            .Select(c => c < 4)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        AddScreenshot = new ReactiveCommand<string>(CanAddScreenshot)
            .DisposeWith(_disposables);
        AddScreenshot.Subscribe(file =>
        {
            if (File.Exists(file))
            {
                const int SIZE = 800;

                using (var srcBmp = SKBitmap.Decode(file))
                {
                    float x = SIZE / (float)srcBmp.Width;
                    float y = SIZE / (float)srcBmp.Height;
                    float w = srcBmp.Width * MathF.Max(x, y);
                    float h = srcBmp.Height * MathF.Max(x, y);
                    SKBitmap dstBmp = srcBmp.Resize(new SKImageInfo((int)w, (int)h, SKColorType.Bgra8888, SKAlphaType.Opaque), SKFilterQuality.Medium);
                    var stream = new MemoryStream();
                    dstBmp.Encode(stream, SKEncodedImageFormat.Jpeg, 100);
                    dstBmp.Dispose();
                    stream.Position = 0;

                    Screenshots.Add(new ImageModel(stream, new Bitmap(stream), Guid.NewGuid().ToString()));
                }
            }
        }).DisposeWith(_disposables);

        MoveScreenshotFront.Subscribe(item =>
        {
            int idx = Screenshots.IndexOf(item);
            if (idx == 0)
            {
                Screenshots.Move(idx, Screenshots.Count - 1);
            }
            else
            {
                Screenshots.Move(idx, idx - 1);
            }
        }).DisposeWith(_disposables);

        MoveScreenshotBack.Subscribe(item =>
        {
            int idx = Screenshots.IndexOf(item);
            if (idx == Screenshots.Count - 1)
            {
                Screenshots.Move(idx, 0);
            }
            else
            {
                Screenshots.Move(idx, idx + 1);
            }
        }).DisposeWith(_disposables);

        DeleteScreenshot.Subscribe(item =>
        {
            Screenshots.Remove(item);
            item.Bitmap.Dispose();
            item.Stream.Dispose();
        }).DisposeWith(_disposables);

        Parent.Package.Value.SubscribeResources(
            item =>
            {
                if (!Items.Any(p => p.Reference.Id == item.Id))
                {
                    ResourcePageViewModel viewModel = new(item.Reference, this);
                    viewModel.Update(item);
                    Items.Add(viewModel);
                }
            },
            item =>
            {
                ResourcePageViewModel? viewModel = Items.FirstOrDefault(p => p.Reference.Id == item.Id);
                if (viewModel != null)
                {
                    Items.Remove(viewModel);
                }
            },
            item =>
            {
                ResourcePageViewModel? viewModel = Items.FirstOrDefault(p => p.Reference.Id == item.Id);
                viewModel?.Update(item);
            })
            .DisposeWith(_disposables);
    }

    ~PackageSettingsPageViewModel()
    {
        Dispose();
    }

    public DocumentReference Reference { get; }

    public PackageDetailsPageViewModel Parent
        => _parentWeak.TryGetTarget(out PackageDetailsPageViewModel? parent)
            ? parent
            : null!;

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> DisplayName { get; } = new();

    public ReactiveProperty<string> Description { get; } = new();

    public ReactiveProperty<string> ShortDescription { get; } = new();

    public ReactiveProperty<ImageLink?> LogoImageLink { get; }

    public ReactivePropertySlim<bool> IsLogoLoading { get; } = new(false);

    public ReadOnlyReactivePropertySlim<Bitmap?> LogoImage { get; }

    public ReactiveCommand<string> SetLogo { get; } = new();

    public ReactiveProperty<string[]> ScreenshotsArray { get; }

    public CoreList<ImageModel> Screenshots { get; } = new();

    public ReactivePropertySlim<bool> IsScreenshotLoading { get; } = new(false);

    public AsyncReactiveCommand Save { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAddScreenshot { get; }

    public ReactiveCommand<string> AddScreenshot { get; }

    public ReactiveCommand<ImageModel> MoveScreenshotFront { get; } = new();

    public ReactiveCommand<ImageModel> MoveScreenshotBack { get; } = new();

    public ReactiveCommand<ImageModel> DeleteScreenshot { get; } = new();

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand MakePublic { get; } = new();

    public ReactiveCommand MakePrivate { get; } = new();

    public CoreList<ResourcePageViewModel> Items { get; } = new();

    public void Dispose()
    {
        Debug.WriteLine($"{GetType().Name} disposed (Count: {_disposables.Count}).");

        _disposables.Dispose();

        foreach (ResourcePageViewModel item in Items.AsSpan())
        {
            item.Dispose();
        }
        Items.Clear();

        GC.SuppressFinalize(this);
    }

    private static string NotNullOrWhitespace(string str)
    {
        if (!string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return "Please enter a string.";
        }
    }
}
