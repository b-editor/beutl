using System.Collections.Generic;
using System.Linq;

using BEditor.Media.Encoding;

namespace BEditor.Media
{
    internal static class EncoderFactory
    {
        public static List<IEncoderBuilder> Builder { get; } = new();

        public static IOutputContainer? Create(string file)
        {
            return GuessEncoders(file).FirstOrDefault()?.Create(file);
        }

        public static IEncoderBuilder[] GuessEncoders(string file)
        {
            return Builder.Where(i => i.IsSupported(file)).ToArray();
        }

        public static void Register(IEncoderBuilder builder)
        {
            Builder.Add(builder);
        }
    }
}
