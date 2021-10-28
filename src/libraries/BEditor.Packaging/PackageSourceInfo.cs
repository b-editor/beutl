// PackageSourceInfo.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using BEditor.Data;

namespace BEditor.Packaging
{
    /// <summary>
    /// Represents the package source infomation.
    /// </summary>
    public sealed class PackageSourceInfo : EditingObject
    {
        /// <summary>
        /// Defines the <see cref="Url"/> property.
        /// </summary>
        public static readonly EditingProperty<Uri?> UrlProperty
            = EditingProperty.Register<Uri?, PackageSourceInfo>(
                "url,Url",
                EditingPropertyOptions<Uri?>.Create().Serialize(
                    (writer, obj) => writer.WriteStringValue(obj?.ToString() ?? string.Empty),
                    ctx => ctx.Element.GetString() is string value ? new Uri(value) : null));

        /// <summary>
        /// Defines the <see cref="Name"/> property.
        /// </summary>
        public static new readonly EditingProperty<string> NameProperty
            = EditingProperty.Register<string, PackageSourceInfo>(
                "name,Name",
                EditingPropertyOptions<string>.Create().DefaultValue(string.Empty)!.Serialize()!);

        /// <summary>
        /// Gets or sets the url.
        /// </summary>
        [JsonPropertyName("Url")]
        public Uri? Url
        {
            get => GetValue(UrlProperty);
            set => SetValue(UrlProperty, value);
        }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        [JsonPropertyName("Name")]
        public override string Name
        {
            get => GetValue(NameProperty);
            set => SetValue(NameProperty, value);
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
    }
}