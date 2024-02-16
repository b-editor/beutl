using System.CommandLine;
using System.CommandLine.Parsing;

using Beutl.Configuration;
using Beutl.Logging;
using Beutl.PackageTools;
using Beutl.PackageTools.Properties;
using Beutl.Services;

if (WaitForProcessExited.PackageToolsCount != 0)
{
    Console.WriteLine(Resources.PleaseTerminateOtherInstances);
    return;
}

GlobalConfiguration config = GlobalConfiguration.Instance;
config.Restore(GlobalConfiguration.DefaultFilePath);

var verbose = new Option<bool>(["--verbose", "-v"], () => false)
{
    Description = Resources.VerboseDescription,
};
var clean = new Option<bool>(["--clean", "-c"], () => true)
{
    Description = Resources.CleanDescription,
};
var stayOpen = new Option<bool>("--stay-open", () => false)
{
    IsHidden = true,
};
var launchDebugger = new Option<bool>("--launch-debugger", () => false)
{
    IsHidden = true,
};
var sessionId = new Option<string?>("--session-id", () => null)
{
    IsHidden = true,
};

string? GetSessionId()
{
    int idx = Array.IndexOf(args, "--session-id");
    if (idx >= 0 && idx + 1 < args.Length)
    {
        return args[idx + 1];
    }
    else
    {
        return null;
    }
}

using IDisposable _ = Telemetry.GetDisposable(GetSessionId());
ILogger<Program> logger = Log.CreateLogger<Program>();

var apiApp = new BeutlApiApplication(new HttpClient());
try
{
    await apiApp.RestoreUserAsync(null);
}
catch (Exception ex)
{
    logger.LogError(ex, "An error occurred during authentication");
}

try
{
    var rootCommand = new RunCommand(apiApp, verbose, clean);
    rootCommand.AddGlobalOption(verbose);
    rootCommand.AddGlobalOption(clean);
    rootCommand.AddGlobalOption(stayOpen);
    rootCommand.AddGlobalOption(launchDebugger);
    rootCommand.AddGlobalOption(sessionId);
    rootCommand.AddCommand(new InstallCommand(apiApp, verbose, clean));
    rootCommand.AddCommand(new UninstallCommand(apiApp, verbose, clean));
    rootCommand.AddCommand(new UpdateCommand(apiApp, verbose, clean));
    rootCommand.AddCommand(new CleanCommand(apiApp));
    rootCommand.AddCommand(new ListCommand(apiApp, verbose));

    ParseResult parseResult = rootCommand.Parse(args);
    bool stayOpenValue = parseResult.GetValueForOption(stayOpen);
    bool launchDebuggerValue = parseResult.GetValueForOption(launchDebugger);

#if DEBUG
    if (!Debugger.IsAttached && launchDebuggerValue)
    {
        while (true)
        {
            Thread.Sleep(100);

            if (Debugger.Launch())
                break;
        }
    }
#endif

    await rootCommand.InvokeAsync(args);

    if (stayOpenValue)
    {
        Console.WriteLine(Resources.ToCloseThisWindowPressAnyKey);
        Console.ReadKey();
    }
}
catch (Exception ex)
{
    logger.LogCritical(ex, "An unhandled exception occurred.");
    throw;
}
