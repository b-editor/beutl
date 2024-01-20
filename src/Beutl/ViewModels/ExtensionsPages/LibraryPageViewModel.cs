using System.Collections;

using Avalonia.Collections;

using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

using NuGet.Versioning;

using OpenTelemetry.Trace;

using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages;

public sealed class LibraryPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<LibraryPageViewModel>();
    private readonly AuthorizedUser _user;
    private readonly BeutlApiApplication _clients;
    private readonly CompositeDisposable _disposables = [];
    private readonly LibraryService _service;

    public LibraryPageViewModel(AuthorizedUser user, BeutlApiApplication clients)
    {
        _user = user;
        _clients = clients;
        _service = new LibraryService(clients);

        Refresh = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Services.Telemetry.StartActivity("LibraryPage.Refresh");

                try
                {
                    IsBusy.Value = true;
                    Task task = RefreshLocalPackages();
                    DisposeAll(Packages.OfType<IDisposable>());
                    Packages.Clear();
                    Packages.AddRange(Enumerable.Repeat(new DummyItem(), 6));

                    using (await _clients.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));

                        await _user.RefreshAsync();

                        await RefreshPackages();
                        activity?.AddEvent(new("Refreshed_Packages"));
                    }
                    await task;
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    ErrorHandle(e);
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Refresh.Execute();

        More = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Services.Telemetry.StartActivity("LibraryPage.More");

                try
                {
                    IsBusy.Value = true;
                    using (await _clients.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));

                        await _user.RefreshAsync();

                        await MoreLoadPackages();
                        activity?.AddEvent(new("Done_MoreLoadPackages"));
                    }
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    ErrorHandle(e);
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        CheckUpdate = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Services.Telemetry.StartActivity("LibraryPage.CheckUpdate");

                try
                {
                    IsBusy.Value = true;
                    using (await _clients.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));

                        await _user.RefreshAsync();

                        PackageManager manager = _clients.GetResource<PackageManager>();
                        foreach (PackageUpdate item in await manager.CheckUpdate())
                        {
                            LocalYourPackageViewModel? localPackage = LocalPackages.FirstOrDefault(
                                x => x.Package.Name.Equals(item.Package.Name, StringComparison.OrdinalIgnoreCase));

                            if (localPackage != null)
                            {
                                localPackage.LatestRelease.Value = item.NewVersion;
                            }

                            RemoteYourPackageViewModel? remotePackage = Packages.OfType<RemoteYourPackageViewModel>()
                                .FirstOrDefault(
                                    x => x?.Package?.Name?.Equals(item.Package.Name, StringComparison.OrdinalIgnoreCase) == true);

                            if (remotePackage != null)
                                Packages.Remove(remotePackage);
                            remotePackage ??= new RemoteYourPackageViewModel(item.Package, _clients)
                            {
                                OnRemoveFromLibrary = OnPackageRemoveFromLibrary
                            };

                            remotePackage.LatestRelease.Value = item.NewVersion;

                            Packages.Insert(0, remotePackage);
                        }
                    }
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    ErrorHandle(e);
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);
    }

    public AvaloniaList<object> Packages { get; } = [];

    public AvaloniaList<LocalYourPackageViewModel> LocalPackages { get; } = [];

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand CheckUpdate { get; }

    public AsyncReactiveCommand More { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public override void Dispose()
    {
        _disposables.Dispose();
    }

    public async Task<Package?> TryFindPackage(LocalPackage localPackage)
    {
        using (await _clients.Lock.LockAsync())
        {
            DiscoverService discover = _clients.GetResource<DiscoverService>();
            try
            {
                return await discover.GetPackage(localPackage.Name);
            }
            catch
            {
                return null;
            }
        }
    }

    private void OnPackageRemoveFromLibrary(RemoteYourPackageViewModel obj)
    {
        Packages.Remove(obj);
        obj.Dispose();
    }

    private async Task RefreshLocalPackages()
    {
        PackageManager manager = _clients.GetResource<PackageManager>();
        DisposeAll(LocalPackages);
        LocalPackages.Clear();

        var dict = new Dictionary<string, LocalPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (LocalPackage item in manager.GetLocalSourcePackages()
            .Concat(await manager.GetPackages()))
        {
            if (dict.TryGetValue(item.Name, out LocalPackage? localPackage))
            {
                // itemのほうが新しいバージョン
                if (NuGetVersion.Parse(item.Version).CompareTo(NuGetVersion.Parse(localPackage.Version)) >= 0)
                {
                    dict[item.Name] = item;
                }
            }
            else
            {
                dict.Add(item.Name, item);
            }
        }

        foreach (KeyValuePair<string, LocalPackage> item in dict)
        {
            LocalPackages.Add(new LocalYourPackageViewModel(item.Value, _clients));
        }
    }

    private async Task RefreshPackages()
    {
        Package[] array = await _service.GetPackages(0, 30);
        Packages.Clear();

        foreach (Package item in array)
        {
            Packages.Add(new RemoteYourPackageViewModel(item, _clients)
            {
                OnRemoveFromLibrary = OnPackageRemoveFromLibrary
            });
        }

        if (array.Length == 30)
        {
            Packages.Add(new LoadMoreItem());
        }
    }

    private async Task MoreLoadPackages()
    {
        Packages.RemoveAt(Packages.Count - 1);
        Package[] array = await _service.GetPackages(Packages.Count, 30);

        foreach (Package item in array)
        {
            Packages.Add(new RemoteYourPackageViewModel(item, _clients)
            {
                OnRemoveFromLibrary = OnPackageRemoveFromLibrary
            });
        }

        if (array.Length == 30)
        {
            Packages.Add(new LoadMoreItem());
        }
    }

    private static void DisposeAll<T>(IEnumerable<T?> items)
        where T : IDisposable
    {
        foreach (T? item in items)
        {
            item?.Dispose();
        }
    }
}
