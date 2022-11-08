
using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class StringCompareBench
{
    private const string Str = "NOFenwknvV+mGあｄgwbtab;nbtrawaafうぇえ";
    private const string Other = "NOFenwknvV+mGあｄgwbtabGEWｌｂｋｎあＦＶＥＷＢ";

    [Benchmark]
    public void MemX_SequenceEqual()
    {
        ReadOnlySpan<char> str = Str.AsSpan();
        ReadOnlySpan<char> other = Other.AsSpan();
        for (int i = 0; i < 50000; i++)
        {
            str.SequenceEqual(other);
        }
    }

    [Benchmark]
    public void MemX_Equals_Ordinal()
    {
        ReadOnlySpan<char> str = Str.AsSpan();
        ReadOnlySpan<char> other = Other.AsSpan();
        for (int i = 0; i < 50000; i++)
        {
            str.Equals(other, StringComparison.Ordinal);
        }
    }

    [Benchmark]
    public void MemX_Equals_OrdinalIgnoreCase()
    {
        ReadOnlySpan<char> str = Str.AsSpan();
        ReadOnlySpan<char> other = Other.AsSpan();
        for (int i = 0; i < 50000; i++)
        {
            str.Equals(other, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Benchmark]
    public void MemX_Equals_Invariant()
    {
        ReadOnlySpan<char> str = Str.AsSpan();
        ReadOnlySpan<char> other = Other.AsSpan();
        for (int i = 0; i < 50000; i++)
        {
            str.Equals(other, StringComparison.InvariantCulture);
        }
    }

    [Benchmark]
    public void MemX_Equals_Current()
    {
        ReadOnlySpan<char> str = Str.AsSpan();
        ReadOnlySpan<char> other = Other.AsSpan();
        for (int i = 0; i < 50000; i++)
        {
            str.Equals(other, StringComparison.CurrentCulture);
        }
    }
}
