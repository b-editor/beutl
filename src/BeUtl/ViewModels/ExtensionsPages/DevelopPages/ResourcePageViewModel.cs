using System.Diagnostics;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Threading;

using BeUtl.Collections;
using BeUtl.Models.ExtensionsPages.DevelopPages;
using BeUtl.Services;

using Google.Cloud.Firestore;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using SkiaSharp;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class ResourcePageViewModel : IDisposable
{
    private readonly PackageController _packageController = ServiceLocator.Current.GetRequiredService<PackageController>();
    private readonly HttpClient _httpClient = ServiceLocator.Current.GetRequiredService<HttpClient>();
    private readonly WeakReference<PackageSettingsPageViewModel?> _parentWeak;
    private readonly CompositeDisposable _disposables = new();

    public ResourcePageViewModel(DocumentReference reference, PackageSettingsPageViewModel parent)
    {
        Reference = reference;
        _parentWeak = new WeakReference<PackageSettingsPageViewModel?>(parent);

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

            return "CultureNotFoundException";
        });

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

        HasDisplayName = ActualDisplayName.Select(s => s != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        HasDescription = ActualDescription.Select(s => s != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        HasShortDescription = ActualShortDescription.Select(s => s != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        DisplayName.SetValidateNotifyError(NotWhitespace);
        Description.SetValidateNotifyError(NotWhitespace);
        ShortDescription.SetValidateNotifyError(NotWhitespace);

        LogoStream = ActualLogoImageId
            .Do(_ => IsLogoLoading.Value = true)
            .SelectMany(id => _packageController.GetPackageImageStream(Parent.Reference.Id, id))
            .Do(_ => IsLogoLoading.Value = false)
            .DisposePreviousValue()
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        LogoImage = LogoStream
            .Select(st => st != null ? new Bitmap(st) : null)
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim(null)
            .DisposeWith(_disposables);

        ActualScreenshots.Subscribe(async array =>
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
                    MemoryStream? stream = await _packageController.GetPackageImageStream(Parent.Reference.Id, item);
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

        InheritDisplayName.Subscribe(b => DisplayName.Value = b ? null : ActualDisplayName.Value)
            .DisposeWith(_disposables);
        InheritDescription.Subscribe(b => Description.Value = b ? null : ActualDescription.Value)
            .DisposeWith(_disposables);
        InheritShortDescription.Subscribe(b => ShortDescription.Value = b ? null : ActualShortDescription.Value)
            .DisposeWith(_disposables);
        InheritLogo
            .Do(b => LogoStream.Value = b ? null : LogoStream.Value)
            .Subscribe(b => LogoImageId.Value = b ? null : ActualLogoImageId.Value)
            .DisposeWith(_disposables);
        InheritScreenshots
            .Where(b => b)
            .Subscribe(_ => Screenshots.Clear())
            .DisposeWith(_disposables);
        InheritScreenshots
            .Where(b => !b)
            .Subscribe(_ => ActualScreenshots.ForceNotify())
            .DisposeWith(_disposables);

        IsChanging = Culture.CombineLatest(ActualCulture).Select(t => t.First?.Name == t.Second?.Name)
            .CombineLatest(
                DisplayName.CombineLatest(ActualDisplayName).Select(t => t.First == t.Second),
                Description.CombineLatest(ActualDescription).Select(t => t.First == t.Second),
                ShortDescription.CombineLatest(ActualShortDescription).Select(t => t.First == t.Second),
                LogoImageId.CombineLatest(ActualLogoImageId).Select(t => t.First == t.Second),
                ActualScreenshots.CombineLatest(Screenshots.ToCollectionChanged<ImageModel>().Select(_ => Screenshots).Publish(Screenshots).RefCount())
                    .Select(t => t.First.SequenceEqual(t.Second.Select(i => i.Name))))
            .Select(t => !(t.First && t.Second && t.Third && t.Fourth && t.Fifth && t.Sixth))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Save = new AsyncReactiveCommand(CultureInput.ObserveHasErrors
            .CombineLatest(DisplayName.ObserveHasErrors, Description.ObserveHasErrors, ShortDescription.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third || t.Fourth)))
            .DisposeWith(_disposables);

        Save.Subscribe(async () =>
        {
            var dict = new Dictionary<string, object>
            {
                ["culture"] = (ActualCulture.Value = Culture.Value!).Name,
            };

            if (!InheritDisplayName.Value && DisplayName.Value != null)
            {
                dict["displayName"] = ActualDisplayName.Value = DisplayName.Value;
            }
            if (!InheritDescription.Value && Description.Value != null)
            {
                dict["description"] = ActualDescription.Value = Description.Value;
            }
            if (!InheritShortDescription.Value && ShortDescription.Value != null)
            {
                dict["shortDescription"] = ActualShortDescription.Value = ShortDescription.Value;
            }

            if (!InheritLogo.Value)
            {
                if (LogoStream.Value?.CanRead == true
                    && LogoImageId.Value is string newName)
                {
                    dict["logo"] = newName;
                    LogoStream.Value.Position = 0;
                    await _packageController.GetPackageImageRef(Parent.Reference.Id, newName)
                        .PutAsync(LogoStream.Value, default, "image/jpeg");

                    if (ActualLogoImageId.Value is string oldName0)
                    {
                        await _packageController.GetPackageImageRef(Parent.Reference.Id, oldName0)
                            .DeleteAsync();
                    }
                }
                else if (ActualLogoImageId.Value != null)
                {
                    dict["logo"] = ActualLogoImageId.Value;
                }
            }

            if (InheritLogo.Value && ActualLogoImageId.Value is string oldName1)
            {
                await _packageController.GetPackageImageRef(Parent.Reference.Id, oldName1)
                    .DeleteAsync();
            }

            string[]? oldScreenshots;
            ImageModel[]? newScreenshots;
            if (!InheritScreenshots.Value)
            {
                oldScreenshots = ActualScreenshots.Value;
                newScreenshots = Screenshots.ToArray();

                if (Screenshots.Count > 0)
                {
                    dict["screenshots"] = newScreenshots.Select(i => i.Name).ToArray();

                    // 作成
                    foreach (ImageModel item in newScreenshots.ExceptBy(oldScreenshots, i => i.Name))
                    {
                        item.Stream.Position = 0;
                        await _packageController.GetPackageImageRef(Parent.Reference.Id, item.Name)
                            .PutAsync(item.Stream, default, "image/jpeg");
                    }
                }
            }
            else
            {
                oldScreenshots = ActualScreenshots.Value;
                newScreenshots = Array.Empty<ImageModel>();
            }

            await Reference.SetAsync(dict, SetOptions.Overwrite);

            // 削除
            foreach (string item in oldScreenshots.Except(newScreenshots.Select(i => i.Name)))
            {
                await _packageController.GetPackageImageRef(Parent.Reference.Id, item)
                    .DeleteAsync();
            }
        })
            .DisposeWith(_disposables);

        DiscardChanges.Subscribe(async () => Update(await Reference.GetSnapshotAsync()))
            .DisposeWith(_disposables);

        Delete.Subscribe(async () =>
            {
                //Todo: 画像リソースの削除
                await Reference.DeleteAsync();
            })
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

    public DocumentReference Reference { get; }

    public PackageSettingsPageViewModel Parent
        => _parentWeak.TryGetTarget(out PackageSettingsPageViewModel? parent)
            ? parent
            : null!;

    public ReactivePropertySlim<string?> ActualDisplayName { get; } = new();

    public ReactivePropertySlim<string?> ActualDescription { get; } = new();

    public ReactivePropertySlim<string?> ActualShortDescription { get; } = new();

    public ReactivePropertySlim<string?> ActualLogoImageId { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> HasDisplayName { get; }

    public ReadOnlyReactivePropertySlim<bool> HasDescription { get; }

    public ReadOnlyReactivePropertySlim<bool> HasShortDescription { get; }

    public ReactivePropertySlim<CultureInfo> ActualCulture { get; } = new();

    public ReactiveProperty<string?> DisplayName { get; } = new();

    public ReactiveProperty<string?> Description { get; } = new();

    public ReactiveProperty<string?> ShortDescription { get; } = new();

    public ReactiveProperty<string?> LogoImageId { get; } = new();

    public ReactiveProperty<MemoryStream?> LogoStream { get; }

    public ReactivePropertySlim<bool> IsLogoLoading { get; } = new(false);

    public ReadOnlyReactivePropertySlim<Bitmap?> LogoImage { get; }

    public ReactiveCommand<string> SetLogo { get; } = new();

    public ReactiveProperty<string[]> ActualScreenshots { get; } = new(Array.Empty<string>());

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

    // Todo: Logo, screenshotsの設定
    public ReactiveProperty<bool> InheritLogo { get; } = new();

    public ReactiveProperty<bool> InheritScreenshots { get; } = new();

    public ReactiveProperty<string> CultureInput { get; } = new();

    public ReadOnlyReactivePropertySlim<CultureInfo?> Culture { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public AsyncReactiveCommand Save { get; }

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public void Update(DocumentSnapshot snapshot)
    {
        void Set(string name, IReactiveProperty<string?> property1, IReactiveProperty<string?> property2, IReactiveProperty<bool> property3)
        {
            if (snapshot.TryGetValue<string>(name, out string? value))
            {
                property1.Value = value;
                property2.Value = value;
                property3.Value = false;
            }
            else
            {
                property1.Value = null;
                property2.Value = null;
                property3.Value = true;
            }
        }

        Set("displayName", ActualDisplayName, DisplayName, InheritDisplayName);
        Set("description", ActualDescription, Description, InheritDescription);
        Set("shortDescription", ActualShortDescription, ShortDescription, InheritShortDescription);

        Set("logo", ActualLogoImageId, LogoImageId, InheritLogo);

        if (snapshot.TryGetValue<string[]>("screenshots", out string[]? screenshots))
        {
            ActualScreenshots.Value = screenshots;
            InheritScreenshots.Value = false;
        }
        else
        {
            ActualScreenshots.Value = Array.Empty<string>();
            InheritScreenshots.Value = false;
        }

        ActualCulture.Value = new CultureInfo(CultureInput.Value = snapshot.GetValue<string>("culture"));
    }

    private static string NotWhitespace(string? str)
    {
        if (str == null || !string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return "Please enter a string.";
        }
    }

    public void Dispose()
    {
        Debug.WriteLine($"{GetType().Name} disposed (Count: {_disposables.Count}).");

        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }
}
