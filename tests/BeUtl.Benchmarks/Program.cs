using System.Linq;
using System.Runtime.InteropServices;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<DictionaryBench>();


[MemoryDiagnoser]
public class DictionaryBench
{
    private readonly Dictionary<int, int> _dictionary;

    public DictionaryBench()
    {
        _dictionary = Enumerable.Range(0, 500).Select(i => new KeyValuePair<int, int>(i, i)).ToDictionary(i => i.Key, i => i.Value);
    }

    [Benchmark]
    public void ForEach()
    {
        int sum = 0;
        foreach (var item in _dictionary)
        {
            sum += item.Value;
        }
    }

    [Benchmark]
    public void ForEachArray()
    {
        KeyValuePair<int, int>[] array = new KeyValuePair<int, int>[_dictionary.Count];
        (_dictionary as ICollection<KeyValuePair<int, int>>).CopyTo(array, 0);
        int sum = 0;
        foreach (var item in array)
        {
            sum += item.Value;
        }
    }

    [Benchmark]
    public void ForEachKey()
    {
        int sum = 0;
        foreach (var item in _dictionary.Keys)
        {
            sum += _dictionary[item];
        }
    }

    [Benchmark]
    public void ForEachValue()
    {
        int sum = 0;
        foreach (var item in _dictionary.Values)
        {
            sum += item;
        }
    }

    [Benchmark]
    public void ForEachKeysArray()
    {
        int[] array = new int[_dictionary.Count];
        _dictionary.Keys.CopyTo(array, 0);
        int sum = 0;
        foreach (var item in array)
        {
            sum += _dictionary[item];
        }
    }

    [Benchmark]
    public void ForEachValuesArray()
    {
        int[] array = new int[_dictionary.Count];
        _dictionary.Values.CopyTo(array, 0);
        int sum = 0;
        foreach (var item in array)
        {
            sum += item;
        }
    }
}

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
