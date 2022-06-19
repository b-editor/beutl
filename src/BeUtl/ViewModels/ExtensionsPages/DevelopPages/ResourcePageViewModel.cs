using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Threading;

using BeUtl.Models.Extensions.Develop;
using BeUtl.Models.ExtensionsPages.DevelopPages;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using SkiaSharp;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class ResourcePageViewModel : IDisposable
{
    private readonly WeakReference<PackageSettingsPageViewModel?> _parentWeak;
    private readonly CompositeDisposable _disposables = new();
    private readonly string _imagesPath;

    public ResourcePageViewModel(PackageSettingsPageViewModel parent, ILocalizedPackageResource.ILink link)
    {
        _parentWeak = new WeakReference<PackageSettingsPageViewModel?>(parent);
        _imagesPath = $"users/{parent.Reference.Parent.Parent.Id}/packages/{parent.Reference.Id}/images";
        Resource = link.GetObservable().ToReadOnlyReactivePropertySlim(link).DisposeWith(_disposables);

        // 入力用のプロパティ
        CultureInput = CreateStringInput(p => p.Culture.Name, Resource.Value.Culture.Name)!;
        DisplayName = CreateStringInput(p => p.DisplayName, "");
        Description = CreateStringInput(p => p.Description, "");
        ShortDescription = CreateStringInput(p => p.ShortDescription, "");
        LogoImageId = CreateStringInput(i => i.LogoImage?.Name, null);

        // 入力用プロパティ -> CultureInfo, Bitmap...
        Culture = CultureInput.Select(str =>
        {
            if (!string.IsNullOrWhiteSpace(str))
            {
                try
                {
                    return CultureInfo.GetCultureInfo(str);
                }
                catch { }
            }

            return null;
        })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        LogoStream = Resource.Select(i => i.LogoImage)
            .Do(_ => IsLogoLoading.Value = true)
            .SelectMany(async link => link != null ? await link.TryGetStreamAsync() : null)
            .Do(_ => IsLogoLoading.Value = false)
            .Do(ms => { if (ms != null) ms.Position = 0; })
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        LogoImage = LogoStream
            .Select(st => st != null ? new Bitmap(st) : null)
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim(null)
            .DisposeWith(_disposables);

        ScreenshotsArray = Resource.Select(p => p.Screenshots)
            .ToReactiveProperty(Resource.Value.Screenshots)
            .DisposeWith(_disposables);
        ScreenshotsArray
            .SelectMany(async array =>
            {
                IsScreenshotLoading.Value = true;
                var list = new List<ImageModel>(array.Length);
                foreach (ImageLink item in array)
                {
                    ImageModel? exits = Screenshots.FirstOrDefault(i => i.Name == item.Name);

                    if (exits != null)
                    {
                        list.Add(exits);
                    }
                    else
                    {
                        MemoryStream? stream = await item.TryGetStreamAsync();
                        if (stream != null)
                        {
                            var bitmap = new Bitmap(stream);
                            list.Add(new ImageModel(stream, bitmap, item.Name));
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

                return Unit.Default;
            })
            .Subscribe()
            .DisposeWith(_disposables);

        // 値を継承するかどうか
        InheritDisplayName = CreateInheritXXX(p => p.DisplayName == null);
        InheritDescription = CreateInheritXXX(p => p.Description == null);
        InheritShortDescription = CreateInheritXXX(p => p.ShortDescription == null);
        InheritLogo = CreateInheritXXX(i => i.LogoImage == null);
        InheritScreenshots = CreateInheritXXX(i => i.Screenshots.Length == 0);

        // 値を持っているか (nullじゃない)
        HasDisplayName = CreateHasXXX(s => s.DisplayName != null);
        HasDescription = CreateHasXXX(s => s.Description != null);
        HasShortDescription = CreateHasXXX(s => s.ShortDescription != null);

        // データ検証
        CultureInput.SetValidateNotifyError(str =>
        {
            if (!string.IsNullOrWhiteSpace(str))
            {
                try
                {
                    CultureInfo.GetCultureInfo(str);
                    return null!;
                }
                catch { }
            }

            return StringResources.Message.InvalidString;
        });
        DisplayName.SetValidateNotifyError(NotWhitespace);
        Description.SetValidateNotifyError(NotWhitespace);
        ShortDescription.SetValidateNotifyError(NotWhitespace);

        // プロパティを購読
        // 値を継承する場合、入力用プロパティにnullを、それ以外はもともとの値を設定する
        InheritDisplayName.Subscribe(b => DisplayName.Value = b ? null : Resource.Value.DisplayName)
            .DisposeWith(_disposables);
        InheritDescription.Subscribe(b => Description.Value = b ? null : Resource.Value.Description)
            .DisposeWith(_disposables);
        InheritShortDescription.Subscribe(b => ShortDescription.Value = b ? null : Resource.Value.ShortDescription)
            .DisposeWith(_disposables);
        InheritLogo
            .Do(async b => LogoStream.Value = (!b && Resource.Value.LogoImage is ImageLink logo) ? await logo.TryGetStreamAsync() : null)
            .Subscribe(b => LogoImageId.Value = b ? null : Resource.Value.LogoImage?.Name)
            .DisposeWith(_disposables);
        InheritScreenshots
            .Where(b => b)
            .Subscribe(_ => Screenshots.Clear())
            .DisposeWith(_disposables);
        InheritScreenshots
            .Where(b => !b)
            .Subscribe(_ => ScreenshotsArray.ForceNotify())
            .DisposeWith(_disposables);

        // 入力用プロパティが一つでも変更されたら、trueになる
        IsChanging = Culture.CombineLatest(Resource).Select(t => t.First?.Name == t.Second.Culture.Name)
            .CombineLatest(
                DisplayName.CombineLatest(Resource).Select(t => t.First == t.Second.DisplayName),
                Description.CombineLatest(Resource).Select(t => t.First == t.Second.Description),
                ShortDescription.CombineLatest(Resource).Select(t => t.First == t.Second.ShortDescription),
                LogoImageId.CombineLatest(Resource).Select(t => t.First == t.Second.LogoImage?.Name),
                ScreenshotsArray.CombineLatest(Screenshots.ToCollectionChanged<ImageModel>().Select(_ => Screenshots).Publish(Screenshots).RefCount())
                    .Select(t => t.First.Select(i => i.Name).SequenceEqual(t.Second.Select(i => i.Name))))
            .Select(t => !(t.First && t.Second && t.Third && t.Fourth && t.Fifth && t.Sixth))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // コマンド設定
        Save = new AsyncReactiveCommand(CultureInput.ObserveHasErrors
            .CombineLatest(DisplayName.ObserveHasErrors, Description.ObserveHasErrors, ShortDescription.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third || t.Fourth)))
            .DisposeWith(_disposables);

        Save.Subscribe(async () =>
        {
            ImageLink? newLogo = null;
            if (!InheritLogo.Value)
            {
                if (LogoStream.Value?.CanRead == true
                    && LogoImageId.Value is string newName)
                {
                    LogoStream.Value.Position = 0;
                    newLogo = await ImageLink.UploadAsync(_imagesPath, LogoImageId.Value, LogoStream.Value.GetBuffer());

                    if (Resource.Value.LogoImage is ImageLink oldLogo)
                    {
                        await oldLogo.DeleteAsync();
                    }
                }
                else if (Resource.Value.LogoImage != null)
                {
                    newLogo = Resource.Value.LogoImage;
                }
            }

            if (InheritLogo.Value && Resource.Value.LogoImage is ImageLink oldLogo1)
            {
                await oldLogo1.DeleteAsync();
            }

            var newResource = new LocalizedPackageResource(
                DisplayName: InheritDisplayName.Value ? null : DisplayName.Value,
                Description: InheritDescription.Value ? null : Description.Value,
                ShortDescription: InheritShortDescription.Value ? null : ShortDescription.Value,
                LogoImage: newLogo,
                Screenshots: Screenshots.Select(i => ImageLink.Open(_imagesPath, i.Name)).ToArray(),
                Culture: Culture.Value!);

            ImageLink[]? oldScreenshots;
            ImageModel[]? newScreenshots;
            if (!InheritScreenshots.Value)
            {
                oldScreenshots = ScreenshotsArray.Value;
                newScreenshots = Screenshots.ToArray();

                if (Screenshots.Count > 0)
                {
                    // 作成
                    foreach (ImageModel item in newScreenshots.ExceptBy(oldScreenshots.Select(i => i.Name), i => i.Name))
                    {
                        item.Stream.Position = 0;
                        await ImageLink.UploadAsync(_imagesPath, item.Name, item.Stream.GetBuffer());
                    }
                }
            }
            else
            {
                oldScreenshots = ScreenshotsArray.Value;
                newScreenshots = Array.Empty<ImageModel>();
            }

            await Resource.Value.SyncronizeToAsync(newResource, PackageResourceFields.None);

            // 削除
            foreach (ImageLink item in oldScreenshots.ExceptBy(newScreenshots.Select(i => i.Name), i => i.Name))
            {
                await item.DeleteAsync();
            }
        })
            .DisposeWith(_disposables);

        DiscardChanges.Subscribe(() =>
        {
            ILocalizedPackageResource.ILink link = Resource.Value;
            InheritDisplayName.Value = link.DisplayName == null;
            InheritDescription.Value = link.Description == null;
            InheritShortDescription.Value = link.ShortDescription == null;
            InheritLogo.Value = link.LogoImage == null;
            InheritScreenshots.Value = ScreenshotsArray.Value.Length == 0;
        })
            .DisposeWith(_disposables);

        Delete.Subscribe(async () => await Resource.Value.PermanentlyDeleteAsync())
            .DisposeWith(_disposables);

        SetLogo.Subscribe(file =>
        {
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
                LogoStream.Value = stream;

                InheritLogo.Value = false;
                LogoImageId.Value = Guid.NewGuid().ToString();
            }
        }).DisposeWith(_disposables);

        CanAddScreenshot = Screenshots.ObserveProperty(i => i.Count)
            .CombineLatest(InheritScreenshots)
            .Select(t => t.First < 4 && !t.Second)
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
    }

    ~ResourcePageViewModel()
    {
        Dispose();
    }

    public ReadOnlyReactivePropertySlim<ILocalizedPackageResource.ILink> Resource { get; }

    public PackageSettingsPageViewModel Parent
        => _parentWeak.TryGetTarget(out PackageSettingsPageViewModel? parent)
            ? parent
            : null!;

    public ReadOnlyReactivePropertySlim<bool> HasDisplayName { get; }

    public ReadOnlyReactivePropertySlim<bool> HasDescription { get; }

    public ReadOnlyReactivePropertySlim<bool> HasShortDescription { get; }

    public ReactiveProperty<string?> DisplayName { get; } = new();

    public ReactiveProperty<string?> Description { get; } = new();

    public ReactiveProperty<string?> ShortDescription { get; } = new();

    public ReactiveProperty<string?> LogoImageId { get; }

    public ReactiveProperty<MemoryStream?> LogoStream { get; }

    public ReactivePropertySlim<bool> IsLogoLoading { get; } = new(false);

    public ReadOnlyReactivePropertySlim<Bitmap?> LogoImage { get; }

    public ReactiveCommand<string> SetLogo { get; } = new();

    public ReactiveProperty<ImageLink[]> ScreenshotsArray { get; }

    public CoreList<ImageModel> Screenshots { get; } = new();

    public ReactivePropertySlim<bool> IsScreenshotLoading { get; } = new(false);

    public ReadOnlyReactivePropertySlim<bool> CanAddScreenshot { get; }

    public ReactiveCommand<string> AddScreenshot { get; }

    public ReactiveCommand<ImageModel> MoveScreenshotFront { get; } = new();

    public ReactiveCommand<ImageModel> MoveScreenshotBack { get; } = new();

    public ReactiveCommand<ImageModel> DeleteScreenshot { get; } = new();

    public ReactiveProperty<bool> InheritDisplayName { get; } = new();

    public ReactiveProperty<bool> InheritDescription { get; } = new();

    public ReactiveProperty<bool> InheritShortDescription { get; } = new();

    public ReactiveProperty<bool> InheritLogo { get; } = new();

    public ReactiveProperty<bool> InheritScreenshots { get; } = new();

    public ReactiveProperty<string> CultureInput { get; } = new();

    public ReadOnlyReactivePropertySlim<CultureInfo?> Culture { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public AsyncReactiveCommand Save { get; }

    public ReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    private static string NotWhitespace(string? str)
    {
        if (str == null || !string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return StringResources.Message.PleaseEnterString;
        }
    }

    private ReactiveProperty<string?> CreateStringInput(Func<ILocalizedPackageResource.ILink, string?> func, string? initial)
    {
        return Resource.Select(func)
            .ToReactiveProperty(initial)
            .DisposeWith(_disposables);
    }

    private ReadOnlyReactivePropertySlim<bool> CreateHasXXX(Func<ILocalizedPackageResource.ILink, bool> func)
    {
        return Resource.Select(func)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    private ReactiveProperty<bool> CreateInheritXXX(Func<ILocalizedPackageResource.ILink, bool> func)
    {
        return Resource.Select(func)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
    }

    public void Dispose()
    {
        Debug.WriteLine($"{GetType().Name} disposed (Count: {_disposables.Count}).");

        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }
}
