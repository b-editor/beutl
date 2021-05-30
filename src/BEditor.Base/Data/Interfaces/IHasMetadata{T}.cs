// IHasMetadata{T}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data
{
    /// <summary>
    /// Represents an object with metadata.
    /// </summary>
    /// <typeparam name="T">The type of metadata.</typeparam>
    public interface IHasMetadata<T> : IHasMetadata
        where T : class, IEditingPropertyInitializer
    {
        /// <inheritdoc/>
        object? IHasMetadata.Metadata
        {
            get => Metadata;
            set => Metadata = value as T;
        }

        /// <inheritdoc cref="IHasMetadata.Metadata"/>
        public new T? Metadata { get; set; }
    }
}