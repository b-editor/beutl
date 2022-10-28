using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Collections;

using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages;

public sealed class LibraryPageViewModel : BasePageViewModel
{
    private readonly AuthorizedUser _user;
    private readonly BeutlClients _clients;
    private readonly CompositeDisposable _disposables = new();
    private readonly LibraryService _service;

    public LibraryPageViewModel(AuthorizedUser user, BeutlClients clients)
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
    }

    public AvaloniaList<Package?> UpgradablePackages { get; } = new();

    public AvaloniaList<Package?> Packages { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand CheckUpdate { get; }

    public AsyncReactiveCommand More { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public ReactivePropertySlim<bool> CheckingForUpdate { get; } = new();


    public override void Dispose()
    {
        _disposables.Dispose();
    }

    private async Task RefreshPackages()
    {
        Packages.Clear();
        Package[] array = await _service.GetPackages(0, 30);
        Packages.AddRange(array);

        if (array.Length == 30)
        {
            Packages.Add(null);
        }
    }

    private async Task MoreLoadPackages()
    {
        Packages.RemoveAt(Packages.Count - 1);
        Package[] array = await _service.GetPackages(Packages.Count, 30);
        Packages.AddRange(array);

        if (array.Length == 30)
        {
            Packages.Add(null);
        }
    }
}
