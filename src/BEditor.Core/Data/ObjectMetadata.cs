using System;
using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace BEditor.Core.Data
{
#pragma warning disable CS1591
    public record ObjectMetadata(string Name, Expression<Func<ObjectElement>> Create)
    {
        private Func<ObjectElement>? _Func;

        public Type Type => ((NewExpression)Create.Body).Type;
        public Func<ObjectElement> CreateFunc => _Func ??= Create.Compile();

        public static ObservableCollection<ObjectMetadata> LoadedObjects { get; } = new()
        {
            ClipType.VideoMetadata,
            ClipType.ImageMetadata,
            ClipType.FigureMetadata,
            ClipType.PolygonMetadata,
            ClipType.RoundRectMetadata,
            ClipType.TextMetadata,
            ClipType.CameraMetadata,
            ClipType.GL3DObjectMetadata,
            ClipType.SceneMetadata
        };
    }
#pragma warning restore CS1591
}
