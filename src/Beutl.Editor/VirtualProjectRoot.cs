namespace Beutl.Editor;

/// <summary>
/// エクスポート処理用の仮想的なIHierarchicalRoot実装。
/// Projectをアタッチしてリソース収集を行う際に使用します。
/// </summary>
public sealed class VirtualProjectRoot : Hierarchical, IHierarchicalRoot
{
    private Project? _attachedProject;

    public event EventHandler<IHierarchical>? DescendantAttached;

    public event EventHandler<IHierarchical>? DescendantDetached;

    /// <summary>
    /// プロジェクトをこのルートにアタッチします。
    /// </summary>
    /// <param name="project">アタッチするプロジェクト</param>
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
    /// 現在アタッチされているプロジェクトをデタッチします。
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
