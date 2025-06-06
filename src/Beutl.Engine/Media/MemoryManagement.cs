using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Beutl.Media;

/// <summary>
/// Provides comprehensive memory management utilities and disposable tracking for better resource management.
/// Supports automatic categorization and tracking of various disposable object types including SkiaSharp objects.
/// </summary>
public static class MemoryManagement
{
    private static readonly ConditionalWeakTable<object, DisposableInfo> s_disposableTracker = [];
    private static readonly ConcurrentDictionary<string, long> s_allocationStats = [];
    private static readonly ConcurrentDictionary<DisposableCategory, CategoryStats> s_categoryStats = [];

    /// <summary>
    /// Tracks a disposable object for debugging purposes.
    /// Automatically categorizes the object based on its type.
    /// Only active in DEBUG builds to avoid performance impact in release.
    /// </summary>
    [Conditional("DEBUG")]
    public static void TrackDisposable<T>(T disposable, DisposableCategory? category = null, [CallerMemberName] string? memberName = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0) 
        where T : class, IDisposable
    {
        var detectedCategory = category ?? DetectCategory(disposable);
        var info = new DisposableInfo(typeof(T).Name, detectedCategory, memberName, filePath, lineNumber);
        s_disposableTracker.AddOrUpdate(disposable, info);
        
        // Track allocation statistics
        string typeName = typeof(T).Name;
        s_allocationStats.AddOrUpdate(typeName, 1, (_, count) => count + 1);
        
        // Track category statistics
        s_categoryStats.AddOrUpdate(detectedCategory, 
            new CategoryStats(1, 0), 
            (_, stats) => new CategoryStats(stats.Count + 1, stats.EstimatedBytes));
    }

    /// <summary>
    /// Tracks a disposable object with explicit category and estimated memory usage.
    /// </summary>
    [Conditional("DEBUG")]
    public static void TrackDisposable<T>(T disposable, DisposableCategory category, long estimatedBytes, [CallerMemberName] string? memberName = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0) 
        where T : class, IDisposable
    {
        var info = new DisposableInfo(typeof(T).Name, category, memberName, filePath, lineNumber, estimatedBytes);
        s_disposableTracker.AddOrUpdate(disposable, info);
        
        // Track allocation statistics
        string typeName = typeof(T).Name;
        s_allocationStats.AddOrUpdate(typeName, 1, (_, count) => count + 1);
        
        // Track category statistics
        s_categoryStats.AddOrUpdate(category, 
            new CategoryStats(1, estimatedBytes), 
            (_, stats) => new CategoryStats(stats.Count + 1, stats.EstimatedBytes + estimatedBytes));
    }

    /// <summary>
    /// Convenience method for tracking SkiaSharp objects with automatic size estimation.
    /// </summary>
    [Conditional("DEBUG")]
    public static void TrackSkiaObject<T>(T skiaObject, [CallerMemberName] string? memberName = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0) 
        where T : class, IDisposable
    {
        long estimatedBytes = EstimateSkiaObjectSize(skiaObject);
        TrackDisposable(skiaObject, DisposableCategory.Graphics, estimatedBytes, memberName, filePath, lineNumber);
    }

    /// <summary>
    /// Tracks an object that implements a custom disposable pattern.
    /// </summary>
    [Conditional("DEBUG")]
    public static void TrackCustomDisposable<T>(T disposable, DisposableCategory category, [CallerMemberName] string? memberName = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0) 
        where T : class
    {
        var info = new DisposableInfo(typeof(T).Name, category, memberName, filePath, lineNumber);
        s_disposableTracker.AddOrUpdate(disposable, info);
        
        // Track allocation statistics
        string typeName = typeof(T).Name;
        s_allocationStats.AddOrUpdate(typeName, 1, (_, count) => count + 1);
        
        // Track category statistics
        s_categoryStats.AddOrUpdate(category, 
            new CategoryStats(1, 0), 
            (_, stats) => new CategoryStats(stats.Count + 1, stats.EstimatedBytes));
    }

    /// <summary>
    /// Marks a disposable object as disposed.
    /// </summary>
    [Conditional("DEBUG")]
    public static void MarkDisposed<T>(T disposable) 
        where T : class, IDisposable
    {
        if (s_disposableTracker.TryGetValue(disposable, out DisposableInfo? info))
        {
            info.IsDisposed = true;
            
            // Update category statistics
            if (s_categoryStats.TryGetValue(info.Category, out CategoryStats stats))
            {
                CategoryStats newStats = new(stats.Count, stats.EstimatedBytes - info.EstimatedBytes);
                s_categoryStats.TryUpdate(info.Category, newStats, stats);
            }
        }
    }

