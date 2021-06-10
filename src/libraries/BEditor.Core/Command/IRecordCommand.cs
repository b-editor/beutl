// IRecordCommand.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Resources;

namespace BEditor.Command
{
    /// <summary>
    /// Represents the action of executing, undoing, or redoing.
    /// </summary>
    public interface IRecordCommand
    {
        /// <summary>
        /// Gets the name of this <see cref="IRecordCommand"/>.
        /// </summary>
        public string Name => Strings.UnknownCommand;

        /// <summary>
        /// Execute the operation.
        /// </summary>
        public void Do();

        /// <summary>
        /// Undo the operation.
        /// </summary>
        public void Undo();

        /// <summary>
        /// Redo the operation.
        /// </summary>
        public void Redo();
    }
}