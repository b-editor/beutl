using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.Extensions.Logging;

namespace Beutl;

public partial class BeutlApplication : Hierarchical, IHierarchicalRoot
{
    internal static readonly string s_version;

    static BeutlApplication()
    {
        // Read from this assembly rather than the entry assembly. Every assembly built from the repository
        // carries the version it was built as (root Directory.Build.targets), so this one matches the
        // application's, and a host that is not the application, such as a test runner, gets it too.
        var asm = typeof(BeutlApplication).Assembly;
        var att = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(i => i.Key == "NuGetVersion");
        s_version = att?.Value ?? "1.0.0";
    }

    public static BeutlApplication Current { get; } = new();

    public static string Version => s_version;

    internal static ActivitySource ActivitySource { get; } = new("Beutl.Application", s_version);

    [SuppressMessage("Performance", "CA1822:メンバーを static に設定します", Justification = "<保留中>")]
    public ILoggerFactory LoggerFactory => Logging.Log.LoggerFactory;

    public BeutlApplication()
    {
        // The active project has to be loaded: one that is not would have no parent, and its objects no root.
        ActiveProject = new ValueOnlyProperty<Project?>(null)
        {
            OwnsValue = false,
            AffectsRender = false,
            Coerce = (_, project) => project is null || Projects.Contains(project)
                ? project
                : throw new InvalidOperationException("The active project must be one of the loaded projects."),
        };
        // Unloading the active project leaves none active.
        Projects.Detached += project =>
        {
            if (ReferenceEquals(ActiveProject.Value, project))
                ActiveProject.Value = null;
        };
        ScanProperties<BeutlApplication>();
    }

    // The loaded projects, which the list makes children of the application. Like any child, a project has one
    // parent, so it is loaded into one application once.
    public IListProperty<Project> Projects { get; } = new ListProperty<Project> { AffectsRender = false };

    // The project the editor shows: one of Projects, or null. It does not own the project; Projects does.
    public IProperty<Project?> ActiveProject { get; }

    public event EventHandler<IHierarchical>? DescendantAttached;

    public event EventHandler<IHierarchical>? DescendantDetached;

    void IHierarchicalRoot.OnDescendantAttached(IHierarchical descendant)
    {
        DescendantAttached?.Invoke(this, descendant);
    }

    void IHierarchicalRoot.OnDescendantDetached(IHierarchical descendant)
    {
        DescendantDetached?.Invoke(this, descendant);
    }
}
