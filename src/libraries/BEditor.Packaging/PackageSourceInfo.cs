// PackageSourceInfo.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace BEditor.Packaging
{
    /// <summary>
    /// Represents the package source infomation.
    /// </summary>
    [DataContract]
    public sealed class PackageSourceInfo : INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs _urlArgs = new(nameof(Url));
        private static readonly PropertyChangedEventArgs _nameArgs = new(nameof(Name));
        private string _name = string.Empty;
        private Uri? _url = null;

        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Gets or sets the url.
        /// </summary>
        [DataMember]
        public Uri? Url
        {
            get => _url;
            set => SetValue(value, ref _url, _urlArgs);
        }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        [DataMember]
        public string Name
        {
            get => _name;
            set => SetValue(value, ref _name, _nameArgs);
        }

        /// <summary>
        /// Creates the package source from the information in this package source.
        /// </summary>
        /// <param name="client">The http client.</param>
        /// <returns>Returns an instance of <see cref="PackageSource"/> on success, or <see langword="null"/> on failure.</returns>
        public async ValueTask<PackageSource?> ToRepositoryAsync(HttpClient client)
        {
            try
            {
                PackageSource? repos;

                if (Url is null)
                {
                    return null;
                }
                else if (Url.IsFile)
                {
                    repos = await JsonSerializer.DeserializeAsync<PackageSource>(File.OpenRead(Url.LocalPath), PackageFile._serializerOptions);
                }
                else
                {
                    repos = await JsonSerializer.DeserializeAsync<PackageSource>(await client.GetStreamAsync(Url), PackageFile._serializerOptions);
                }

                if (repos is not null)
                {
                    repos.Info = this;
                }

                return repos;
            }
            catch
            {
                return null;
            }
        }

        private void RaisePropertyChanged(PropertyChangedEventArgs args)
        {
            PropertyChanged?.Invoke(this, args);
        }

        private void SetValue<T1>(T1 src, ref T1 dst, PropertyChangedEventArgs args)
        {
            if (src == null || !src.Equals(dst))
            {
                dst = src;
                RaisePropertyChanged(args);
            }
        }
    }
}