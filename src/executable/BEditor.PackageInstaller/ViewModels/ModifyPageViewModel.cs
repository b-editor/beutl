using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BEditor.PackageInstaller.Models;
using BEditor.PackageInstaller.Resources;
using BEditor.Packaging;

using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

using Reactive.Bindings;

namespace BEditor.PackageInstaller.ViewModels
{
    public class ModifyPageViewModel
    {
        private readonly Queue<PackageChange> _packages;
        private readonly CancellationTokenSource _cancelToken = new();
        private readonly List<PackageChange> _failedChanges = new();
        private readonly List<PackageChange> _successfulChanges = new();

        public ModifyPageViewModel(IEnumerable<PackageChange> changes)
        {
            _packages = new(changes);
            Cancel.Subscribe(() =>
            {
                _cancelToken.Cancel();
                CompleteModify.Execute((_failedChanges, _successfulChanges));
            });

            Task.Run(async () =>
            {
                using var client = new WebClient();
                var progress = new ProgressImpl(this);
                client.DownloadProgressChanged += Client_DownloadProgressChanged;

                while (_packages.TryDequeue(out var package))
                {
                    CurrentPackage.Value = package;
                    var directory = Path.Combine(GetBaseDirectory(), "plugins", Path.GetFileNameWithoutExtension(package.MainAssembly));
                    if (package.Type is not PackageChangeType.Uninstall)
                    {
                        var downloadFile = Path.GetTempFileName();

                        try
                        {
                            if (Directory.Exists(directory))
                            {
                                Directory.Delete(directory);
                            }

                            Status.Value = Strings.Downloading;
                            await client.DownloadFileTaskAsync(package.Url!, downloadFile);

                            Status.Value = Strings.ExtractingFiles;
                            await PackageFile.OpenPackageAsync(downloadFile, directory, progress);

                            var afterInstall = Path.Combine(directory, "AFTER_INSTALL");
                            if (File.Exists(afterInstall))
                            {
                                var code = await File.ReadAllTextAsync(afterInstall);
                                await CSharpScript.RunAsync(code, ScriptOptions.Default.WithFilePath(afterInstall));
                            }

                            _successfulChanges.Add(package);
                        }
                        catch
                        {
                            _failedChanges.Add(package);
                        }
                        finally
                        {
                            if (File.Exists(downloadFile)) File.Delete(downloadFile);
                        }
                    }
                    else if (package.Type is PackageChangeType.Uninstall)
                    {
                        try
                        {
                            var beforUninstall = Path.Combine(directory, "BEFOR_UNINSTALL");
                            if (File.Exists(beforUninstall))
                            {
                                var code = await File.ReadAllTextAsync(beforUninstall);
                                await CSharpScript.RunAsync(code, ScriptOptions.Default.WithFilePath(beforUninstall));
                            }

                            if (Directory.Exists(directory))
                            {
                                Directory.Delete(directory, true);
                            }

                            _successfulChanges.Add(package);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                            _failedChanges.Add(package);
                        }
                    }
                }

                CompleteModify.Execute((_failedChanges, _successfulChanges));
            }, _cancelToken.Token);
        }

        private void Client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            Max.Value = e.TotalBytesToReceive;
            Progress.Value = e.BytesReceived;
        }

        public ReactiveCommand<(IEnumerable<PackageChange>, IEnumerable<PackageChange>)> CompleteModify { get; } = new();

        public ReactivePropertySlim<PackageChange> CurrentPackage { get; } = new();

        public ReactivePropertySlim<string> Status { get; } = new();

        public ReactivePropertySlim<double> Max { get; } = new();

        public ReactivePropertySlim<double> Min { get; } = new();

        public ReactivePropertySlim<double> Progress { get; } = new();

        public ReactivePropertySlim<bool> IsIndeterminate { get; } = new();

        public ReactiveCommand Cancel { get; } = new();

        public static string GetBaseDirectory()
        {
            if (OperatingSystem.IsWindows())
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BEditor");
                return dir;
            }
            else
            {
                return Path.Combine(AppContext.BaseDirectory, "user");
            }
        }

        private class ProgressImpl : IProgress<int>
        {
            private readonly ModifyPageViewModel _viewModel;

            public ProgressImpl(ModifyPageViewModel viewModel)
            {
                _viewModel = viewModel;
            }

            public void Report(int value)
            {
                var per = value / 100f;
                _viewModel.Progress.Value = _viewModel.Max.Value * per;
            }
        }
    }
}