    /// <summary>
    /// Marks a custom disposable object as disposed.
    /// </summary>
    [Conditional("DEBUG")]
    public static void MarkCustomDisposableDisposed<T>(T disposable) 
        where T : class
    {
        if (s_disposableTracker.TryGetValue(disposable, out DisposableInfo? info))
        {
            info.IsDisposed = true;
            
            // Update category statistics
            if (s_categoryStats.TryGetValue(info.Category, out CategoryStats stats))
            {
                CategoryStats newStats = new(stats.Count, stats.EstimatedBytes - info.EstimatedBytes);
                s_categoryStats.TryUpdate(info.Category, newStats, stats);
            }
        }
    }

    /// <summary>
    /// Gets comprehensive statistics about tracked disposable objects.
    /// </summary>
    public static ResourceStatistics GetStatistics()
    {
        var undisposedObjects = new List<DisposableInfo>();
        var categoryBreakdown = new Dictionary<DisposableCategory, List<DisposableInfo>>();
        
        // Note: This is a debugging feature and may have performance implications
#if DEBUG
        foreach (var (obj, info) in s_disposableTracker)
        {
            if (!info.IsDisposed)
            {
                undisposedObjects.Add(info);
                
                if (!categoryBreakdown.TryGetValue(info.Category, out var list))
                {
                    list = [];
                    categoryBreakdown[info.Category] = list;
                }
                list.Add(info);
            }
        }
#endif

        return new ResourceStatistics(
            undisposedObjects, 
            s_allocationStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            s_categoryStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            categoryBreakdown.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<DisposableInfo>)kvp.Value));
    }

    /// <summary>
    /// Gets statistics for a specific category of disposable objects.
    /// </summary>
    public static CategoryStats GetCategoryStatistics(DisposableCategory category)
    {
        return s_categoryStats.TryGetValue(category, out var stats) 
            ? stats 
            : new CategoryStats(0, 0);
    }

    /// <summary>
    /// Gets all currently tracked live objects of a specific category.
    /// </summary>
    public static IEnumerable<DisposableInfo> GetLiveObjects(DisposableCategory? category = null)
    {
#if DEBUG
        foreach (var (obj, info) in s_disposableTracker)
        {
            if (!info.IsDisposed && (category == null || info.Category == category))
            {
                yield return info;
            }
        }
#else
        return Enumerable.Empty<DisposableInfo>();
#endif
    }

    /// <summary>
    /// Detects the category of a disposable object based on its type.
    /// </summary>
    private static DisposableCategory DetectCategory<T>(T disposable) where T : class, IDisposable
    {
        return disposable switch
        {
            SKPaint or SKPath or SKImage or SKSurface or SKCanvas or SKShader or SKColorFilter or SKImageFilter => DisposableCategory.Graphics,
            IBitmap => DisposableCategory.Graphics,
            IAudio => DisposableCategory.Audio,
            System.IO.Stream => DisposableCategory.IO,
            System.Net.Sockets.Socket => DisposableCategory.Network,
            _ when disposable.GetType().Namespace?.StartsWith("SkiaSharp") == true => DisposableCategory.Graphics,
            _ when disposable.GetType().Namespace?.Contains("Audio") == true => DisposableCategory.Audio,
            _ when disposable.GetType().Namespace?.Contains("IO") == true => DisposableCategory.IO,
            _ => DisposableCategory.General
        };
    }

    /// <summary>
    /// Estimates the memory size of a SkiaSharp object.
    /// </summary>
    private static long EstimateSkiaObjectSize<T>(T skiaObject) where T : class, IDisposable
    {
        return skiaObject switch
        {
            SKImage image => image.Width * image.Height * 4L, // Assume RGBA
            SKSurface surface => surface.Canvas.LocalClipBounds.Width * surface.Canvas.LocalClipBounds.Height * 4L,
            SKBitmap bitmap => bitmap.Width * bitmap.Height * bitmap.BytesPerPixel,
            SKPaint => 64, // Small object
            SKPath => 128, // Variable size, use conservative estimate
            _ => 32 // Default small object size
        };
    }

    /// <summary>
    /// Forces garbage collection and provides memory usage information.
    /// Should only be used for debugging purposes.
    /// </summary>
    [Conditional("DEBUG")]
    public static void ForceGarbageCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// Gets current memory usage information.
    /// </summary>
    public static MemoryUsage GetMemoryUsage()
    {
        long estimatedTrackedBytes = s_categoryStats.Values.Sum(stats => stats.EstimatedBytes);
        
        return new MemoryUsage(
            AllocatedBytes: GC.GetTotalMemory(false),
            EstimatedTrackedBytes: estimatedTrackedBytes,
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2)
        );
    }

    /// <summary>
    /// Creates a tracked SkiaSharp object with automatic disposal tracking.
    /// </summary>
    public static T CreateTrackedSkiaObject<T>(Func<T> factory, [CallerMemberName] string? memberName = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0) 
        where T : class, IDisposable
    {
        var obj = factory();
        TrackSkiaObject(obj, memberName, filePath, lineNumber);
        return obj;
    }

    /// <summary>
    /// Creates a disposable wrapper for tracking custom objects.
    /// </summary>
    public static DisposableWrapper<T> CreateTrackedWrapper<T>(T value, Action<T>? disposeAction = null, DisposableCategory category = DisposableCategory.General) 
        where T : class
    {
        return new DisposableWrapper<T>(value, disposeAction, category);
    }
}

