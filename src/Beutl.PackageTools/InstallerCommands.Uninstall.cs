namespace Beutl.PackageTools;

public partial class InstallerCommands
{
    [Command("uninstall")]
    public async Task Uninstall(
        [Option("r", "アンインストールするパッケージ")] string[] uninstalls,
        [Option("v", "詳細を表示")] bool verbose = false,
        [Option("c", "不要になったパッケージを削除するかどうか")] bool clean = true)
    {
        try
        {
            CancellationToken cancellationToken = Context.CancellationToken;

            var uninstallItems = new HashSet<(PackageIdentity, Release?)>();
            await LoadArgs(uninstallItems, uninstalls, false, cancellationToken);

            Show(null, null, uninstallItems);

            Console.WriteLine("上記の操作を実行します。");
            if (!Prompt.Confirm("よろしいですか？", defaultValue: true))
            {
                return;
            }
            else
            {
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

    [Command("clean")]
    public void Clean()
    {
        try
        {
            CleanPackages();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("操作をキャンセルしました。");
        }

        Context.Terminate();
    }

    private void UninstallPackages(HashSet<(PackageIdentity, Release?)> items, bool verbose, bool clean)
    {
        var cancellationToken = Context.CancellationToken;

        foreach ((PackageIdentity package, Release? release) in items)
        {
            Console.WriteLine();
            try
            {
                Console.WriteLine($"'{package}'を削除します。");

                string installed = Helper.PackagePathResolver.GetInstalledPath(package);
                if (installed == null)
                {
                    _installedPackageRepository.RemovePackage(package);
                    Console.WriteLine("このパッケージは既に削除されています。");
                }
                else
                {
                    PackageUninstallContext context = _installer.PrepareForUninstall(installed, clean, cancellationToken);

                    string message = "ファイルを削除しています...";
                    Spinner.Start(message, spinner =>
                    {
                        var progress = new KurukuruProgress(spinner, message);
                        _installer.Uninstall(context, progress, cancellationToken);
                    });

                    Console.WriteLine($"'{package}'をアンインストールしました。");
                    if (context.FailedPackages?.Count > 0)
                    {
                        foreach (var item in context.FailedPackages)
                        {
                            Console.Error.WriteLine(Chalk.Red[Path.GetFileName(item)]);
                        }

                        Console.WriteLine("これらのパッケージは削除されませんでした。");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine(Chalk.Red[$"'{package}'の削除に失敗しました。"]);
                if (verbose)
                {
                    Console.Error.WriteLine(ex);
                }
            }
        }
    }

    private void CleanPackages()
    {
        var cancellationToken = Context.CancellationToken;
        Console.WriteLine("\n不要なパッケージを削除します。");

        PackageCleanContext context = _installer.PrepareForClean(null, cancellationToken);
        IEnumerable<PackageIdentity> remove = Prompt.MultiSelect(
            "削除するパッケージを選択してください。",
            context.UnnecessaryPackages,
            defaultValues: context.UnnecessaryPackages,
            textSelector: x => x.ToString());
        context = _installer.PrepareForClean(context.UnnecessaryPackages.Except(remove, PackageIdentityComparer.Default), cancellationToken);
        Console.WriteLine($"{context.UnnecessaryPackages.Length}個のパッケージが削除され、");
        Console.WriteLine($"{StringFormats.ToHumanReadableSize(context.SizeToBeReleased)}が解放されます。");
        if (Prompt.Confirm("この操作を実行しますか？", true))
        {
            string message = "不要なパッケージを削除しています...";
            Spinner.Start(message, spinner =>
            {
                var progress = new KurukuruProgress(spinner, message);
                _installer.Clean(context, progress, cancellationToken);
            });

            Console.WriteLine($"不要なパッケージを削除しました。");
            if (context.FailedPackages?.Count > 0)
            {
                foreach (var item in context.FailedPackages)
                {
                    Console.Error.WriteLine(Chalk.Red[Path.GetFileName(item)]);
                }

                Console.WriteLine("これらのパッケージは削除されませんでした。");
            }
        }
    }
}
