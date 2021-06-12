// IPackageUploader.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.IO;
using System.Threading.Tasks;

namespace BEditor.Packaging
{
    /// <summary>
    /// Provides the ability to upload packages.
    /// </summary>
    public interface IPackageUploader
    {
        /// <summary>
        /// Upload the package file.
        /// </summary>
        /// <param name="user">The user who uploads the file.</param>
        /// <param name="filename">The file to upload.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public async ValueTask UploadAsync(Authentication user, string filename)
        {
            if (!File.Exists(filename)) throw new FileNotFoundException(null, filename);
            await using var stream = new FileStream(filename, FileMode.Open);
            await UploadAsync(user, stream);
        }

        /// <summary>
        /// Upload the package file from stream.
        /// </summary>
        /// <param name="user">The user who uploads the file.</param>
        /// <param name="stream">The stream of file to upload.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public ValueTask UploadAsync(Authentication user, Stream stream);
    }
}