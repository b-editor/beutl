using System.CommandLine;
using System.Diagnostics;

using Beutl.PackageTools;
using Beutl.PackageTools.Properties;

if (WaitForProcessExited.PackageToolsCount != 0)
{
    Console.WriteLine(Resources.PleaseTerminateOtherInstances);
    return;
}

var apiApp = new BeutlApiApplication(new HttpClient());

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

var rootCommand = new RunCommand(apiApp, verbose, clean);
rootCommand.AddGlobalOption(verbose);
rootCommand.AddGlobalOption(clean);
rootCommand.AddGlobalOption(stayOpen);
rootCommand.AddGlobalOption(launchDebugger);
rootCommand.AddCommand(new InstallCommand(apiApp, verbose, clean));
rootCommand.AddCommand(new UninstallCommand(apiApp, verbose, clean));
rootCommand.AddCommand(new UpdateCommand(apiApp, verbose, clean));
rootCommand.AddCommand(new CleanCommand(apiApp));
rootCommand.AddCommand(new ListCommand(apiApp, verbose));

bool stayOpenValue = rootCommand.Parse(args).GetValueForOption(stayOpen);
bool launchDebuggerValue = rootCommand.Parse(args).GetValueForOption(launchDebugger);

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
