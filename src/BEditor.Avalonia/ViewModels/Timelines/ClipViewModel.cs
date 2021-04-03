using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

using BEditor.Data;
using BEditor.Extensions;
using BEditor.Models;

using Reactive.Bindings;

namespace BEditor.ViewModels.Timelines
{
    public class ClipViewModel
    {
        public ClipViewModel(ClipElement clip)
        {
            static CustomClipUIAttribute GetAtt(ObjectElement self)
            {
                var type = self.GetType();
                var attribute = Attribute.GetCustomAttribute(type, typeof(CustomClipUIAttribute));

                if (attribute is CustomClipUIAttribute uIAttribute) return uIAttribute;
                else return new();
            }

            ClipElement = clip;
            WidthProperty.Value = Scene.ToPixel(ClipElement.Length);
            MarginProperty.Value = new Thickness(Scene.ToPixel(ClipElement.Start), 1, 0, 0);
            Row = clip.Layer;

            if (clip.Effect[0] is ObjectElement @object)
            {
                var color = GetAtt(@object).GetColor;
                ClipColor.Value = new SolidColorBrush(new Color(255, color.R, color.G, color.B));
                ClipText.Value = @object.Name;
            }
        }

        public Scene Scene => ClipElement.Parent;

        //private TimelineViewModel TimelineViewModel => Scene.GetCreateTimelineViewModel();

        public ClipElement ClipElement { get; }

        public int Row { get; set; }

        public ReactivePropertySlim<string> ClipText { get; set; } = new();

        public ReactivePropertySlim<Brush> ClipColor { get; set; } = new();

        public static double TrackHeight => ConstantSettings.ClipHeight;

        public ReactivePropertySlim<double> WidthProperty { get; } = new();

        public ReactivePropertySlim<Thickness> MarginProperty { get; } = new();

        public double MarginLeft
        {
            get => MarginProperty.Value.Left;
            set
            {
                var tmp = MarginProperty.Value;
                MarginProperty.Value = new(value, tmp.Top, tmp.Right, tmp.Bottom);
            }
        }

        public ReactivePropertySlim<bool> IsExpanded { get; } = new();

        public ReactivePropertySlim<StandardCursorType> ClipCursor { get; } = new();
    }
}
