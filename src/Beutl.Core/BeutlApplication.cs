using System.ComponentModel;
using System.Diagnostics;

using Beutl.Collections;

using Microsoft.Extensions.Logging;

namespace Beutl;

public class BeutlApplication : Hierarchical, IHierarchicalRoot
{
    public static readonly CoreProperty<Project?> ProjectProperty;
    private Project? _project;
    private ILoggerFactory? _loggerFactory;

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

    public ILoggerFactory LoggerFactory
    {
        get => _loggerFactory!;
        internal set => _loggerFactory ??= value;
    }

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
