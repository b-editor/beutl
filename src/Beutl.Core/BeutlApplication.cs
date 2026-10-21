using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.Extensions.Logging;

namespace Beutl;

public class BeutlApplication : Hierarchical, IHierarchicalRoot
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
        ScanProperties<BeutlApplication>();
        ProjectProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Project)));
    }

    // The open project, which the property makes a child of the application.
    public IProperty<Project?> ProjectProperty { get; } = new ValueOnlyProperty<Project?>(null);

    public Project? Project
    {
        get => ProjectProperty.Value;
        set => ProjectProperty.Value = value;
    }

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
