using Beutl.UnitTests.Api;

namespace Beutl.UnitTests;

internal static class TestWorkerProgram
{
    public const string PackageInstallWorkerArgument = "--package-install-worker";
    public const string BitmapSaveWorkerArgument = "--bitmap-save-worker";
    public const string ProjectFontWorkerArgument = "--project-font-worker";
    public const string XAudioLifetimeWorkerArgument = "--xaudio-lifetime-worker";
    public const string FFmpegLifetimeWorkerArgument = "--ffmpeg-lifetime-worker";
    public const string SwiftShaderLifetimeWorkerArgument = "--swiftshader-lifetime-worker";

    private static async Task<int> Main(string[] args)
    {
        try
        {
            switch (args)
            {
                case [PackageInstallWorkerArgument]:
                    PackageInstallerCrashRecoveryTests.RunCrashWorker();
                    break;
                case [BitmapSaveWorkerArgument, var linear]:
                    Engine.BitmapTests.RunSaveFailureWorker(bool.Parse(linear));
                    break;
                case [ProjectFontWorkerArgument, var package, var destination]:
                    await Editor.ProjectPackageFontTests.RunImportWorker(package, destination);
                    break;
                case [XAudioLifetimeWorkerArgument, var action]:
                    Engine.Audio.XAudioLifetimeTests.RunWorker(action);
                    break;
                case [SwiftShaderLifetimeWorkerArgument, var action]:
                    Engine.Graphics.Backend.SwiftShaderLifetimeTests.RunWorker(action);
                    break;
                case [FFmpegLifetimeWorkerArgument, "--test", var testAction]:
                    await Extensions.FFmpeg.FFmpegWorkerProcessLifetimeTests.RunHostAsync(testAction);
                    break;
                case [FFmpegLifetimeWorkerArgument, .. var workerArguments]:
                    await Extensions.FFmpeg.FFmpegWorkerProcessLifetimeTests.RunWorkerAsync(workerArguments);
                    break;
                default:
                    Console.Error.WriteLine("Run tests with dotnet test. Direct execution requires a supported worker argument.");
                    return 2;
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    internal static async Task RunAsync(params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(typeof(TestWorkerProgram).Assembly.Location);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        Assert.That(process.ExitCode, Is.Zero, await stdout + await stderr);
    }
}
