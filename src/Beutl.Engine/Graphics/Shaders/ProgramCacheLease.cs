namespace Beutl.Graphics.Shaders;

/// <summary>A checkout that keeps one program alive until the lease is returned.</summary>
internal sealed class ProgramCacheLease<TProgram> : IDisposable
    where TProgram : class, IDisposable
{
    private ProgramCache<TProgram>? _owner;
    private readonly ProgramCache<TProgram>.Entry? _entry;
    private TProgram? _program;

    internal ProgramCacheLease(
        ProgramCache<TProgram> owner,
        ProgramCache<TProgram>.Entry? entry,
        TProgram program,
        bool isCacheHit)
    {
        _owner = owner;
        _entry = entry;
        _program = program;
        IsCacheHit = isCacheHit;
    }

    public TProgram Program
        => _program ?? throw new ObjectDisposedException(nameof(ProgramCacheLease<TProgram>));

    public bool IsCacheHit { get; }

    public void Dispose()
    {
        ProgramCache<TProgram>? owner = Interlocked.Exchange(ref _owner, null);
        if (owner is null)
            return;

        TProgram program = Interlocked.Exchange(ref _program, null)
            ?? throw new InvalidOperationException("A program-cache lease lost its checked-out program.");
        owner.Release(_entry, program);
    }
}
