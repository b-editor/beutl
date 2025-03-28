using System.Collections;
using Avalonia.Collections;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using OpenTelemetry.Trace;
using Reactive.Bindings;
using LibraryService = Beutl.Api.Services.LibraryService;

namespace Beutl.ViewModels.ExtensionsPages;

public sealed class LibraryPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<LibraryPageViewModel>();
    private readonly AuthorizedUser? _user;
    private readonly BeutlApiApplication _clients;
    private readonly CompositeDisposable _disposables = [];
    private readonly LibraryService _service;

    public LibraryPageViewModel(AuthorizedUser? user, BeutlApiApplication clients)
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

                        if (_user != null)
                        {
                            await _user.RefreshAsync();
                        }

                        await RefreshPackages(_user != null);
                        activity?.AddEvent(new("Refreshed_Packages"));
                    }

                    await task;
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    await e.Handle();
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Refresh.Execute();

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

                        if (_user != null)
                        {
                            await _user.RefreshAsync();
                        }

                        PackageManager manager = _clients.GetResource<PackageManager>();
                        foreach (PackageUpdate item in await manager.CheckUpdate())
                        {
                            LocalUserPackageViewModel? localPackage = LocalPackages.FirstOrDefault(
                                x => x.Package.Name.Equals(item.Package.Name, StringComparison.OrdinalIgnoreCase));

                            if (localPackage != null)
                            {
                                localPackage.LatestRelease.Value = item.NewVersion;
                            }

                            RemoteUserPackageViewModel? remotePackage = Packages.OfType<RemoteUserPackageViewModel>()
                                .FirstOrDefault(
                                    x => x?.Package?.Name?.Equals(item.Package.Name,
                                        StringComparison.OrdinalIgnoreCase) == true);

                            if (remotePackage != null)
                                Packages.Remove(remotePackage);
                            remotePackage ??= new RemoteUserPackageViewModel(item.Package, _clients)
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
                    await e.Handle();
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

    public AvaloniaList<LocalUserPackageViewModel> LocalPackages { get; } = [];

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

    private void OnPackageRemoveFromLibrary(RemoteUserPackageViewModel obj)
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
            LocalPackages.Add(new LocalUserPackageViewModel(item.Value, _clients));
        }
    }

    private async Task RefreshPackages(bool auth)
    {
        PackageManager manager = _clients.GetResource<PackageManager>();
        DiscoverService discover = _clients.GetResource<DiscoverService>();
        List<Package> own = auth ? await LoadAll() : [];
        Packages.Clear();

        foreach (var item in await manager.GetPackages())
        {
            var remote = own.FirstOrDefault(i => i.Name == item.Name);
            if (remote == null)
            {
                try
                {
                    remote = await discover.GetPackage(item.Name);
                }
                catch
                {
                    Packages.Add(new LocalUserPackageViewModel(item, _clients));
                }
            }

            if (remote != null)
            {
                Packages.Add(new RemoteUserPackageViewModel(remote, _clients)
                {
                    OnRemoveFromLibrary = OnPackageRemoveFromLibrary
                });
            }
        }
    }

    private async Task<List<Package>> LoadAll()
    {
        var list = new List<Package>();
        Package[] array = await _service.GetPackages(0, 30);
        list.AddRange(array);
        while (array.Length == 30)
        {
            array = await _service.GetPackages(list.Count, 30);
            list.AddRange(array);
        }

        return list;
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
