using Beutl.Extensibility;

namespace Beutl.Editor.VersionControl;

/// <summary>
/// A host that owns editor contexts and can hand out its workspace admission for one of them.
/// </summary>
internal interface IProjectFileWriteAdmissionHost
{
    /// <summary>
    /// Returns this host's admission when <paramref name="context"/> is one of the editor contexts it
    /// currently owns, and <see langword="null"/> otherwise.
    /// </summary>
    IProjectFileWriteAdmission? TryGetAdmission(IEditorContext context);
}

/// <summary>
/// Resolves the workspace admission that governs writes made on behalf of an editor context.
/// </summary>
/// <remarks>
/// <see cref="IProjectFileWriteAdmission"/> is internal, so only the host's own editor context can serve it
/// through <see cref="IServiceProvider"/>; a tool tab attached to an out-of-tree editor context finds nothing
/// there, and treating that absence as permission is exactly the write during a branch switch, pull, or
/// restore that the admission exists to prevent. Hosts therefore register themselves here and answer for the
/// contexts they own at the moment of the write, so a second host in the same process cannot stand in for the
/// first, and a context no host owns is refused rather than admitted.
/// </remarks>
internal static class HostProjectFileWriteAdmission
{
    private static readonly object s_gate = new();
    private static readonly List<WeakReference<IProjectFileWriteAdmissionHost>> s_hosts = [];

    /// <summary>Registers a host. Hosts are held weakly, so nothing needs to unregister.</summary>
    public static void RegisterHost(IProjectFileWriteAdmissionHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (s_gate)
        {
            s_hosts.RemoveAll(static reference => !reference.TryGetTarget(out _));
            foreach (WeakReference<IProjectFileWriteAdmissionHost> reference in s_hosts)
            {
                if (reference.TryGetTarget(out IProjectFileWriteAdmissionHost? existing)
                    && ReferenceEquals(existing, host))
                {
                    return;
                }
            }

            s_hosts.Add(new WeakReference<IProjectFileWriteAdmissionHost>(host));
        }
    }

    /// <summary>
    /// Finds the admission of the host that owns <paramref name="context"/>, or <see langword="null"/> when
    /// no registered host does.
    /// </summary>
    public static IProjectFileWriteAdmission? Resolve(IEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IProjectFileWriteAdmissionHost[] hosts;
        lock (s_gate)
        {
            s_hosts.RemoveAll(static reference => !reference.TryGetTarget(out _));
            hosts = new IProjectFileWriteAdmissionHost[s_hosts.Count];
            int count = 0;
            foreach (WeakReference<IProjectFileWriteAdmissionHost> reference in s_hosts)
            {
                if (reference.TryGetTarget(out IProjectFileWriteAdmissionHost? host))
                    hosts[count++] = host;
            }

            Array.Resize(ref hosts, count);
        }

        // Outside the gate: a host answers by inspecting its own tab list, which lives on the UI thread.
        foreach (IProjectFileWriteAdmissionHost host in hosts)
        {
            if (host.TryGetAdmission(context) is { } admission)
                return admission;
        }

        return null;
    }
}
