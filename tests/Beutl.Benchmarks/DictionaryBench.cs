using BenchmarkDotNet.Attributes;

namespace Beutl.Benchmarks;

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
        foreach (KeyValuePair<int, int> item in _dictionary)
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
        foreach (KeyValuePair<int, int> item in array)
        {
            sum += item.Value;
        }
    }

    [Benchmark]
    public void ForEachKey()
    {
        int sum = 0;
        foreach (int item in _dictionary.Keys)
        {
            sum += _dictionary[item];
        }
    }

    [Benchmark]
    public void ForEachValue()
    {
        int sum = 0;
        foreach (int item in _dictionary.Values)
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
        foreach (int item in array)
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
        foreach (int item in array)
        {
            sum += item;
        }
    }
}
