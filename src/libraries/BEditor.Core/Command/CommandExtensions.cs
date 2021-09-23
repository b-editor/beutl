// CommandExtensions.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;

namespace BEditor
{
    /// <summary>
    /// Represents a class that provides an extension method.
    /// </summary>
    public static class CommandExtensions
    {
        /// <summary>
        /// Execute IRecordCommand.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        public static void Execute(this IRecordCommand command)
        {
            CommandManager.Default.Do(command);
        }

        /// <summary>
        /// Execute IRecordCommand with CommandManager specified.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        /// <param name="manager">The command manager for executing commands.</param>
        public static void Execute(this IRecordCommand command, CommandManager manager)
        {
            manager.Do(command);
        }

        /// <summary>
        /// Combine the two commands.
        /// </summary>
        /// <param name="first">The first command.</param>
        /// <param name="second">The second command.</param>
        /// <returns>The command.</returns>
        public static IRecordCommand Combine(this IRecordCommand first, IRecordCommand second)
        {
            return RecordCommand.Create<(IRecordCommand First, IRecordCommand Second)>(
                (first, second),
                item =>
                {
                    item.First.Do();
                    item.Second.Do();
                },
                item =>
                {
                    item.First.Undo();
                    item.Second.Undo();
                },
                item =>
                {
                    item.First.Redo();
                    item.Second.Redo();
                },
                item => $"{item.First.Name} {item.Second.Name}");
        }
    }
}