
using System.Net.Mime;

using Avalonia.Collections;

using Beutl.Api;
using Beutl.Api.Objects;

using BeUtl.Configuration;

using FluentIcons.Common;

using Reactive.Bindings;

namespace BeUtl.ViewModels.SettingsPages;

public sealed class StorageSettingsPageViewModel
{
    private readonly BackupConfig _config;
    private readonly IReadOnlyReactiveProperty<AuthorizedUser?> _user;
    private readonly ReactivePropertySlim<StorageUsageResponse?> _storageUsageResponse = new();

    public StorageSettingsPageViewModel(IReadOnlyReactiveProperty<AuthorizedUser?> user)
    {
        _config = GlobalConfiguration.Instance.BackupConfig;
        BackupSettings = _config.GetObservable(BackupConfig.BackupSettingsProperty).ToReactiveProperty();
        BackupSettings.Subscribe(b => _config.BackupSettings = b);

        Percent = _storageUsageResponse.Where(x => x != null)
            .Select(x => x!.Size / (double)x.Max_size)
            .ToReadOnlyReactivePropertySlim();

        MaxSize = _storageUsageResponse.Where(x => x != null)
            .Select(x => ToHumanReadableSize(x!.Max_size))
            .ToReadOnlyReactivePropertySlim()!;

        Using = _storageUsageResponse.Where(x => x != null)
            .Select(x => ToHumanReadableSize(x!.Size))
            .ToReadOnlyReactivePropertySlim()!;

        Remaining = _storageUsageResponse.Where(x => x != null)
            .Select(x => ToHumanReadableSize(x!.Max_size - x.Size))
            .ToReadOnlyReactivePropertySlim()!;

        _storageUsageResponse.Where(x => x != null)
            .Subscribe(x =>
            {
                DetailItem ToDetailItem(DetailItem defaultValue, long size)
                {
                    if (size == 0)
                    {
                        return defaultValue;
                    }

                    return new DetailItem(defaultValue.Type, ToHumanReadableSize(size), size, size / (double)x.Size);
                }

                Details.Clear();
                long imageSize = 0;
                long zipSize = 0;
                long textSize = 0;
                long fontSize = 0;
                long otherSize = 0;

                foreach ((string key, long value) in x!.Details)
                {
                    switch (ToKnownType(key))
                    {
                        case KnownType.Image:
                            imageSize += value;
                            break;
                        case KnownType.Zip:
                            zipSize += value;
                            break;
                        case KnownType.Text:
                            textSize += value;
                            break;
                        case KnownType.Font:
                            fontSize += value;
                            break;
                        case KnownType.Other:
                            otherSize += value;
                            break;
                    }
                }

                Details.Add(ToDetailItem(DetailItem.Image, imageSize));
                Details.Add(ToDetailItem(DetailItem.Zip, zipSize));
                Details.Add(ToDetailItem(DetailItem.Text, textSize));
                Details.Add(ToDetailItem(DetailItem.Font, fontSize));
                Details.Add(ToDetailItem(DetailItem.Other, otherSize));
            });

        _user = user;
        _user.Skip(1).Subscribe(async _ =>
        {
            if (Refresh.CanExecute())
            {
                await Refresh.ExecuteAsync();
            }
        });
        Refresh.Subscribe(async () =>
        {
            if (_user.Value == null)
            {
                FillBlankItems();
                return;
            }

            try
            {
                IsBusy.Value = true;
                await _user.Value.RefreshAsync();
                _storageUsageResponse.Value = await _user.Value.StorageUsageAsync();
            }
            catch
            {
                // Todo
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        _user.Where(x => x != null)
            .Timeout(TimeSpan.FromSeconds(10))
            .Subscribe(
                _ => Refresh.Execute(),
                ex => { },
                () => { });
    }

    public ReactiveProperty<bool> BackupSettings { get; }

    public ReadOnlyReactivePropertySlim<double> Percent { get; }

    public ReadOnlyReactivePropertySlim<string> MaxSize { get; }

    public ReadOnlyReactivePropertySlim<string> Using { get; }

    public ReadOnlyReactivePropertySlim<string> Remaining { get; }

    public AvaloniaList<DetailItem> Details { get; } = new(5);

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public static KnownType ToKnownType(string mediaType)
    {
        if (mediaType.StartsWith("image/"))
            return KnownType.Image;
        else if (mediaType == "application/zip")
            return KnownType.Zip;
        else if (mediaType is "application/json" or "application/xml" || mediaType.StartsWith("text/"))
            return KnownType.Text;
        else if (mediaType.StartsWith("font/"))
            return KnownType.Font;
        else
            return KnownType.Other;
    }

    public StorageDetailPageViewModel? CreateDetailPage(KnownType type)
    {
        return _user.Value == null ? null : new StorageDetailPageViewModel(_user.Value, type);
    }

    // https://teratail.com/questions/136799#reply-207332
    private static string ToHumanReadableSize(double size, int scale = 0, int standard = 1024)
    {
        string[] unit = new[] { "B", "KB", "MB", "GB" };
        if (scale == unit.Length - 1 || size <= standard) { return $"{size:F} {unit[scale]}"; }
        return ToHumanReadableSize(size / standard, scale + 1, standard);
    }

    private void FillBlankItems()
    {
        Details.Clear();
        Details.Add(DetailItem.Image);
        Details.Add(DetailItem.Zip);
        Details.Add(DetailItem.Text);
        Details.Add(DetailItem.Font);
        Details.Add(DetailItem.Other);
    }

    public record DetailItem(KnownType Type, string DisplayUsing, long Size, double Percent)
    {
        public static readonly DetailItem Image = new(KnownType.Image, "0.00 B", 0, 0);
        public static readonly DetailItem Zip = new(KnownType.Zip, "0.00 B", 0, 0);
        public static readonly DetailItem Text = new(KnownType.Text, "0.00 B", 0, 0);
        public static readonly DetailItem Font = new(KnownType.Font, "0.00 B", 0, 0);
        public static readonly DetailItem Other = new(KnownType.Other, "0.00 B", 0, 0);

        public Symbol IconSymbol => Type switch
        {
            KnownType.Image => Symbol.Image,
            KnownType.Zip => Symbol.FolderZip,
            KnownType.Text => Symbol.Document,
            KnownType.Font => Symbol.TextFont,
            KnownType.Other => Symbol.Folder,
            _ => Symbol.Folder,
        };

        public string DisplayName => Type.ToString();
    }

    public enum KnownType
    {
        // image/*
        Image,

        // application/zip
        Zip,

        // application/json
        // application/xml
        // text/*
        Text,

        // font/*
        Font,
        Other
    }
}
