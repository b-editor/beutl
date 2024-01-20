using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using Beutl.Collections;

using Microsoft.Extensions.Logging;

namespace Beutl;

public class BeutlApplication : Hierarchical, IHierarchicalRoot
{
    public static readonly CoreProperty<Project?> ProjectProperty;
    private Project? _project;

    static BeutlApplication()
    {
        ProjectProperty = ConfigureProperty<Project?, BeutlApplication>(nameof(Project))
            .Accessor(o => o.Project, (o, v) => o.Project = v)
            .Register();
    }

    public BeutlApplication()
    {
        Items = new HierarchicalList<ProjectItem>(this);
    }

    public static BeutlApplication Current { get; } = new();

    internal static ActivitySource ActivitySource { get; } = new("Beutl.Application", GitVersionInformation.SemVer);

    [SuppressMessage("Performance", "CA1822:メンバーを static に設定します", Justification = "<保留中>")]
    public ILoggerFactory LoggerFactory => Logging.Log.LoggerFactory;

    public Project? Project
    {
        get => _project;
        set => SetAndRaise(ProjectProperty, ref _project, value);
    }

    public ICoreList<ProjectItem> Items { get; }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs<Project?> { PropertyName: nameof(Project) } args2)
        {
            if (args2.OldValue != null)
            {
                HierarchicalChildren.Remove(args2.OldValue);
            }
            if (args2.NewValue != null)
            {
                HierarchicalChildren.Add(args2.NewValue);
            }
        }
    }
}
