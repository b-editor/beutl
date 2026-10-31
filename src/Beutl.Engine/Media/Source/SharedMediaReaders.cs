using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;

namespace Beutl.Media.Source;

/// <summary>
/// Shares the open readers of a file between every source that plays it. Splitting a clip regenerates its
/// sources, so each piece would otherwise open (and index) the file again when playback reaches it.
/// </summary>
/// <remarks>
/// Each source reads through its own handle. A read uses the open reader the request continues forward from;
/// a second reader opens only when two consumers keep reading different parts of the file at the same time.
/// </remarks>
internal sealed class SharedMediaReaders(Func<long> clock)
{
    // Consecutive pieces of a split clip resume from where the previous piece stopped. Decoding forward a
    // few frames is far cheaper than opening the file or seeking back to a keyframe.
    internal const int VideoContinuationFrames = 100;
    // A consumer that read this recently is still playing; taking its reader away would make it seek.
    internal const long BusyMilliseconds = 500;
    internal const int MaxReaders = 3;

    private readonly Lock _lock = new();
    private readonly Dictionary<Key, Group> _groups = [];

    public static SharedMediaReaders Shared { get; } = new(() => Environment.TickCount64);

    internal readonly record struct Key(string Path, MediaMode Mode, bool PreferProxy, ProxyPreset Preset, long ProxyVersion);

    /// <summary>
    /// Returns a new handle on the shared readers of <paramref name="key"/>, opening the first one if needed.
    /// </summary>
    /// <param name="open">Opens one more reader of the file; exceptions propagate to the caller.</param>
    /// <param name="shareable">
    /// Whether a freshly opened first reader may serve other sources; an unshareable one keeps serving this
    /// handle only, so the next source opens its own.
    /// </param>
    public MediaReader Open(Key key, Func<MediaReader> open, Func<MediaReader, bool>? shareable = null)
    {
        while (true)
        {
            Group group;
            bool created = false;
            lock (_lock)
            {
                if (!_groups.TryGetValue(key, out group!) || !group.TryAddHandle())
                {
                    group = new Group(this, key, open, clock);
                    _groups[key] = group;
                    created = true;
                }
            }

            var handle = new Handle(group);
            try
            {
                group.EnsureOpened();
            }
            catch
            {
                handle.Dispose();
                Detach(group);
                // Another source's first open failed while this one waited; try once on its own.
                if (!created) continue;
                throw;
            }

            if (created && shareable?.Invoke(group.First) == false)
                Detach(group);
            return handle;
        }
    }

    /// <summary>
    /// Makes later opens of <paramref name="path"/> start fresh readers, for example after the file changed on
    /// disk. Existing handles keep reading their readers until their sources reload.
    /// </summary>
    public void Invalidate(string path)
    {
        lock (_lock)
        {
            foreach (Key key in _groups.Keys.Where(key => key.Path == path).ToArray())
                _groups.Remove(key);
        }
    }

    internal int OpenGroupCount
    {
        get { lock (_lock) return _groups.Count; }
    }

    private void Detach(Group group)
    {
        lock (_lock)
        {
            if (_groups.TryGetValue(group.Key, out Group? current) && ReferenceEquals(current, group))
                _groups.Remove(group.Key);
        }
    }

    private sealed class Slot(MediaReader reader)
    {
        public MediaReader Reader { get; } = reader;
        // The position the next read continues from (a frame for video, a sample for audio); unknown until read.
        public long? Position { get; set; }
        public Handle? Owner { get; set; }
        public long LastUse { get; set; } = long.MinValue;
    }

    private sealed class Group(SharedMediaReaders owner, Key key, Func<MediaReader> open, Func<long> clock)
    {
        private readonly Lock _lock = new();
        private readonly List<Slot> _slots = [];
        // Set once before any handle is returned. Handles read stream info without the lock while another
        // consumer may be adding a reader.
        private MediaReader? _first;
        // Reaches zero once, when the group closes; it is never revived.
        private int _handles = 1;
        private bool _closed;
        private volatile Exception? _openFailure;

        public Key Key { get; } = key;

        public MediaReader First => _first ?? throw new InvalidOperationException("The shared reader is not open.");

        // Runs under the pool's lock, so it must not wait for this group's lock, which an open can hold for a
        // long time; other files would wait too.
        public bool TryAddHandle()
        {
            while (true)
            {
                int handles = Volatile.Read(ref _handles);
                if (handles <= 0 || _openFailure is not null) return false;
                if (Interlocked.CompareExchange(ref _handles, handles + 1, handles) == handles) return true;
            }
        }

