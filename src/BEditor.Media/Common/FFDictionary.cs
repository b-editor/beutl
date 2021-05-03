using System;
using System.Collections.Generic;

using BEditor.Media.Helpers;

using FFmpeg.AutoGen;

namespace BEditor.Media.Common
{
    /// <summary>
    /// Represents a wrapper of <see cref="AVDictionary"/>. Used for applying codec and container settings.
    /// </summary>
    internal unsafe class FFDictionary : Wrapper<AVDictionary>
    {
        private readonly bool _requireDisposing;

        /// <summary>
        /// Initializes a new instance of the <see cref="FFDictionary"/> class.
        /// </summary>
        /// <param name="dispose">Should the dictionary be disposed automatically?.</param>
        public FFDictionary(bool dispose = true) : base(null)
        {
            _requireDisposing = dispose;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FFDictionary"/> class.
        /// </summary>
        /// <param name="dictionary">The dictionary to copy.</param>
        /// <param name="dispose">Should the dictionary be disposed automatically?.</param>
        public FFDictionary(Dictionary<string, string> dictionary, bool dispose = true) : base(null)
        {
            Copy(dictionary);
            _requireDisposing = dispose;
        }

        /// <summary>
        /// Gets the number of elements in the dictionary.
        /// </summary>
        public int Count => Pointer == null ? 0 : ffmpeg.av_dict_count(Pointer);

        /// <summary>
        /// Gets or sets the value with the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The value.</returns>
        public string? this[string key]
        {
            get => Get(key);
            set => Set(key, value);
        }

        /// <summary>
        /// Converts a <see cref="AVDictionary"/> to the managed string dictionary.
        /// </summary>
        /// <param name="dictionary">The <see cref="AVDictionary"/> to converter.</param>
        /// <param name="ignoreSuffix">If set the flag <see cref="ffmpeg.AV_DICT_IGNORE_SUFFIX"/> will be used.</param>
        /// <returns>The converted <see cref="Dictionary{TKey, TValue}"/>.</returns>
        public static Dictionary<string, string> ToDictionary(AVDictionary* dictionary, bool ignoreSuffix = false)
        {
            var result = new Dictionary<string, string>();

            var item = ffmpeg.av_dict_get(dictionary, string.Empty, null, ignoreSuffix ? ffmpeg.AV_DICT_IGNORE_SUFFIX : 0);

            while (item != null)
            {
                result[new IntPtr(item->key).Utf8ToString()] = new IntPtr(item->value).Utf8ToString();
                item = ffmpeg.av_dict_get(dictionary, string.Empty, item, ignoreSuffix ? ffmpeg.AV_DICT_IGNORE_SUFFIX : 0);
            }

            return result;
        }

        /// <summary>
        /// Gets the value with specified key.
        /// </summary>
        /// <param name="key">The dictionary key.</param>
        /// <param name="matchCase">If <see langword="true"/> matches case.</param>
        /// <returns>The value with specified key. If the key not exist, returns <see langword="null"/>.</returns>
        public string? Get(string key, bool matchCase = true)
        {
            var ptr = ffmpeg.av_dict_get(Pointer, key, null, matchCase ? ffmpeg.AV_DICT_MATCH_CASE : 0);
            return ptr != null ? new IntPtr(ptr).Utf8ToString() : null;
        }

        /// <summary>
        /// Sets the value for the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public void Set(string key, string? value)
        {
            var ptr = Pointer;
            ffmpeg.av_dict_set(&ptr, key, value, 0);
            UpdatePointer(ptr);
        }

        /// <summary>
        /// Copies items from specified dictionary to this <see cref="FFDictionary"/>.
        /// </summary>
        /// <param name="dictionary">The dictionary to copy.</param>
        public void Copy(Dictionary<string, string> dictionary)
        {
            foreach (var item in dictionary)
            {
                this[item.Key] = item.Value;
            }
        }

        /// <summary>
        /// Updates the pointer to the dictionary.
        /// </summary>
        /// <param name="pointer">The pointer to the <see cref="AVDictionary"/>.</param>
        internal void Update(AVDictionary* pointer) => UpdatePointer(pointer);

        /// <inheritdoc/>
        protected override void OnDisposing()
        {
            if (_requireDisposing && Pointer != null && Count > 0)
            {
                var ptr = Pointer;
                ffmpeg.av_dict_free(&ptr);
            }
        }
    }
}