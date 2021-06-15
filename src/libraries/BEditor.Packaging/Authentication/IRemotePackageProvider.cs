// IRemotePackageProvider.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BEditor.Packaging
{
    /// <summary>
    /// Provides the ability to upload, download, and other packages.
    /// </summary>
    public interface IRemotePackageProvider
    {
        /// <summary>
        /// Upload the package file.
        /// </summary>
        /// <param name="auth">The user who uploads the file.</param>
        /// <param name="filename">The file to upload.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public async ValueTask UploadAsync(Authentication auth, string filename)
        {
            if (!File.Exists(filename)) throw new FileNotFoundException(null, filename);
            await using var stream = new FileStream(filename, FileMode.Open);
            await UploadAsync(auth, stream);
        }

        /// <summary>
        /// Upload the package file from stream.
        /// </summary>
        /// <param name="auth">The user who uploads the file.</param>
        /// <param name="stream">The stream of file to upload.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public ValueTask UploadAsync(Authentication auth, Stream stream);

        /// <summary>
        /// Gets the packages that has been uploaded.
        /// </summary>
        /// <param name="auth">The user who uploaded the package.</param>
        /// <returns>Returns the packages that has been uploaded.</returns>
        public ValueTask<IEnumerable<Package>> GetPackagesAsync(Authentication auth);
    }
}