        public void EnsureOpened()
        {
            lock (_lock)
            {
                if (_openFailure is not null) throw new InvalidOperationException("The shared reader could not be opened.", _openFailure);
                if (_first is not null) return;
                try
                {
                    MediaReader reader = open();
                    _slots.Add(new Slot(reader));
                    _first = reader;
                }
                catch (Exception ex)
                {
                    _openFailure = ex;
                    throw;
                }
            }
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _handles) > 0) return;

            Slot[] closing;
            lock (_lock)
            {
                _closed = true;
                closing = [.. _slots];
                _slots.Clear();
            }

            owner.Detach(this);
            foreach (Slot slot in closing)
                slot.Reader.Dispose();
        }

        public bool ReadVideo(Handle handle, int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            lock (_lock)
            {
                image = null;
                if (_closed || _slots.Count == 0) return false;
                Slot slot = Choose(handle, frame, VideoContinuationFrames);
                bool read = slot.Reader.ReadVideo(frame, out image);
                Claim(handle, slot, frame);
                return read;
            }
        }

        public bool ReadAudio(Handle handle, int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            lock (_lock)
            {
                sound = null;
                if (_closed || _slots.Count == 0) return false;
                Slot slot = Choose(handle, start, Math.Max(1, First.AudioInfo.SampleRate));
                bool read = slot.Reader.ReadAudio(start, length, out sound);
                Claim(handle, slot, (long)start + length);
                return read;
            }
        }

        private void Claim(Handle handle, Slot slot, long position)
        {
            long now = clock();
            slot.Position = position;
            slot.Owner = handle;
            slot.LastUse = now;
            handle.Slot = slot;
            handle.LastRead = now;
        }

        private Slot Choose(Handle handle, long position, long continuation)
        {
            Slot? next = null;
            long nextGap = long.MaxValue;
            foreach (Slot slot in _slots)
            {
                if (slot.Position is not { } from) continue;
                long gap = position - from;
                if (gap < 0 || gap > continuation || gap >= nextGap) continue;
                next = slot;
                nextGap = gap;
            }
            if (next is not null) return next;

            if (handle.Slot is { } own && ReferenceEquals(own.Owner, handle)) return own;

            long now = clock();
            Slot? idle = _slots.Where(slot => !IsBusy(slot, handle, now)).MinBy(slot => slot.LastUse);
            if (idle is not null) return idle;

            // Every reader serves another consumer that is still reading. A consumer that already had one
            // lost it to such a consumer, so both keep running; give it a reader of its own. A newcomer
            // usually follows a consumer that just finished, such as the next piece of a split clip.
            if (handle.Slot is not null && _slots.Count < MaxReaders && TryOpenSlot() is { } opened) return opened;
            return _slots.MinBy(slot => slot.LastUse)!;
        }

        private static bool IsBusy(Slot slot, Handle handle, long now)
            => slot.Owner is { } user && !ReferenceEquals(user, handle) && now - user.LastRead < BusyMilliseconds;

        private Slot? TryOpenSlot()
        {
            MediaReader reader;
            try
            {
                reader = open();
            }
            catch
            {
                return null;
            }

            // Frames and samples address the same media only if this reader resolved the same way.
            MediaReader first = First;
            if (reader.HasVideo != first.HasVideo || reader.HasAudio != first.HasAudio
                || (reader.ProxyResolution is null) != (first.ProxyResolution is null))
            {
                reader.Dispose();
                return null;
            }

            var slot = new Slot(reader);
            _slots.Add(slot);
            return slot;
        }

        internal int ReaderCount
        {
            get { lock (_lock) return _slots.Count; }
        }
    }

    private sealed class Handle(Group group) : MediaReader
    {
        internal Slot? Slot { get; set; }

        internal long LastRead { get; set; } = long.MinValue;

        internal Group Group => group;

        public override VideoStreamInfo VideoInfo => group.First.VideoInfo;

        public override AudioStreamInfo AudioInfo => group.First.AudioInfo;

        public override bool HasVideo => group.First.HasVideo;

        public override bool HasAudio => group.First.HasAudio;

        public override ProxyResolution? ProxyResolution => group.First.ProxyResolution;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            if (IsDisposed)
            {
                image = null;
                return false;
            }

            return group.ReadVideo(this, frame, out image);
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            if (IsDisposed)
            {
                sound = null;
                return false;
            }

            return group.ReadAudio(this, start, length, out sound);
        }

        // Only an explicit dispose releases the readers; a finalizer must not take the group's locks.
        protected override void Dispose(bool disposing)
        {
            if (disposing) group.Release();
        }
    }

    // The reader a resource's reader stands for: the first shared reader of a handle, or the reader itself.
    internal static MediaReader UnderlyingOf(MediaReader reader) => reader is Handle handle ? handle.Group.First : reader;

    internal static int ReaderCountOf(MediaReader reader) => reader is Handle handle ? handle.Group.ReaderCount : 1;
}
