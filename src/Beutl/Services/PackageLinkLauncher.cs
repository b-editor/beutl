namespace Beutl.Services;

internal enum PackageLinkLaunchAction
{
    StartApplication,
    Forwarded,
    Failed
}

internal sealed record PackageLinkLaunchResult(PackageLinkLaunchAction Action, PackageLinkBroker? Broker, string[] Arguments);

internal static class PackageLinkLauncher
{
    public static async Task<PackageLinkLaunchResult> PrepareAsync(
        string[] arguments, string? pipeName = null, TimeSpan? forwardingTimeout = null)
    {
        if (arguments.Length == 0 || !arguments.All(argument => PackageInstallRequest.TryParse(argument, out _)))
            return new(PackageLinkLaunchAction.StartApplication, PackageLinkBroker.TryCreate(pipeName), arguments);

        using var timeout = new CancellationTokenSource(forwardingTimeout ?? TimeSpan.FromSeconds(10));
        int next = 0;
        while (!timeout.IsCancellationRequested)
        {
            // A failed forward is not permission to start another application. Only a
            // newly acquired ownership lock allows takeover if the previous process exits.
            if (PackageLinkBroker.TryCreate(pipeName) is { } broker)
                return new(PackageLinkLaunchAction.StartApplication, broker, arguments[next..]);

            if (await PackageLinkBroker.TryForwardAsync(arguments[next], pipeName, timeout.Token).ConfigureAwait(false))
            {
                if (++next == arguments.Length)
                    return new(PackageLinkLaunchAction.Forwarded, null, []);
                continue;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
        }

        return new(PackageLinkLaunchAction.Failed, null, []);
    }
}
