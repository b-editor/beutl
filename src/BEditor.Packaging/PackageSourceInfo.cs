using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace BEditor.Packaging
{
    [DataContract]
    public sealed class PackageSourceInfo : INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs urlArgs = new(nameof(Url));
        private static readonly PropertyChangedEventArgs nameArgs = new(nameof(Name));
        private string _name = string.Empty;
        private Uri? _url = null;

        [DataMember]
        public Uri? Url
        {
            get => _url;
            set => SetValue(value, ref _url, urlArgs);
        }

        [DataMember]
        public string Name
        {
            get => _name;
            set => SetValue(value, ref _name, nameArgs);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public async ValueTask<PackageSource?> ToRepositoryAsync(HttpClient client)
        {
            try
            {
                PackageSource? repos;

                if (Url is null) return null;
                else if (Url.IsFile)
                {
                    repos = await JsonSerializer.DeserializeAsync<PackageSource>(File.OpenRead(Url.LocalPath), PackageFile._serializerOptions);
                }
                else
                {
                    repos = await JsonSerializer.DeserializeAsync<PackageSource>(await client.GetStreamAsync(Url), PackageFile._serializerOptions);
                }

                if (repos is not null) repos.Info = this;

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