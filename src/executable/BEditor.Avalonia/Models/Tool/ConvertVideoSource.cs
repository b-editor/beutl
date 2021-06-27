using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media.Decoding;

namespace BEditor.Models.Tool
{
    public record ConvertVideoSource(VideoStreamInfo VideoInfo, AudioStreamInfo AudioInfo, string File);
}