// ResourceItem.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a resource used by the project.
    /// </summary>
    public class ResourceItem
    {
        private readonly Func<object> _create;
        private readonly HashSet<object> _refs = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceItem"/> class.
        /// </summary>
        /// <param name="key">The key of the resource.</param>
        /// <param name="create">Create a value.</param>
        public ResourceItem(string key, Func<object> create)
        {
            Key = key;
            _create = create;
        }

        /// <summary>
        /// Gets the key of this resource.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Gets the value.
        /// </summary>
        public object? Value { get; internal set; }

        internal ProjectResources? Parent { get; set; }

        /// <summary>
        /// References this resource.
        /// </summary>
        /// <param name="obj">The object to reference.</param>
        /// <returns>Returns the <see cref="IDisposable"/> from which the reference will be removed.</returns>
        public IDisposable MakeReference(object obj)
        {
            _refs.Add(obj);
            return Disposable.Create(obj, obj =>
            {
                _refs.Remove(obj);

                if (_refs.Count == 0)
                {
                    Parent?.RemoveItem(Key);
                }
            });
        }

        /// <summary>
        /// Builds this resource.
        /// </summary>
        public void Build()
        {
            Value ??= _create.Invoke();
        }
    }
}
