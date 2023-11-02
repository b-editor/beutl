// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;

namespace System.Runtime.Loader
{
    internal partial struct LibraryNameVariation
    {
        private static readonly string s_libraryNamePrefix = "lib";
        private static readonly string s_libraryNameSuffix;

        static LibraryNameVariation()
        {
            if(OperatingSystem.IsMacOS()
                || OperatingSystem.IsMacCatalyst()
                || OperatingSystem.IsIOS()
                || OperatingSystem.IsTvOS())
            {
                s_libraryNameSuffix = ".dylib";
            }
            else if(OperatingSystem.IsWindows())
            {
                s_libraryNameSuffix = ".dll";
                s_libraryNamePrefix = "";
            }
            else
            {
                s_libraryNameSuffix = ".so";
            }
        }

        internal static IEnumerable<LibraryNameVariation> DetermineLibraryNameVariations(string libName, bool isRelativePath)
        {
            // This is a copy of the logic in DetermineLibNameVariations in dllimport.cpp in CoreCLR

            if (!isRelativePath)
            {
                yield return new LibraryNameVariation(string.Empty, string.Empty);
            }
            else
            {
                bool containsSuffix = false;
                int indexOfSuffix = libName.IndexOf(s_libraryNameSuffix, StringComparison.OrdinalIgnoreCase);
                if (indexOfSuffix >= 0)
                {
                    indexOfSuffix += s_libraryNameSuffix.Length;
                    containsSuffix = indexOfSuffix == libName.Length || libName[indexOfSuffix] == '.';
                }

                bool containsDelim = libName.Contains(Path.DirectorySeparatorChar);

                if (containsSuffix)
                {
                    yield return new LibraryNameVariation(string.Empty, string.Empty);
                    if (!containsDelim)
                    {
                        yield return new LibraryNameVariation(s_libraryNamePrefix, string.Empty);
                    }
                    yield return new LibraryNameVariation(string.Empty, s_libraryNameSuffix);
                    if (!containsDelim)
                    {
                        yield return new LibraryNameVariation(s_libraryNamePrefix, s_libraryNameSuffix);
                    }
                }
                else
                {
                    yield return new LibraryNameVariation(string.Empty, s_libraryNameSuffix);
                    if (!containsDelim)
                    {
                        yield return new LibraryNameVariation(s_libraryNamePrefix, s_libraryNameSuffix);
                    }
                    yield return new LibraryNameVariation(string.Empty, string.Empty);
                    if (!containsDelim)
                    {
                        yield return new LibraryNameVariation(s_libraryNamePrefix, string.Empty);
                    }
                }
            }
        }
    }
}
