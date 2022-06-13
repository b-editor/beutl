using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Skia;

using BeUtl.Collections;
using BeUtl.Framework.Service;
using BeUtl.Services;

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
    private readonly INotificationService _notification = ServiceLocator.Current.GetRequiredService<INotificationService>();
    private readonly HttpClient _httpClient = ServiceLocator.Current.GetRequiredService<HttpClient>();
    private readonly CompositeDisposable _disposables = new();
    private readonly object _lockObject = new();
    private readonly FirestoreChangeListener? _listener;
    private CancellationTokenSource? _cts;

    public PackageSettingsPageViewModel(DocumentReference docRef, PackageDetailsPageViewModel parent)
    {
        Reference = docRef;
        Parent = parent;

        Name = parent.Name.ToReactiveProperty("").DisposeWith(_disposables);
        DisplayName = parent.DisplayName.ToReactiveProperty("").DisposeWith(_disposables);
        Description = parent.Description.ToReactiveProperty("").DisposeWith(_disposables);
        ShortDescription = parent.ShortDescription.ToReactiveProperty("").DisposeWith(_disposables);
        Logo = parent.Logo.ToReactiveProperty().DisposeWith(_disposables);
        LogoId = parent.LogoId.ToReactiveProperty().DisposeWith(_disposables);
        LogoImage = Logo.SelectMany(async uri => uri != null ? await _httpClient.GetByteArrayAsync(uri) : null)
            .Select(arr => arr != null ? new MemoryStream(arr) : null)
            .Select(st => (st, st != null ? new Bitmap(st) : null))
            .Do(t => t.st?.Dispose())
            .Select(t => t.Item2)
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim(null)
            .DisposeWith(_disposables);

        IsChanging = Name.CombineLatest(parent.Name).Select(t => t.First == t.Second)
            .CombineLatest(
                DisplayName.CombineLatest(parent.DisplayName).Select(t => t.First == t.Second),
                Description.CombineLatest(parent.Description).Select(t => t.First == t.Second),
                ShortDescription.CombineLatest(parent.ShortDescription).Select(t => t.First == t.Second),
                LogoId.CombineLatest(parent.LogoId).Select(t => t.First == t.Second))
            .Select(t => !(t.First && t.Second && t.Third && t.Fourth && t.Fifth))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // データ検証を設定
        Name.SetValidateNotifyError(NotNullOrWhitespace);
        DisplayName.SetValidateNotifyError(NotNullOrWhitespace);
        Description.SetValidateNotifyError(NotNullOrWhitespace);
        ShortDescription.SetValidateNotifyError(NotNullOrWhitespace);

        // コマンドを初期化
        Save = new AsyncReactiveCommand(Name.ObserveHasErrors
            .CombineLatest(
                DisplayName.ObserveHasErrors,
                Description.ObserveHasErrors,
                ShortDescription.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third || t.Fourth)));
        Save.Subscribe(async () =>
        {
            var dict = new Dictionary<string, object>
            {
                ["name"] = Name.Value,
                ["displayName"] = DisplayName.Value,
                ["description"] = Description.Value,
                ["shortDescription"] = ShortDescription.Value,
                ["visible"] = Parent.IsPublic.Value
            };

            if (LogoId.Value is string newLogo)
            {
                dict["logo"] = newLogo;

                if (Parent.LogoId.Value is string oldLogo && newLogo != oldLogo)
                {
                    await _packageController.GetPackageImageRef(Reference.Id, oldLogo)
                        .DeleteAsync();
                }
            }

            await Reference.SetAsync(dict, SetOptions.Overwrite);
        }).DisposeWith(_disposables);

        DiscardChanges.Subscribe(async () =>
        {
            DocumentSnapshot snapshot = await Reference.GetSnapshotAsync();
            Name.Value = snapshot.GetValue<string>("name");
            DisplayName.Value = snapshot.GetValue<string>("displayName");
            Description.Value = snapshot.GetValue<string>("description");
            ShortDescription.Value = snapshot.GetValue<string>("shortDescription");

            if (LogoId.Value is string logo && Parent.LogoId.Value != logo)
            {
                await _packageController.GetPackageImageRef(Reference.Id, logo)
                    .DeleteAsync();
            }

            Logo.Value = Parent.Logo.Value;
            LogoId.Value = Parent.LogoId.Value;
        }).DisposeWith(_disposables);

        Delete.Subscribe(async () => await Reference.DeleteAsync()).DisposeWith(_disposables);

        MakePublic.Subscribe(async () => await Reference.UpdateAsync("visible", true)).DisposeWith(_disposables);

        MakePrivate.Subscribe(async () => await Reference.UpdateAsync("visible", false)).DisposeWith(_disposables);

        SetLogo.Subscribe(async file =>
        {
            if (File.Exists(file))
            {
                _cts?.Cancel();
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

                using var dstStream = new MemoryStream();
                dstBmp.Encode(dstStream, SKEncodedImageFormat.Jpeg, 100);
                dstBmp.Dispose();
                dstStream.Position = 0;

                string name = Parent.LogoId.Value == "logo1" ? "logo2" : "logo1";
                FirebaseStorageReference reference = _packageController.GetPackageImageRef(Reference.Id, name);
                Logo.Value = await _packageController.UploadImage(dstStream, reference);
                LogoId.Value = name;

                _cts = new CancellationTokenSource();
                CancellationToken token = _cts.Token;
                await Task.Delay(TimeSpan.FromMinutes(1));

                if (!token.IsCancellationRequested
                    && LogoId.Value is string logo
                    && Parent.LogoId.Value != logo)
                {
                    Logo.Value = Parent.Logo.Value;
                    LogoId.Value = Parent.LogoId.Value;
                    await _packageController.GetPackageImageRef(Reference.Id, logo)
                        .DeleteAsync();

                    _notification.Show(new Notification(Parent.Name.Value, "ロゴ画像の変更がキャンセルされました。"));
                }
            }
        }).DisposeWith(_disposables);

        CollectionReference? resources = Parent.Reference.Collection("resources");
        resources?.GetSnapshotAsync()
            .ToObservable()
            .Subscribe(snapshot =>
            {
                foreach (DocumentSnapshot item in snapshot.Documents)
                {
                    lock (_lockObject)
                    {
                        if (!Items.Any(p => p.Reference.Id == item.Reference.Id))
                        {
                            var viewModel = new ResourcePageViewModel(item.Reference, this);
                            viewModel.Update(item);
                            Items.Add(viewModel);
                        }
                    }
                }
            });

        _listener = resources?.Listen(snapshot =>
        {
            foreach (DocumentChange item in snapshot.Changes)
            {
                lock (_lockObject)
                {
                    switch (item.ChangeType)
                    {
                        case DocumentChange.Type.Added when item.NewIndex.HasValue:
                            if (!Items.Any(p => p.Reference.Id == item.Document.Reference.Id))
                            {
                                var viewModel = new ResourcePageViewModel(item.Document.Reference, this);
                                viewModel.Update(item.Document);
                                Items.Add(viewModel);
                            }
                            break;
                        case DocumentChange.Type.Removed when item.OldIndex.HasValue:
                            foreach (ResourcePageViewModel viewModel in Items)
                            {
                                if (viewModel.Reference.Id == item.Document.Id)
                                {
                                    Items.Remove(viewModel);
                                    return;
                                }
                            }
                            break;
                        case DocumentChange.Type.Modified:
                            foreach (ResourcePageViewModel viewModel in Items)
                            {
                                if (viewModel.Reference.Id == item.Document.Id)
                                {
                                    viewModel.Update(item.Document);
                                    return;
                                }
                            }
                            break;
                    }
                }
            }
        });
    }

    public DocumentReference Reference { get; }

    public PackageDetailsPageViewModel Parent { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> DisplayName { get; } = new();

    public ReactiveProperty<string> Description { get; } = new();

    public ReactiveProperty<string> ShortDescription { get; } = new();

    public ReadOnlyReactivePropertySlim<Bitmap?> LogoImage { get; }

    public ReactiveProperty<Uri?> Logo { get; } = new();

    public ReactiveProperty<string?> LogoId { get; } = new();

    public ReactiveCommand<string> SetLogo { get; } = new();

    public AsyncReactiveCommand Save { get; }

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand MakePublic { get; } = new();

    public ReactiveCommand MakePrivate { get; } = new();

    public CoreList<ResourcePageViewModel> Items { get; } = new();

    public void Dispose()
    {
        _disposables?.Dispose();

        _listener?.StopAsync();

        Items.Clear();
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
