namespace Beutl.PackageTools;

public partial class InstallerCommands
{
    [Command("install")]
    public async Task Install(
        [Option("i", "インストールするパッケージ")] string[] installs,
        [Option("v", "詳細を表示")] bool verbose = false,
        [Option("c", "不要になったパッケージを削除するかどうか")] bool clean = true)
    {
        try
        {
            CancellationToken cancellationToken = Context.CancellationToken;

            var installItems = new HashSet<(PackageIdentity, Release?)>();
            await LoadArgs(installItems, installs, true, cancellationToken);

            Show(installItems, null, null);

            Console.WriteLine("上記の操作を実行します。");
            if (!Prompt.Confirm("よろしいですか？", defaultValue: true))
            {
                return;
            }
            else
            {
                await InstallPackages(installItems, verbose);
                if (clean)
                {
                    CleanPackages();
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("操作をキャンセルしました。");
        }

        Context.Terminate();
    }

    [Command("update")]
    public async Task Update(
        [Option("u", "更新するパッケージ")] string[] updates,
        [Option("v", "詳細を表示")] bool verbose = false,
        [Option("c", "不要になったパッケージを削除するかどうか")] bool clean = true)
    {
        try
        {
            CancellationToken cancellationToken = Context.CancellationToken;

            var updateItems = new HashSet<(PackageIdentity, Release?)>();
            await LoadArgs(updateItems, updates, true, cancellationToken);

            Show(null, updateItems, null);

            Console.WriteLine("上記の操作を実行します。");
            if (!Prompt.Confirm("よろしいですか？", defaultValue: true))
            {
                return;
            }
            else
            {
                await UpdatePackages(updateItems, verbose);
                if (clean)
                {
                    CleanPackages();
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("操作をキャンセルしました。");
        }

        Context.Terminate();
    }

    [Command("perform")]
    public async Task Perform(
        [Option("i", "インストールするパッケージ")] string[]? installs = null,
        [Option("r", "アンインストールするパッケージ")] string[]? uninstalls = null,
        [Option("u", "更新するパッケージ")] string[]? updates = null,
        [Option("v", "詳細を表示")] bool verbose = false,
        [Option("c", "不要になったパッケージを削除するかどうか")] bool clean = true)
    {
        try
        {
            CancellationToken cancellationToken = Context.CancellationToken;

            installs ??= Array.Empty<string>();
            uninstalls ??= Array.Empty<string>();
            updates ??= Array.Empty<string>();

            var installItems = new HashSet<(PackageIdentity, Release?)>();
            var uninstallItems = new HashSet<(PackageIdentity, Release?)>();
            var updateItems = new HashSet<(PackageIdentity, Release?)>();
            await LoadArgs(installItems, installs, true, cancellationToken);
            await LoadArgs(uninstallItems, uninstalls, false, cancellationToken);
            await LoadArgs(updateItems, updates, true, cancellationToken);

            Show(installItems, updateItems, uninstallItems);

            Console.WriteLine("上記の操作を実行します。");
            if (!Prompt.Confirm("よろしいですか？", defaultValue: true))
            {
                return;
            }
            else
            {
                await InstallPackages(installItems, verbose);
                await UpdatePackages(updateItems, verbose);
                UninstallPackages(uninstallItems, verbose, clean);
                if (clean)
                {
                    CleanPackages();
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("操作をキャンセルしました。");
        }

        Context.Terminate();
    }

    private async Task InstallPackages(HashSet<(PackageIdentity, Release?)> items, bool verbose)
    {
        CancellationToken cancellationToken = Context.CancellationToken;

        foreach ((PackageIdentity package, Release? release) in items)
        {
            Console.WriteLine();
            try
            {
                Console.WriteLine($"'{package}'をインストールします。");
                PackageInstallContext? context;
                if (release != null)
                {
                    context = await _installer.PrepareForInstall(release, true, cancellationToken);

                    string message = "パッケージをダウンロードしています...";
                    await Spinner.StartAsync(message, async spinner =>
                    {
                        var progress = new KurukuruProgress(spinner, message);
                        await _installer.DownloadPackageFile(context, null, cancellationToken);
                    });
                }
                else
                {
                    context = _installer.PrepareForInstall(package.Id, package.Version.ToString(), true, cancellationToken);
                    Console.WriteLine("パッケージのダウンロードが省略されました。");
                }

                await Spinner.StartAsync("依存関係を解決しています...", async spinner =>
                {
                    await _installer.ResolveDependencies(context, verbose ? ConsoleLogger.Instance : NullLogger.Instance, cancellationToken);
                });

                Console.WriteLine($"'{package}'をインストールしました。");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"'{package}'のインストールに失敗しました。");
                if (verbose)
                {
                    Console.Error.WriteLine(ex);
                }
            }
        }
    }

    private async Task UpdatePackages(HashSet<(PackageIdentity, Release?)> items, bool verbose)
    {
        CancellationToken cancellationToken = Context.CancellationToken;

        foreach ((PackageIdentity package, Release? release) in items)
        {
            Console.WriteLine();
            try
            {
                Console.WriteLine($"'{package.Id}'を更新します。");

                _installedPackageRepository.RemovePackages(package.Id);

                PackageInstallContext? context;
                if (release != null)
                {
                    context = await _installer.PrepareForInstall(release, true, cancellationToken);

                    string message = "パッケージをダウンロードしています...";
                    await Spinner.StartAsync(message, async spinner =>
                    {
                        var progress = new KurukuruProgress(spinner, message);
                        await _installer.DownloadPackageFile(context, progress, cancellationToken);
                    });
                }
                else
                {
                    context = _installer.PrepareForInstall(package.Id, package.Version.ToString(), true, cancellationToken);
                    Console.WriteLine("パッケージのダウンロードが省略されました。");
                }

                await Spinner.StartAsync("依存関係を解決しています...", async spinner =>
                {
                    await _installer.ResolveDependencies(context, verbose ? ConsoleLogger.Instance : NullLogger.Instance, cancellationToken);
                });

                Console.WriteLine($"'{package.Id}'を更新しました。");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"'{package.Id}'のインストールに失敗しました。");
                if (verbose)
                {
                    Console.Error.WriteLine(ex);
                }
            }
        }
    }
}
