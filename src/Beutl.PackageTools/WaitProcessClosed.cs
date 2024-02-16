using Beutl.PackageTools.Properties;

namespace Beutl.PackageTools;

public static class WaitForProcessExited
{
    private static readonly Process[] s_beutlProcesses;
    private static readonly Process[] s_bptProcesses;

    static WaitForProcessExited()
    {
        s_beutlProcesses =
        [
            .. Process.GetProcessesByName("Beutl"),
            .. Process.GetProcessesByName("beutl")
        ];

        s_bptProcesses =
        [
            ..Process.GetProcessesByName("Beutl.PackageTools"),
            ..Process.GetProcessesByName("Beutl.PackageTools.UI"),
            ..Process.GetProcessesByName("beutl-pkg"),
        ];
    }

    public static int Count => s_beutlProcesses.Length;

    public static int PackageToolsCount => s_bptProcesses.Length - 1;

    public static async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        if (Count == 0)
        {
            return;
        }

        foreach (Process item in s_beutlProcesses)
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
