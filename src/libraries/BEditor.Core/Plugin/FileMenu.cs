// FileMenu.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using BEditor.Data;

namespace BEditor.Plugin
{
    /// <summary>
    /// Represents the custom file menu.
    /// </summary>
    public class FileMenu : BaseMenu<string>
    {
        /// <summary>
        /// Defines the <see cref="SupportedExtensions"/> property.
        /// </summary>
        public static readonly EditingProperty<IReadOnlyList<string>> SupportedExtensionsProperty =
            EditingProperty.Register<IReadOnlyList<string>, FileMenu>(
                nameof(SupportedExtensions),
                EditingPropertyOptions<IReadOnlyList<string>>.Create().DefaultValue(new string[] { "*.*" }).Notify(true));

        /// <summary>
        /// Gets or sets the supported file extensions.
        /// </summary>
        public IReadOnlyList<string> SupportedExtensions
        {
            get => GetValue(SupportedExtensionsProperty);
            set => SetValue(SupportedExtensionsProperty, value);
        }

        /// <summary>
        /// Check if the file matches.
        /// </summary>
        /// <param name="file">The name of file.</param>
        /// <returns>true if the regular expression finds a match; otherwise, false.</returns>
        public bool IsMatch(string file)
        {
            foreach (var item in SupportedExtensions
                .Select(i => Regex.Replace(i, ".", m =>
            {
                var s = m.Value;
                if (s.Equals("?"))
                {
                    return ".";
                }
                else if (s.Equals("*"))
                {
                    return ".*";
                }
                else
                {
                    return Regex.Escape(s);
                }
            })))
            {
                if (Regex.IsMatch(file, item))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
