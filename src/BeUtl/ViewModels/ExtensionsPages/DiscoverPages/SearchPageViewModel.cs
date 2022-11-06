using Avalonia.Collections;

using Beutl.Api.Objects;
using Beutl.Api.Services;
using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

public sealed class SearchPageViewModel : BasePageViewModel
{
    private readonly CompositeDisposable _disposables = new();
    private readonly DiscoverService _discoverService;

    public SearchPageViewModel(DiscoverService discoverService, string keyword)
    {
        _discoverService = discoverService;
        Keyword = keyword;

        Refresh = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                try
                {
                    IsBusy.Value = true;
                    if (SearchType.Value == 0)
                        await RefreshPackages();
                    else
                        await RefreshUsers();
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

        More = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                try
                {
                    IsBusy.Value = true;
                    if (SearchType.Value == 0)
                        await MoreLoadPackages();
                    else
                        await MoreLoadUsers();
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

        SearchType.Subscribe(async type =>
            {
                try
                {
                    IsBusy.Value = true;
                    if (type == 0 && Packages.Count == 0)
                    {
                        await RefreshPackages();
                    }
                    else if (Users.Count == 0)
                    {
                        await RefreshUsers();
                    }
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

    public string Keyword { get; }

    public AvaloniaList<Package?> Packages { get; } = new();

    public AvaloniaList<Profile?> Users { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand More { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public ReactivePropertySlim<int> SearchType { get; } = new();

    public override void Dispose()
    {
        _disposables.Dispose();
    }

    private async Task<Package[]> SearchPackages(int start, int count)
    {
        return await _discoverService.SearchPackages(Keyword, start, count);
    }

    private async Task RefreshPackages()
    {
        Packages.Clear();
        Package[] array = await SearchPackages(0, 30);
        Packages.AddRange(array);

        if (array.Length == 30)
        {
            Packages.Add(null);
        }
    }

    private async Task MoreLoadPackages()
    {
        Packages.RemoveAt(Packages.Count - 1);
        Package[] array = await SearchPackages(Packages.Count, 30);
        Packages.AddRange(array);

        if (array.Length == 30)
        {
            Packages.Add(null);
        }
    }

    private async Task<Profile[]> SearchUsers(int start, int count)
    {
        return await _discoverService.SearchUsers(Keyword, start, count);
    }

    private async Task RefreshUsers()
    {
        Users.Clear();
        Profile[] array = await SearchUsers(0, 30);
        Users.AddRange(array);

        if (array.Length == 30)
        {
            Users.Add(null);
        }
    }

    private async Task MoreLoadUsers()
    {
        Users.RemoveAt(Users.Count - 1);
        Profile[] array = await SearchUsers(Users.Count, 30);
        Users.AddRange(array);

        if (array.Length == 30)
        {
            Users.Add(null);
        }
    }
}
