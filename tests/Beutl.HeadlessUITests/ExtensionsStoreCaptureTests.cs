using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Controls.Styling.Themes;
using Beutl.Language;
using Beutl.Pages;
using Beutl.Pages.ExtensionsPages;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using FluentAvalonia.UI.Controls;
using LiveMarkdown.Avalonia;
using Reactive.Bindings;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

// Produces PNG captures of every extension store page, filled from a fake store, for manual design review.
[TestFixture]
[Explicit("Produces PNG captures for manual design review; not a regression test.")]
public class ExtensionsStoreCaptureTests
{
    [AvaloniaTest]
    [TestCase("dark", 800, 600)]
    [TestCase("light", 800, 600)]
    [TestCase("dark", 1200, 800)]
    [TestCase("dark", 480, 600)]
    public async Task Capture_store_pages(string theme, int width, int height)
    {
        ThemeCaptureTests.UseCaptureTheme(
            theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
            theme == "dark" ? BeutlDarkBorderTheme.AccentColor : null);
        await TestReset.ResetShellAsync();
        MainViewModel main = TestShell.MainViewModel;

        var store = new FakeStore();
        using var httpClient = new HttpClient(new FakeStoreHandler(store));
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var viewModel = new ExtensionsPageViewModel(clients, main.EditorService, main.ProjectService);
        var window = new ExtensionsPage { DataContext = viewModel, Width = width, Height = height };
        string suffix = $"{theme}-{width}x{height}";

        try
        {
            window.Show();
            FAFrame frame = window.FindControl<FAFrame>("frame")!;

            await SettleAsync(window, () => !viewModel.Discover.IsBusy.Value
                && viewModel.Discover.Items.OfType<Package>().Any());
            ThemeCaptureTests.Capture(window, $"store-home-{suffix}.png");

            Package featured = viewModel.Discover.Items.OfType<Package>().First();
            frame.Navigate(typeof(PackageDetailsPage), featured);
            await SettleAsync(window, () => frame.Content is PackageDetailsPage
            {
                DataContext: PackageDetailsPageViewModel { IsBusy.Value: false }
            } page && IsMarkdownReady(page));
            ThemeCaptureTests.Capture(window, $"store-details-{suffix}.png");
            ((PackageDetailsPage)frame.Content!).GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
            await SettleAsync(window, () => true);
            ThemeCaptureTests.Capture(window, $"store-details-end-{suffix}.png");

            frame.Navigate(typeof(SearchPage), "text");
            await HeadlessTestHelpers.WaitUntilAsync(() => frame.Content is SearchPage { DataContext: SearchPageViewModel });
            // The search page does not query on its own yet (#3763); start the search here.
            ((SearchPageViewModel)((SearchPage)frame.Content!).DataContext!).Refresh.Execute();
            await SettleAsync(window, () => frame.Content is SearchPage
            {
                DataContext: SearchPageViewModel { IsBusy.Value: false } search
            } && search.Packages.OfType<Package>().Any());
            ThemeCaptureTests.Capture(window, $"store-search-{suffix}.png");

            frame.Navigate(typeof(UserProfilePage), featured.Owner);
            await SettleAsync(window, () => frame.Content is UserProfilePage
            {
                DataContext: UserProfilePageViewModel { IsBusy.Value: false }
            });
            ThemeCaptureTests.Capture(window, $"store-profile-{suffix}.png");

            LibraryPageViewModel library = viewModel.Library;
            frame.Navigate(typeof(LibraryPage), library);
            await SettleAsync(window, () => frame.Content is LibraryPage && !library.IsBusy.Value);
            ShowLibraryRows(library, store);
            await SettleAsync(window, () => true);
            ThemeCaptureTests.Capture(window, $"store-library-{suffix}.png");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // Waits for the page's data and images, then lets the navigation transition finish.
    private static async Task SettleAsync(Window window, Func<bool> ready)
    {
        await HeadlessTestHelpers.WaitUntilAsync(ready, TimeSpan.FromSeconds(20));
        await HeadlessTestHelpers.WaitUntilAsync(
            () => window.GetVisualDescendants().OfType<AdvancedImage>()
                .All(image => image.Source is null || !image.IsLoading),
            TimeSpan.FromSeconds(20));
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(30);
            HeadlessTestHelpers.Render();
        }
    }

    private static bool IsMarkdownReady(PackageDetailsPage page)
    {
        return page.GetVisualDescendants().OfType<MarkdownRenderer>()
            .All(renderer => renderer.DocumentUpdate is not null);
    }

    private static void ShowLibraryRows(LibraryPageViewModel library, FakeStore store)
    {
        foreach (IDisposable item in library.Packages.OfType<IDisposable>())
        {
            item.Dispose();
        }

        library.Packages.Clear();
        library.Packages.Add(new StubLibraryRow(store.Packages[1], LibraryRowState.UpdateAvailable));
        library.Packages.Add(new StubLibraryRow(store.Packages[0], LibraryRowState.Installed));
        library.Packages.Add(new StubLibraryRow(store.Packages[2], LibraryRowState.Busy));
        library.Packages.Add(new StubLibraryRow(store.Packages[3], LibraryRowState.NotInstalled));
        library.Packages.Add(new StubLibraryRow(store.Packages[7], LibraryRowState.Installed));
    }

    private enum LibraryRowState
    {
        NotInstalled,
        Installed,
        UpdateAvailable,
        Busy,
    }

    private sealed record StorePackage(
        string Name,
        string DisplayName,
        string ShortDescription,
        string Description,
        string[] Tags,
        ProfileResponse Owner,
        string? Logo,
        string[] Screenshots,
        int? Price,
        string? Currency)
    {
        public SimplePackageResponse ToSimple() => new()
        {
            Id = Name,
            Owner = Owner,
            Name = Name,
            DisplayName = DisplayName,
            ShortDescription = ShortDescription,
            Tags = Tags,
            LogoId = Logo is null ? null : Name,
            LogoUrl = Logo,
            Currency = Currency,
            Price = Price,
            Paid = Price is > 0,
            Owned = false,
        };

        public PackageResponse ToResponse() => new()
        {
            Id = Name,
            Owner = Owner,
            Name = Name,
            DisplayName = DisplayName,
            Description = Description,
            ShortDescription = ShortDescription,
            WebSite = $"https://example.com/{Name.ToLowerInvariant()}",
            Tags = Tags,
            LogoId = Logo is null ? null : Name,
            LogoUrl = Logo,
            Screenshots = Screenshots,
            Currency = Currency,
            Price = Price,
            Paid = Price is > 0,
            Owned = false,
        };
    }

    // The packages, publishers and generated artwork the fake store serves.
    private sealed class FakeStore
    {
        private const string Description = """
            ## 特徴

            - プロジェクトにすぐ追加できるプリセットを同梱しています。
            - 色やタイミング、レイアウトを細かく調整できます。

            > 最新の Beutl に対応しています。

            [ドキュメント](https://example.com/docs)
            """;

        public FakeStore()
        {
            string directory = Path.Combine(Path.GetTempPath(), "beutl-store-capture-assets");
            Directory.CreateDirectory(directory);

            Team = new ProfileResponse
            {
                Id = "team",
                Name = "beutl-team",
                DisplayName = "Beutl Team",
                Bio = "Beutl の公式パッケージを公開しています。",
                IconId = "team",
                IconUrl = CreateImage(directory, "avatar-team", 160, 160, new SKColor(0x3B, 0x82, 0xF6), new SKColor(0x8B, 0x5C, 0xF6), Shape.Circle),
            };
            Studio = new ProfileResponse
            {
                Id = "studio",
                Name = "sample-studio",
                DisplayName = "Sample Studio",
                Bio = "モーショングラフィックス向けの素材とエフェクトを制作しています。",
                IconId = "studio",
                IconUrl = CreateImage(directory, "avatar-studio", 160, 160, new SKColor(0xF9, 0x73, 0x16), new SKColor(0xEF, 0x44, 0x44), Shape.Circle),
            };

            string[] screenshots =
            [
                CreateImage(directory, "shot-1", 640, 360, new SKColor(0x1E, 0x29, 0x3B), new SKColor(0x3B, 0x82, 0xF6), Shape.Wide),
                CreateImage(directory, "shot-2", 640, 360, new SKColor(0x31, 0x2E, 0x81), new SKColor(0xEC, 0x48, 0x99), Shape.Wide),
                CreateImage(directory, "shot-3", 640, 360, new SKColor(0x06, 0x4E, 0x3B), new SKColor(0x14, 0xB8, 0xA6), Shape.Wide),
            ];

            Packages =
            [
                new("Beutl.Particles", "Particle Emitter", "雨・雪・火花などのパーティクルを簡単に追加できます。", Description,
                    ["particles", "effects"], Team,
                    CreateImage(directory, "logo-particles", 256, 256, new SKColor(0x3B, 0x82, 0xF6), new SKColor(0x8B, 0x5C, 0xF6), Shape.Circle),
                    screenshots, null, null),
                new("Beutl.TextAnimator", "Text Animator", "文字単位のアニメーションでタイトルを演出します。", Description,
                    ["text", "animation"], Studio,
                    CreateImage(directory, "logo-text", 256, 256, new SKColor(0xF9, 0x73, 0x16), new SKColor(0xEF, 0x44, 0x44), Shape.Square),
                    screenshots, 500, "JPY"),
                new("Beutl.CinematicLuts", "Cinematic LUTs", "映画風のカラーグレーディング用 LUT を 24 種類収録。", Description,
                    [PackageKinds.MaterialTag, "color", "lut"], Studio,
                    CreateImage(directory, "logo-luts", 256, 256, new SKColor(0x14, 0xB8, 0xA6), new SKColor(0x22, 0xC5, 0x5E), Shape.Diamond),
                    [], null, null),
                new("Beutl.LowerThirds", "Lower Thirds", "テロップや名前表示のテンプレート集です。", Description,
                    [PackageKinds.TemplateTag, "text"], Team,
                    CreateImage(directory, "logo-thirds", 256, 256, new SKColor(0xEC, 0x48, 0x99), new SKColor(0x8B, 0x5C, 0xF6), Shape.Square),
                    screenshots, null, null),
                new("Beutl.SoundKit", "Sound Effects Kit", "効果音 120 種類（WAV）。", Description,
                    [PackageKinds.MaterialTag, "audio"], Studio,
                    CreateImage(directory, "logo-sound", 256, 256, new SKColor(0xEA, 0xB3, 0x08), new SKColor(0xF9, 0x73, 0x16), Shape.Circle),
                    [], 300, "JPY"),
                new("Beutl.GlitchFx", "Glitch FX", "デジタルノイズや RGB ずれのエフェクト。", Description,
                    ["effects"], Studio,
                    CreateImage(directory, "logo-glitch", 256, 256, new SKColor(0x22, 0xC5, 0x5E), new SKColor(0x06, 0xB6, 0xD4), Shape.Diamond),
                    screenshots, null, null),
                new("Beutl.Transitions", "Smooth Transitions", "シーン切り替え用のトランジションを 30 種類収録。", Description,
                    ["transitions"], Team,
                    CreateImage(directory, "logo-transitions", 256, 256, new SKColor(0x63, 0x66, 0xF1), new SKColor(0x3B, 0x82, 0xF6), Shape.Square),
                    [], null, null),
                new("Beutl.ChartMaker", "Chart Maker", "棒グラフや円グラフをアニメーション付きで作成します。", Description,
                    ["charts"], Studio, null, [], null, null),
            ];
        }

        private enum Shape
        {
            Circle,
            Square,
            Diamond,
            Wide,
        }

        public ProfileResponse Team { get; }

        public ProfileResponse Studio { get; }

        public StorePackage[] Packages { get; }

        public ReleaseResponse[] Releases { get; } =
        [
            new()
            {
                Id = "1.2.0",
                Version = "1.2.0",
                Title = "1.2.0",
                Description = "### 変更点\n\n- 描画のパフォーマンスを改善しました。\n- 一部の環境で表示が崩れる問題を修正しました。",
                TargetVersion = null,
                FileId = null,
                FileUrl = null,
            },
            new()
            {
                Id = "1.1.0",
                Version = "1.1.0",
                Title = "1.1.0",
                Description = "初回リリース",
                TargetVersion = null,
                FileId = null,
                FileUrl = null,
            },
        ];

        public ProfileResponse? FindOwner(string name)
            => new[] { Team, Studio }.FirstOrDefault(owner => owner.Name == name);

        public StorePackage? FindPackage(string name)
            => Packages.FirstOrDefault(package => package.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private static string CreateImage(string directory, string name, int width, int height, SKColor from, SKColor to, Shape shape)
        {
            string path = Path.Combine(directory, name + ".png");
            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            {
                using var fill = new SKPaint
                {
                    IsAntialias = true,
                    Shader = SKShader.CreateLinearGradient(
                        new SKPoint(0, 0), new SKPoint(width, height), [from, to], null, SKShaderTileMode.Clamp),
                };
                canvas.DrawRect(0, 0, width, height, fill);

                using var mark = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 220) };
                float cx = width / 2f;
                float cy = height / 2f;
                float r = Math.Min(width, height) * 0.22f;
                switch (shape)
                {
                    case Shape.Circle:
                        canvas.DrawCircle(cx, cy, r, mark);
                        break;
                    case Shape.Square:
                        canvas.DrawRoundRect(cx - r, cy - r, r * 2, r * 2, r * 0.3f, r * 0.3f, mark);
                        break;
                    case Shape.Diamond:
                        using (var path2 = new SKPath())
                        {
                            path2.MoveTo(cx, cy - r * 1.2f);
                            path2.LineTo(cx + r * 1.2f, cy);
                            path2.LineTo(cx, cy + r * 1.2f);
                            path2.LineTo(cx - r * 1.2f, cy);
                            path2.Close();
                            canvas.DrawPath(path2, mark);
                        }

                        break;
                    case Shape.Wide:
                        canvas.DrawRoundRect(width * 0.1f, height * 0.62f, width * 0.5f, height * 0.08f, 6, 6, mark);
                        canvas.DrawRoundRect(width * 0.1f, height * 0.74f, width * 0.32f, height * 0.06f, 6, 6, mark);
                        canvas.DrawCircle(width * 0.75f, height * 0.38f, height * 0.18f, mark);
                        break;
                }
            }

            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, encoded.ToArray());
            return path;
        }
    }

    // Serves the store API from FakeStore.
    private sealed class FakeStoreHandler(FakeStore store) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            string[] segments = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
            Dictionary<string, string> query = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => pair[0], pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "");
            bool firstPage = !query.TryGetValue("start", out string? start) || start == "0";

            object? response = segments switch
            {
                ["api", "v3", "discover", "featured"] => firstPage
                    ? Filter(store.Packages, query).Select(package => package.ToSimple()).ToArray()
                    : Array.Empty<SimplePackageResponse>(),
                ["api", "v3", "discover", "search"] => firstPage
                    ? Filter(store.Packages, query)
                        .Where(package => Matches(package, query.GetValueOrDefault("query", "")))
                        .Select(package => package.ToSimple())
                        .ToArray()
                    : Array.Empty<SimplePackageResponse>(),
                ["api", "v3", "users", var name, "packages"] => firstPage
                    ? store.Packages.Where(package => package.Owner.Name == name).Select(package => package.ToSimple()).ToArray()
                    : Array.Empty<SimplePackageResponse>(),
                ["api", "v3", "users", var name] => store.FindOwner(name),
                ["api", "v3", "packages", _, "releases"] => firstPage ? store.Releases : Array.Empty<ReleaseResponse>(),
                ["api", "v3", "packages", _, "releases", var version] => store.Releases.FirstOrDefault(release => release.Version == version),
                ["api", "v3", "packages", var name] => store.FindPackage(name)?.ToResponse(),
                ["api", "v3", "account", "library"] => Array.Empty<AcquirePackageResponse>(),
                _ => null,
            };

            if (response is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response, response.GetType(), s_options), Encoding.UTF8, "application/json"),
            });
        }

        private static IEnumerable<StorePackage> Filter(IEnumerable<StorePackage> packages, Dictionary<string, string> query)
        {
            return query.GetValueOrDefault("type", "all") switch
            {
                "extension" => packages.Where(package => package.Tags.GetPackageKind() == PackageKind.Extension),
                "material" => packages.Where(package => package.Tags.Contains(PackageKinds.MaterialTag)),
                "template" => packages.Where(package => package.Tags.Contains(PackageKinds.TemplateTag)),
                _ => packages,
            };
        }

        private static bool Matches(StorePackage package, string keyword)
        {
            return package.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || package.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || package.Tags.Any(tag => tag.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
    }

    // A library row in a fixed state, so the capture shows each action without running a real install.
    private sealed class StubLibraryRow : IUserPackageViewModel
    {
        public StubLibraryRow(StorePackage package, LibraryRowState state)
        {
            Name = package.Name;
            Publisher = package.Owner.Name;
            DisplayName = Observable.Return<string?>(package.DisplayName).ToReadOnlyReactivePropertySlim();
            LogoUrl = Observable.Return(package.Logo).ToReadOnlyReactivePropertySlim();
            IsInstallButtonVisible = Observable.Return(state == LibraryRowState.NotInstalled).ToReadOnlyReactivePropertySlim();
            IsUninstallButtonVisible = Observable.Return(state == LibraryRowState.Installed).ToReadOnlyReactivePropertySlim();
            IsUpdateButtonVisible = Observable.Return(state == LibraryRowState.UpdateAvailable).ToReadOnlyReactivePropertySlim();
            CanCancel = Observable.Return(false).ToReadOnlyReactivePropertySlim();
            IsBusy.Value = state == LibraryRowState.Busy;
            StatusText.Value = state == LibraryRowState.Busy ? ExtensionsStrings.Installing : null;
        }

        public string Name { get; }

        public IReadOnlyReactiveProperty<string?> DisplayName { get; }

        public IReadOnlyReactiveProperty<string?> LogoUrl { get; }

        public string Publisher { get; }

        public bool IsRemote => true;

        public ReadOnlyReactivePropertySlim<bool> IsInstallButtonVisible { get; }

        public ReadOnlyReactivePropertySlim<bool> IsUninstallButtonVisible { get; }

        public IReadOnlyReactiveProperty<bool> IsUpdateButtonVisible { get; }

        public ReadOnlyReactivePropertySlim<bool> CanCancel { get; }

        public AsyncReactiveCommand Install { get; } = new();

        public AsyncReactiveCommand Update { get; } = new();

        public AsyncReactiveCommand Uninstall { get; } = new();

        public AsyncReactiveCommand Cancel { get; } = new();

        public ReactiveCommand RemoveFromLibrary { get; } = new();

        public ReactivePropertySlim<bool> IsBusy { get; } = new();

        public ReactivePropertySlim<string?> StatusText { get; } = new();

        public void Dispose()
        {
        }
    }
}
