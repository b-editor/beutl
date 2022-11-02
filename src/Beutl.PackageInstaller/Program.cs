using System;
using System.Diagnostics.CodeAnalysis;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.PackageInstaller;
internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        static bool TryParse(string s, [NotNullWhen(true)] out PackageIdentity? packageIdentity)
        {
            string[] splited = s.Split('/');
            packageIdentity = null;
            if (splited.Length == 2)
            {
                packageIdentity = new PackageIdentity(splited[0], new NuGetVersion(splited[1]));
                return true;
            }
            else
            {
                return false;
            }
        }

        static void LoadArgs(HashSet<PackageIdentity> packages, string[] args, int start)
        {
            for (int i = start + 1; i < args.Length; i++)
            {
                string s = args[i];
                if (s.StartsWith('-'))
                    break;

                if (TryParse(s, out PackageIdentity? packageIdentity))
                {
                    packages.Add(packageIdentity);
                }
            }
        }

        int indexOfInstalls = Array.IndexOf(args, "--installs");
        int indexOfUninstalls = Array.IndexOf(args, "--uninstalls");
        int indexOfUpdates = Array.IndexOf(args, "--updates");

        var installs = new HashSet<PackageIdentity>();
        var uninstalls = new HashSet<PackageIdentity>();
        var updates = new HashSet<PackageIdentity>();
        LoadArgs(installs, args, indexOfInstalls);
        LoadArgs(uninstalls, args, indexOfUninstalls);
        LoadArgs(updates, args, indexOfUpdates);

        if (args.Contains("--show-window"))
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
