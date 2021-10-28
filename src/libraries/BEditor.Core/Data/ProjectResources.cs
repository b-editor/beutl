// ProjectResources.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;

namespace BEditor.Data
{
    /// <summary>
    /// Manage the resources that the project is using.
    /// </summary>
    public sealed class ProjectResources
    {
        private readonly Dictionary<string, ResourceItem> _items = new();

        /// <summary>
        /// Finalizes an instance of the <see cref="ProjectResources"/> class.
        /// </summary>
        ~ProjectResources()
        {
            Release();
        }

        /// <summary>
        /// Gets all registered resources.
        /// </summary>
        public IReadOnlyDictionary<string, ResourceItem> Items => _items;

        /// <summary>
        /// Register the resources to be used.
        /// </summary>
        /// <param name="item">A resource to register.</param>
        /// <returns>Returns a registered resource.</returns>
        public ResourceItem RegisterResource(ResourceItem item)
        {
            item.Parent = this;
            if (_items.TryGetValue(item.Key, out var result))
            {
                return result;
            }
            else
            {
                _items.TryAdd(item.Key, item);
                return item;
            }
        }

        /// <summary>
        /// Releases the registered resources.
        /// </summary>
        public void Release()
        {
            foreach (var item in _items)
            {
                if (item.Value.Value is IDisposable disposable)
                    disposable.Dispose();

                item.Value.Value = null;
            }

            _items.Clear();
        }

        /// <summary>
        /// Gets a registered resource.
        /// </summary>
        /// <param name="key">The key of a resource to get.</param>
        /// <returns>Returns a resource that matches the key. If not found, <see langword="null"/> will be returned.</returns>
        public ResourceItem? GetItem(string key)
        {
            if (_items.TryGetValue(key, out var item))
            {
                return item;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Removes a resource with the specified key.
        /// </summary>
        /// <param name="key">The key of a resource to be removed.</param>
        /// <returns>Returns a resource if it has been removed, otherwise returns <see langword="null"/>.</returns>
        public ResourceItem? RemoveItem(string key)
        {
            if (_items.Remove(key, out var item))
            {
                if (item.Value is IDisposable disposable)
                    disposable.Dispose();

                item.Value = null;

                return item;
            }
            else
            {
                return null;
            }
        }
    }
}