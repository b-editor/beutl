namespace Beutl.Graphics.Shaders;

/// <summary>A checkout that keeps one cached program alive until the lease is returned.</summary>
internal sealed class ProgramCacheLease<TProgram> : IDisposable
    where TProgram : class, IDisposable
{
    private ProgramCache<TProgram>? _owner;
    private TProgram? _program;

    internal ProgramCacheLease(
        ProgramCache<TProgram> owner,
        ProgramCache<TProgram>.Entry? entry,
        TProgram program,
        bool isCacheHit,
        bool isTransient)
    {
        _owner = owner;
        Entry = entry;
        _program = program;
        IsCacheHit = isCacheHit;
        IsTransient = isTransient;
    }

    internal ProgramCache<TProgram>.Entry? Entry { get; }

    public TProgram Program
        => _program ?? throw new ObjectDisposedException(nameof(ProgramCacheLease<TProgram>));

    public bool IsCacheHit { get; }

    public bool IsTransient { get; }

    public void Dispose()
    {
        ProgramCache<TProgram>? owner = Interlocked.Exchange(ref _owner, null);
        if (owner is null)
            return;

        TProgram program = Interlocked.Exchange(ref _program, null)
            ?? throw new InvalidOperationException("A program-cache lease lost its checked-out program.");
        owner.Release(Entry, program);
    }
}
