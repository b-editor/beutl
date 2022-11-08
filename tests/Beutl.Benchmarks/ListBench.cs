using System.Runtime.InteropServices;

using BenchmarkDotNet.Attributes;

public class ListBench
{
    private readonly List<int> _list;

    public ListBench()
    {
        _list = Enumerable.Range(0, 500).ToList();
    }

    [Benchmark]
    public void Foreach()
    {
        int sum = 0;
        foreach (int item in _list)
        {
            sum += item;
        }
    }

    [Benchmark]
    public void For()
    {
        int sum = 0;
        for (int i = 0; i < _list.Count; i++)
        {
            sum += _list[i];
        }
    }

    [Benchmark]
    public void SpanForeach()
    {
        int sum = 0;
        foreach (int item in CollectionsMarshal.AsSpan(_list))
        {
            sum += item;
        }
    }

    [Benchmark]
    public void SpanFor()
    {
        int sum = 0;
        Span<int> span = CollectionsMarshal.AsSpan(_list);
        for (int i = 0; i < span.Length; i++)
        {
            sum += span[i];
        }
    }
}
