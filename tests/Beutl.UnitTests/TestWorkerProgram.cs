using Beutl.UnitTests.Api;

namespace Beutl.UnitTests;

internal static class TestWorkerProgram
{
    public const string PackageInstallWorkerArgument = "--package-install-worker";

    private static int Main(string[] args)
    {
        if (args is not [PackageInstallWorkerArgument])
        {
            Console.Error.WriteLine("Run tests with dotnet test. Direct execution requires a supported worker argument.");
            return 2;
        }

        try
        {
            PackageInstallerCrashRecoveryTests.RunCrashWorker();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
