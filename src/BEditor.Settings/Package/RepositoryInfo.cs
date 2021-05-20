using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace BEditor.Package
{
    [DataContract]
    public sealed class RepositoryInfo : INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs urlArgs = new(nameof(Url));
        private static readonly PropertyChangedEventArgs nameArgs = new(nameof(Name));
        private string name = string.Empty;
        private Uri? url = null;

        [DataMember]
        public Uri? Url
        {
            get => url;
            set => SetValue(value, ref url, urlArgs);
        }

        [DataMember]
        public string Name
        {
            get => name;
            set => SetValue(value, ref name, nameArgs);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public async ValueTask<Repository?> ToRepositoryAsync(HttpClient client)
        {
            if (Url is null) return null;
            else if (Url.IsFile)
            {
                return await JsonSerializer.DeserializeAsync<Repository>(File.OpenRead(Url.LocalPath));
            }
            else 
            {
                return await JsonSerializer.DeserializeAsync<Repository>(await client.GetStreamAsync(Url));
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