using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Beutl.Models;

public partial class FrameCacheManager
{
    // |□■■■■□□□■□■■■□|
    // CalculateBlocks() -> [(1, 4), (8, 1), (10, 3)]
    public ImmutableArray<CacheBlock> CalculateBlocks()
    {
        return CalculateBlocks(int.MinValue, int.MaxValue);
    }

    public ImmutableArray<CacheBlock> CalculateBlocks(int start, int end)
    {
        lock (_lock)
        {
            var list = new List<CacheBlock>();
            int blockStart = -1;
            int expect = 0;
            int count = 0;
            bool isLocked = false;
            IEnumerable<KeyValuePair<int, CacheEntry>> items
                = start == int.MinValue && end == int.MaxValue
                    ? _entries
                    : GetRange(_entries, start, end);

            foreach ((int key, CacheEntry item) in items)
            {
                if (blockStart == -1)
                {
                    blockStart = key;
                    isLocked = item.IsLocked;
                    expect = key;
                }

                if (expect == key && isLocked == item.IsLocked)
                {
                    count++;
                    expect = key + 1;
                }
                else
                {
                    list.Add(new(blockStart, count, isLocked));
                    blockStart = -1;
                    count = 0;

                    blockStart = key;
                    isLocked = item.IsLocked;
                    expect = key + 1;
                    count++;
                }
            }

            if (blockStart != -1)
            {
                list.Add(new(blockStart, count, isLocked));
            }

            return [.. list];
        }
    }

    public void UpdateBlocks()
    {
        lock (_lock)
        {
            Blocks = CalculateBlocks();
            BlocksUpdated?.Invoke(Blocks);
        }
    }

    public record CacheBlock(int Start, int Length, bool IsLocked);
}
