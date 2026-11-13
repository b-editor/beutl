using Avalonia.Collections;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using LibraryService = Beutl.Api.Services.LibraryService;

namespace Beutl.ViewModels.ExtensionsPages;

public sealed class LibraryPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<LibraryPageViewModel>();
    private readonly AuthenticatedUser? _user;
    private readonly BeutlApiApplication _clients;
    private readonly CompositeDisposable _disposables = [];
    private readonly LifetimeCancellationSource _lifetimeCts = new();
    private readonly LibraryService _service;
    private readonly EditorService _editorService;
    private readonly ProjectService _projectService;
    private readonly Task _initialRefresh;

    public LibraryPageViewModel(AuthenticatedUser? user, BeutlApiApplication clients, EditorService editorService, ProjectService projectService)
    {
        _user = user;
        _clients = clients;
        _service = new LibraryService(clients);
        _editorService = editorService;
        _projectService = projectService;

        Refresh = new AsyncReactiveCommand(IsBusy.Inverse())
            .WithSubscribe(() => StorePageCommand.RunAsync(
                IsBusy, "LibraryPage.Refresh", _lifetimeCts.Token, _logger,
                async activity =>
                {
                    Task task = RefreshLocalPackages(_lifetimeCts.Token);
                    DisposeAll(Packages.OfType<IDisposable>());
                    Packages.Clear();
                    Packages.AddRange(Enumerable.Repeat(new DummyItem(), 6));

                    using (await _clients.Lock.LockAsync(_lifetimeCts.Token))
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));

                        if (_user != null)
                        {
                            await _user.RefreshAsync(_lifetimeCts.Token);
                        }

                        await RefreshPackages(_user != null, _lifetimeCts.Token);
                        activity?.AddEvent(new("Refreshed_Packages"));
                    }

                    await task;
                }))
            .DisposeWith(_disposables);

        _initialRefresh = Refresh.ExecuteAsync();

        CheckUpdate = new AsyncReactiveCommand(IsBusy.Inverse())
            .WithSubscribe(() => StorePageCommand.RunAsync(
                IsBusy, "LibraryPage.CheckUpdate", _lifetimeCts.Token, _logger,
                async activity =>
                {
                    using (await _clients.Lock.LockAsync(_lifetimeCts.Token))
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));

                        if (_user != null)
                        {
                            await _user.RefreshAsync(_lifetimeCts.Token);
                        }

                        PackageManager manager = _clients.GetResource<PackageManager>();
                        IReadOnlyList<PackageUpdate> updates = await manager.CheckUpdate(_lifetimeCts.Token);
                        _lifetimeCts.Token.ThrowIfCancellationRequested();
                        foreach (PackageUpdate item in updates)
                        {
                            _lifetimeCts.Token.ThrowIfCancellationRequested();
                            ApplyPackageUpdate(item);
                        }
                    }
                }))
            .DisposeWith(_disposables);
    }

    public AvaloniaList<object> Packages { get; } = [];

    public AvaloniaList<LocalUserPackageViewModel> LocalPackages { get; } = [];

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand CheckUpdate { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public override void Dispose()
    {
        try
        {
            _lifetimeCts.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cancel the lifetime token during disposal.");
        }
        finally
        {
            _disposables.Dispose();
            _lifetimeCts.Dispose();
        }
    }

    // The refresh that the constructor starts blocks CheckUpdate until it has finished.
    public async Task CheckUpdateAfterRefreshAsync()
    {
        await _initialRefresh;
        await CheckUpdate.ExecuteAsync();
    }

    public async Task<Package?> TryFindPackage(LocalPackage localPackage)
    {
        using (await _clients.Lock.LockAsync(_lifetimeCts.Token))
        {
            DiscoverService discover = _clients.GetResource<DiscoverService>();
            try
            {
                return await discover.GetPackage(localPackage.Name, _lifetimeCts.Token);
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
            {
                throw;
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

    private async Task RefreshLocalPackages(CancellationToken cancellationToken)
    {
        PackageManager manager = _clients.GetResource<PackageManager>();
        DisposeAll(LocalPackages);
        LocalPackages.Clear();

        var dict = new Dictionary<string, LocalPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (LocalPackage item in manager.GetLocalSourcePackages()
                     .Concat(await manager.GetPackages()))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            LocalPackages.Add(new LocalUserPackageViewModel(item.Value, _clients, _editorService, _projectService));
        }
    }

    private async Task RefreshPackages(bool auth, CancellationToken cancellationToken)
    {
        PackageManager manager = _clients.GetResource<PackageManager>();
        DiscoverService discover = _clients.GetResource<DiscoverService>();
        List<Package> own = auth ? await LoadAll(cancellationToken) : [];
        var installed = await manager.GetPackages();
        string[] names = [.. own.Select(i => i.Name).Union(installed.Select(i => i.Name))];

        // Looks up the installed packages missing from the library together rather than one by one.
        Package?[] remotes = await Task.WhenAll(names.Select(name =>
            own.FirstOrDefault(i => i.Name == name) is { } package
                ? Task.FromResult<Package?>(package)
                : TryGetPackage(discover, name, cancellationToken)));

        Packages.Clear();
        for (int i = 0; i < names.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (remotes[i] is { } remote)
            {
                Packages.Add(new RemoteUserPackageViewModel(remote, _clients, _editorService, _projectService)
                {
                    OnRemoveFromLibrary = OnPackageRemoveFromLibrary
                });
            }
            else
            {
                // Not in own, so the name came from installed.
                LocalPackage item = installed.First(p => p.Name == names[i]);
                Packages.Add(new LocalUserPackageViewModel(item, _clients, _editorService, _projectService));
            }
        }
    }

    private static async Task<Package?> TryGetPackage(DiscoverService discover, string name, CancellationToken cancellationToken)
    {
        try
        {
            return await discover.GetPackage(name, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    // Shows the newer release on the installed package, and moves the library entry to the top.
    private void ApplyPackageUpdate(PackageUpdate item)
    {
        LocalUserPackageViewModel? localPackage = LocalPackages.FirstOrDefault(
            x => x.Package.Name.Equals(item.Package.Name, StringComparison.OrdinalIgnoreCase));

        if (localPackage != null)
        {
            localPackage.LatestRelease.Value = item.NewVersion;
        }

        RemoteUserPackageViewModel? remotePackage = Packages.OfType<RemoteUserPackageViewModel>()
            .FirstOrDefault(
                x => x.Package.Name?.Equals(item.Package.Name,
                    StringComparison.OrdinalIgnoreCase) == true);

        if (remotePackage != null)
            Packages.Remove(remotePackage);
        remotePackage ??= new RemoteUserPackageViewModel(item.Package, _clients, _editorService, _projectService)
        {
            OnRemoveFromLibrary = OnPackageRemoveFromLibrary
        };

        remotePackage.LatestRelease.Value = item.NewVersion;

        Packages.Insert(0, remotePackage);
    }

    private async Task<List<Package>> LoadAll(CancellationToken cancellationToken)
    {
        var list = new List<Package>();
        Package[] array = await _service.GetPackages(cancellationToken, 0, PackagePageList.PageSize);
        list.AddRange(array);
        while (array.Length == PackagePageList.PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            array = await _service.GetPackages(cancellationToken, list.Count, PackagePageList.PageSize);
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
