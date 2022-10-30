using System.Collections.ObjectModel;

using Avalonia.Collections;
using Avalonia.Media;

using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;

using BeUtl.Framework.Service;

using DynamicData;

using Microsoft.Extensions.DependencyInjection;

using NuGet.Packaging.Core;
using NuGet.Versioning;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages;

public sealed class LibraryPageViewModel : BasePageViewModel
{
    private readonly AuthorizedUser _user;
    private readonly BeutlApiApplication _clients;
    private readonly CompositeDisposable _disposables = new();
    private readonly LibraryService _service;

    public LibraryPageViewModel(AuthorizedUser user, BeutlApiApplication clients)
    {
        _user = user;
        _clients = clients;
        _service = new LibraryService(clients);

        Refresh = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                try
                {
                    IsBusy.Value = true;
                    await _user.RefreshAsync();
                    await RefreshPackages();
                    await RefreshLocalPackages();
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
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
                try
                {
                    IsBusy.Value = true;
                    await _user.RefreshAsync();
                    await MoreLoadPackages();
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        CheckUpdate = new AsyncReactiveCommand(IsBusy.AnyTrue(CheckingForUpdate).Not())
            .WithSubscribe(async () =>
            {
                try
                {
                    IsBusy.Value = true;
                    CheckingForUpdate.Value = true;
                    await _user.RefreshAsync();

                    PackageManager manager = _clients.GetResource<PackageManager>();
                    //UpgradablePackages.Clear();
                    //UpgradablePackages.AddRange(await manager.CheckUpdate());
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
                }
                finally
                {
                    IsBusy.Value = false;
                    CheckingForUpdate.Value = false;
                }
            })
            .DisposeWith(_disposables);
    }

    public AvaloniaList<RemoteYourPackageViewModel?> UpgradablePackages { get; } = new();

    public AvaloniaList<RemoteYourPackageViewModel?> Packages { get; } = new();

    public AvaloniaList<LocalYourPackageViewModel> LocalPackages { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand CheckUpdate { get; }

    public AsyncReactiveCommand More { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public ReactivePropertySlim<bool> CheckingForUpdate { get; } = new();

    public override void Dispose()
    {
        _disposables.Dispose();
    }

    public async Task<Package?> TryFindPackage(LocalPackage localPackage)
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

    private async Task RefreshLocalPackages()
    {
        PackageManager manager = _clients.GetResource<PackageManager>();
        LocalPackages.Clear();

        var dict = new Dictionary<PackageIdentity, LocalPackage>();
        foreach (LocalPackage item in manager.GetLocalSourcePackages())
        {
            dict.TryAdd(new PackageIdentity(item.Name, new NuGetVersion(item.Version)), item);
        }

        foreach (LocalPackage item in await manager.GetPackages())
        {
            dict.TryAdd(new PackageIdentity(item.Name, new NuGetVersion(item.Version)), item);
        }

        foreach (KeyValuePair<PackageIdentity, LocalPackage> item in dict)
        {
            LocalPackages.Add(new LocalYourPackageViewModel(item.Value, _clients));
        }
    }

    private async Task RefreshPackages()
    {
        DisposeAll(Packages);
        Packages.Clear();
        Package[] array = await _service.GetPackages(0, 30);

        foreach (Package item in array)
        {
            Packages.Add(new RemoteYourPackageViewModel(item, _clients));
        }

        if (array.Length == 30)
        {
            Packages.Add(null);
        }
    }

    private async Task MoreLoadPackages()
    {
        Packages.RemoveAt(Packages.Count - 1);
        Package[] array = await _service.GetPackages(Packages.Count, 30);

        foreach (Package item in array)
        {
            Packages.Add(new RemoteYourPackageViewModel(item, _clients));
        }

        if (array.Length == 30)
        {
            Packages.Add(null);
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
