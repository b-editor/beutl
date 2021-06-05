// PluginTask.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BEditor
{
    /// <summary>
    /// Represents the task to be executed after the plugin is loaded.
    /// </summary>
    public struct PluginTask : IEquatable<PluginTask>
    {
        internal readonly Func<IProgressDialog, ValueTask>? _task;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginTask"/> struct.
        /// </summary>
        /// <param name="task">The function to run.</param>
        /// <param name="name">The name of the <see cref="PluginTask"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="task"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
        public PluginTask(Func<IProgressDialog, ValueTask> task, string name)
        {
            _task = task ?? throw new ArgumentNullException(nameof(task));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            IsExecuted = false;
        }

        /// <summary>
        /// Gets the name of this <see cref="PluginTask"/>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the value of whether the task has been executed.
        /// </summary>
        public bool IsExecuted { get; private set; }

        /// <summary>
        /// Compares two <see cref="PluginTask"/> structures. The result specifies whether the two <see cref="PluginTask"/> structures are equal.
        /// </summary>
        /// <param name="left">A <see cref="PluginTask"/> to compare.</param>
        /// <param name="right">A <see cref="PluginTask"/> to compare.</param>
        /// <returns>true if the left and right <see cref="PluginTask"/> structures are equal; otherwise, false.</returns>
        public static bool operator ==(PluginTask left, PluginTask right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether the coordinates of the specified points are not equal.
        /// </summary>
        /// <param name="left">A <see cref="PluginTask"/> to compare.</param>
        /// <param name="right">A <see cref="PluginTask"/> to compare.</param>
        /// <returns>true if the left and right <see cref="PluginTask"/> structures differ; otherwise, false.</returns>
        public static bool operator !=(PluginTask left, PluginTask right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is PluginTask task && Equals(task);
        }

        /// <inheritdoc/>
        public bool Equals(PluginTask other)
        {
            return EqualityComparer<Func<IProgressDialog, ValueTask>?>.Default.Equals(_task, other._task) &&
                   Name == other.Name;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(_task, Name);
        }

        /// <summary>
        /// Run the task.
        /// </summary>
        /// <param name="progress">The progress of this <see cref="PluginTask"/>.</param>
        /// <returns>A <see cref="ValueTask"/> representing the result of the asynchronous operation.</returns>
        public async ValueTask RunTask(IProgressDialog progress)
        {
            if (!IsExecuted && _task is not null)
            {
                await _task(progress);
            }

            IsExecuted = true;
        }
    }
}