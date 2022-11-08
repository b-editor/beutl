
using BenchmarkDotNet.Attributes;

public class StartWithBench
{
    private const string Str = "NOFenwknvV+mGあｄgwbtab;nbtrawaafうぇえ";
    private const string Other = "NOFenwknvV+mGあｄgwbtabGEWｌｂｋｎあＦＶＥＷＢ";

    [Benchmark]
    public void MemX_StartWith()
    {
        ReadOnlySpan<char> str = Str.AsSpan();
        ReadOnlySpan<char> other = Other.AsSpan();
        for (int i = 0; i < 50000; i++)
        {
            str.StartsWith(other);
        }
    }
    
    [Benchmark]
    public void MemX_StartWith_Ordinal()
    {
        ReadOnlySpan<char> str = Str.AsSpan();
        ReadOnlySpan<char> other = Other.AsSpan();
        for (int i = 0; i < 50000; i++)
        {
            str.StartsWith(other, StringComparison.Ordinal);
        }
    }
}
