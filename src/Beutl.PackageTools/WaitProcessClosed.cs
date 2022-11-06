using System.Diagnostics;

using Beutl.PackageTools.Properties;

namespace Beutl.PackageTools;

public static class WaitForProcessExited
{
    private static readonly Process[] _beutlProcesses;
    private static readonly Process[] _bptProcesses;

    static WaitForProcessExited()
    {
        _beutlProcesses = Process.GetProcessesByName("Beutl");
        _bptProcesses = Process.GetProcessesByName("bpt");
    }

    public static int Count => _beutlProcesses.Length;

    public static int PackageToolsCount => _bptProcesses.Length - 1;

    public static async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        if (Count == 0)
        {
            return;
        }

        foreach (Process item in _beutlProcesses)
        {
            if (!item.HasExited)
            {
                await item.WaitForExitAsync(cancellationToken);
            }
        }
    }

    public static async ValueTask Guard(CancellationToken cancellationToken)
    {
        if (Count > 0)
        {
            Console.WriteLine(Resources.XXXInstancesAreRunning, Count);
            Console.WriteLine(Resources.PleaseTerminateThem);

            await WaitAsync(cancellationToken);
        }
    }
}