/// <summary>
/// Information about a tracked disposable object.
/// </summary>
public sealed class DisposableInfo
{
    public DisposableInfo(string typeName, DisposableCategory category, string? memberName, string? filePath, int lineNumber, long estimatedBytes = 0)
    {
        TypeName = typeName;
        Category = category;
        MemberName = memberName;
        FilePath = filePath;
        LineNumber = lineNumber;
        EstimatedBytes = estimatedBytes;
        CreatedAt = DateTime.UtcNow;
    }

    public string TypeName { get; }
    public DisposableCategory Category { get; }
    public string? MemberName { get; }
    public string? FilePath { get; }
    public int LineNumber { get; }
    public long EstimatedBytes { get; }
    public DateTime CreatedAt { get; }
    public bool IsDisposed { get; set; }
    public TimeSpan Age => DateTime.UtcNow - CreatedAt;

    public override string ToString()
    {
        string location = FilePath is not null 
            ? $"{Path.GetFileName(FilePath)}:{LineNumber}" 
            : "Unknown";
        string size = EstimatedBytes > 0 ? $" ({EstimatedBytes:N0} bytes)" : "";
        return $"{TypeName} [{Category}] created in {MemberName} at {location} ({CreatedAt:HH:mm:ss}){size} - {(IsDisposed ? "Disposed" : "LEAKED")}";
    }
}

/// <summary>
/// Comprehensive statistics about disposable object usage.
/// </summary>
public sealed record ResourceStatistics(
    IReadOnlyList<DisposableInfo> UndisposedObjects,
    IReadOnlyDictionary<string, long> AllocationCounts,
    IReadOnlyDictionary<DisposableCategory, CategoryStats> CategoryStats,
    IReadOnlyDictionary<DisposableCategory, IReadOnlyList<DisposableInfo>> CategoryBreakdown
);

/// <summary>
/// Statistics for a specific category of disposable objects.
/// </summary>
public sealed record CategoryStats(
    long Count,
    long EstimatedBytes
)
{
    public double EstimatedMB => EstimatedBytes / (1024.0 * 1024.0);
};

/// <summary>
/// Categories of disposable objects for better tracking and reporting.
/// </summary>
public enum DisposableCategory
{
    /// <summary>General disposable objects</summary>
    General,
    /// <summary>Graphics resources (SkiaSharp objects, bitmaps, etc.)</summary>
    Graphics,
    /// <summary>Audio resources</summary>
    Audio,
    /// <summary>I/O resources (streams, files, etc.)</summary>
    IO,
    /// <summary>Network resources</summary>
    Network,
    /// <summary>Platform-specific resources</summary>
    Platform,
    /// <summary>Memory buffers and allocations</summary>
    Memory
}

/// <summary>
/// Information about current memory usage.
/// </summary>
public sealed record MemoryUsage(
    long AllocatedBytes,
    long EstimatedTrackedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections)
{
    public double AllocatedMB => AllocatedBytes / (1024.0 * 1024.0);
    public double EstimatedTrackedMB => EstimatedTrackedBytes / (1024.0 * 1024.0);
}

/// <summary>
/// Enhanced disposable base class with automatic tracking and category support.
/// </summary>
public abstract class TrackableDisposable : IDisposable
{
    private bool _disposed;

    protected TrackableDisposable(DisposableCategory category = DisposableCategory.General)
    {
        MemoryManagement.TrackDisposable(this, category);
    }

    protected TrackableDisposable(DisposableCategory category, long estimatedBytes)
    {
        MemoryManagement.TrackDisposable(this, category, estimatedBytes);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Dispose(true);
            MemoryManagement.MarkDisposed(this);
            GC.SuppressFinalize(this);
            _disposed = true;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        // Override in derived classes
    }

    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    ~TrackableDisposable()
    {
        Dispose(false);
    }
}

/// <summary>
/// Helper class for creating disposable wrappers around non-disposable objects that need tracking.
/// </summary>
public sealed class DisposableWrapper<T> : IDisposable where T : class
{
    private readonly T _value;
    private readonly Action<T>? _disposeAction;
    private bool _disposed;

    public DisposableWrapper(T value, Action<T>? disposeAction = null, DisposableCategory category = DisposableCategory.General)
    {
        _value = value;
        _disposeAction = disposeAction;
        MemoryManagement.TrackCustomDisposable(this, category);
    }

    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _value;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposeAction?.Invoke(_value);
            MemoryManagement.MarkCustomDisposableDisposed(this);
            _disposed = true;
        }
    }
}