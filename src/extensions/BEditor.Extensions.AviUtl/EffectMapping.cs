using System.Collections.Generic;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Extensions.AviUtl.Effects;

namespace BEditor.Extensions.AviUtl
{
    public static class EffectMapping
    {
        private static readonly List<IMappedEffect> _effects = new()
        {
            new ColorToneCorrection(),
            new Clipping(),
            new Blur(),
            new EdgeBlur(),
            new Mosaic(),
            new Luminescence(),
            new Flash(),
            new DiffuseLight(),
            new Glow(),
            new ChromaKey(),
            new ColorKey(),
            new LuminanceKey(),
            new Light(),
            new Shadow(),
            new Border(),
            new TotuEdge(),
            new EdgeExtract(),
            new Sharp(),
            new Fade(),
            new Wipe(),
            new Mask(),
            new DiagonalClipping(),
            new RadiantBlur(),
            new DirectionalBlur(),
            new LensBlur(),
            new MotionBlur(),
            new Vibration(),
            new Mirror(),
            new Raster(),
            new Ripple(),
            new ImageLoop(),
            new PolarCoordinatesTransform(),
            new DisplacementMap(),
            new Noise(),
            new ColorShift(),
            new Monochromatic(),
            new Gradient(),
            new ExtendedColorSetting(),
            new SpecificGamutConversion(),
            new ObjectSplit(),
        };

        public static void Apply(this ObjectTable table, ref Image<BGRA32> image, string name, object[] param)
        {
            IMappedEffect? effect = null;
            foreach (var item in _effects)
            {
                if (item.Name == name)
                {
                    effect = item;
                }
            }

            if (effect is null)
            {
                // obj.effect()の場合があるので
                // throw new Exception($"Not found {name}.");
                return;
            }

            effect.Apply(ref image, table, ToDictionary(param));
        }

        private static Dictionary<string, object> ToDictionary(object[] param)
        {
            var dict = new Dictionary<string, object>();
            for (var i = 0; i < param.Length; i += 2)
            {
                dict.Add((string)param[i], param[i + 1]);
            }
            return dict;
        }
    }
}