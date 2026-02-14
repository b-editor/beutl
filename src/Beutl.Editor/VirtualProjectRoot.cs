namespace Beutl.Editor;

/// <summary>
/// A virtual IHierarchicalRoot implementation for export processing.
/// Used when attaching a Project for resource collection.
/// </summary>
public sealed class VirtualProjectRoot : Hierarchical, IHierarchicalRoot
{
    private Project? _attachedProject;

    public event EventHandler<IHierarchical>? DescendantAttached;

    public event EventHandler<IHierarchical>? DescendantDetached;

    /// <summary>
    /// Attaches a project to this root.
    /// </summary>
    /// <param name="project">The project to attach.</param>
    public void AttachProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (_attachedProject != null)
        {
            throw new InvalidOperationException("A project is already attached to this root.");
        }

        _attachedProject = project;
        ((IModifiableHierarchical)this).AddChild(project);
    }

    /// <summary>
    /// Detaches the currently attached project.
    /// </summary>
    public void DetachProject()
    {
        if (_attachedProject != null)
        {
            ((IModifiableHierarchical)this).RemoveChild(_attachedProject);
            _attachedProject = null;
        }
    }

    public void OnDescendantAttached(IHierarchical descendant)
    {
        DescendantAttached?.Invoke(this, descendant);
    }

    public void OnDescendantDetached(IHierarchical descendant)
    {
        DescendantDetached?.Invoke(this, descendant);
    }
}
