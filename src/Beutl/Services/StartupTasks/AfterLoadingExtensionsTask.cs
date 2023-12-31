using System.CodeDom.Compiler;

using Beutl.Api.Services;

namespace Beutl.Services.StartupTasks;

// エラーの表示、
public sealed class AfterLoadingExtensionsTask : StartupTask
{
    private readonly Startup _startup;

    public AfterLoadingExtensionsTask(Startup startup)
    {
        _startup = startup;
        Task = Task.Run(async () =>
        {
            LoadInstalledExtensionTask t1 = _startup.GetTask<LoadInstalledExtensionTask>();
            LoadPrimitiveExtensionTask t2 = _startup.GetTask<LoadPrimitiveExtensionTask>();
            LoadSideloadExtensionTask t3 = _startup.GetTask<LoadSideloadExtensionTask>();
            await Task.WhenAll(t1.Task, t2.Task, t3.Task);

            (LocalPackage, Exception)[] failures = [.. t1.Failures, .. t2.Failures, .. t3.Failures];
            if (failures.Length > 0)
            {
                NotificationService.ShowError(
                    Message.Failed_to_load_package,
                    string.Format(Message.Failed_to_load_N_packages, failures.Length),
                    onActionButtonClick: () => ShowPackageLoadingError(failures),
                    actionButtonText: Strings.Details);
            }
        });
    }

    public override Task Task { get; }

    // ユーザー向けのテキストファイルを生成して、デフォルトのテキストエディタで表示する。
    private static async void ShowPackageLoadingError(IReadOnlyList<(LocalPackage, Exception)> failures)
    {
        string file = Path.GetTempFileName();
        file = Path.ChangeExtension(file, ".txt");

        using (StreamWriter baseWriter = File.CreateText(file))
        using (var writer = new IndentedTextWriter(baseWriter, "  "))
        {
            baseWriter.AutoFlush = false;
            writer.WriteLine(string.Format(Message.Failed_to_load_N_packages, failures.Count));
            writer.WriteLine();
            foreach ((LocalPackage pkg, Exception ex) in failures)
            {
                writer.WriteLine("Package:");
                writer.Indent++;
                writer.WriteLine($"Name: '{pkg.Name}'");
                writer.WriteLine($"DisplayName: '{pkg.DisplayName}'");
                writer.WriteLine($"Version: '{pkg.Version}'");
                writer.WriteLine($"Publisher: '{pkg.Publisher}'");
                writer.WriteLine($"WebSite: '{pkg.WebSite}'");
                writer.WriteLine($"Description: '{pkg.Description}");
                writer.WriteLine($"ShortDescription: '{pkg.ShortDescription}'");
                writer.WriteLine($"Tags: '{string.Join(',', pkg.Tags)}'");
                writer.WriteLine($"InstalledPath: '{pkg.InstalledPath}'");
                writer.Indent--;
                writer.WriteLine(ex.ToString());
                writer.WriteLine();
            }

            await writer.FlushAsync();
        }

        Process.Start(new ProcessStartInfo(file)
        {
            UseShellExecute = true,
            Verb = "open"
        });
    }
}
