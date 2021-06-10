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
                    if (package.Type is not PackageChangeType.Uninstall)
                    {
                        var downloadFile = Path.GetTempFileName();

                        try
                        {
                            var dstDir = Path.Combine(AppContext.BaseDirectory, "user", "plugins", Path.GetFileNameWithoutExtension(package.MainAssembly));
                            if (Directory.Exists(dstDir))
                            {
                                Directory.Delete(dstDir);
                            }

                            Status.Value = Strings.Downloading;
                            await client.DownloadFileTaskAsync(package.Url!, downloadFile);

                            Status.Value = Strings.ExtractingFiles;
                            await PackageFile.OpenPackageAsync(downloadFile, dstDir, progress);
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
                            var directory = Path.Combine(AppContext.BaseDirectory, "user", "plugins", Path.GetFileNameWithoutExtension(package.MainAssembly));
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