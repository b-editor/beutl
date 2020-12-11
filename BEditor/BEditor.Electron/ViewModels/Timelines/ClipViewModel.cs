using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Models;
using BEditor.Views;

using Reactive.Bindings;

namespace BEditor.ViewModels.Timelines
{
    public class ClipViewModel : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs cursorArgs = new(nameof(Cursor));
        private static readonly PropertyChangedEventArgs rowArgs = new(nameof(Row));
        private static readonly PropertyChangedEventArgs marginLeftArgs = new(nameof(MarginLeft));
        private static readonly PropertyChangedEventArgs widthArgs = new(nameof(Width));
        private Cursors cursor;
        private int row;
        private double marginLeft;
        private double width;

        public ClipViewModel(ClipData clip)
        {
            Clip = clip;
            Row = clip.Layer;
            Width = clip.Parent.ToPixel(clip.Length);
            MarginLeft = clip.Parent.ToPixel(clip.Start);
        }

        private TimelineViewModel TimeLineViewModel => Scene.GetCreateTimeLineViewModel();
        public Scene Scene => Clip.Parent;
        public ClipData Clip { get; }
        public Cursors Cursor
        { 
            get => cursor;
            set => SetValue(value, ref cursor, cursorArgs);
        }
        public int Row
        {
            get => row;
            set => SetValue(value, ref row, rowArgs);
        }
        public double Width
        {
            get => width;
            set => SetValue(value, ref width, widthArgs);
        }
        public double MarginLeft
        {
            get => marginLeft;
            set => SetValue(value, ref marginLeft, marginLeftArgs);
        }
    }
}
