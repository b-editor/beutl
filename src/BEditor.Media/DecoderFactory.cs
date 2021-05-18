using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media.Decoding;
using BEditor.Media.Encoding;

namespace BEditor.Media
{
    internal static class DecoderFactory
    {
        public static List<IDecoderBuilder> Builder { get; } = new();

        public static MediaFile? Open(string file, MediaOptions options)
        {
            var container = GuessDecoders(file).FirstOrDefault()?.Open(file, options);

            return container is null ? null : new MediaFile(container);
        }

        public static IDecoderBuilder[] GuessDecoders(string file)
        {
            return Builder.Where(i => i.IsSupported(file)).ToArray();
        }

        public static void Register(IDecoderBuilder builder)
        {
            Builder.Add(builder);
        }
    }
}