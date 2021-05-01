using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Media.Encoder;
using BEditor.Models;

using Reactive.Bindings;

namespace BEditor.ViewModels
{
    public class VideoOutputViewModel
    {
        public VideoOutputViewModel()
        {
            SelectedScene.Value = Project.PreviewScene;
            Codecs = Enum.GetValues<VideoCodec>().Select(i => new CodecTupple(i.ToString("g"), i)).ToArray();
        }

        public MainWindowViewModel MainWindow => MainWindowViewModel.Current;
        public Project Project => AppModel.Current.Project;
        public ReactivePropertySlim<Scene> SelectedScene { get; } = new();
        public CodecTupple[] Codecs { get; }
        public ReactivePropertySlim<CodecTupple> SelectedCodec { get; } = new(new CodecTupple("Default", VideoCodec.Default));

        public record CodecTupple(string Name, VideoCodec Codec);
    }
}
