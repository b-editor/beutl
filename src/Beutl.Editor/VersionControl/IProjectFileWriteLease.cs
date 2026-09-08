namespace Beutl.Editor.VersionControl;

/// <summary>
/// A reservation of the project workspace, held while project files are written so that no
/// version-control operation can replace them mid-write. Disposing it releases the reservation.
/// </summary>
internal interface IProjectFileWriteLease : IDisposable;

internal interface IProjectFileWriteAdmission
{
    IProjectFileWriteLease? TryBeginProjectFileWrite();
}
