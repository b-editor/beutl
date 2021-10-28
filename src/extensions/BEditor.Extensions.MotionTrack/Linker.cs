using System.Collections.Generic;
using System.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Extensions.MotionTrack.Resources;
using BEditor.Graphics;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Extensions.MotionTrack
{
    public sealed class Linker : ImageEffect
    {
        public static readonly EditingProperty<ValueProperty> TargetIdProperty
            = EditingProperty.Register<ValueProperty, Linker>(
                nameof(TargetId),
                EditingPropertyOptions<ValueProperty>.Create(new ValuePropertyMetadata(Strings.ID, 0)).Serialize());

        private TrackingService _trackingService;

        public Linker()
        {
            _trackingService = ServicesLocator.Current.Provider.GetRequiredService<TrackingService>();
        }

        public override string Name => Strings.Linker;

        public ValueProperty TargetId => GetValue(TargetIdProperty);

        public override void Apply(EffectApplyArgs<IEnumerable<Texture>> args)
        {
            var roi = _trackingService[(int)TargetId.Value];
            args.Value = args.Value.Select(texture =>
            {
                var scene = this.GetParent<Scene>();
                if (roi.Width == 0 || roi.Height == 0 || scene == null) return texture;

                var pos = texture.Transform.Relative;
                pos.X += roi.X + roi.Width / 2 - scene.Width / 2;
                pos.Y -= roi.Y + roi.Height / 2 - scene.Height / 2;
                var transform = texture.Transform;
                transform.Relative = pos;
                texture.Transform = transform;

                return texture;
            });
        }

        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return TargetId;
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            _trackingService = ServicesLocator.Current.Provider.GetRequiredService<TrackingService>();
        }
    }
}