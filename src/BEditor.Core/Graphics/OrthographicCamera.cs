
using OpenTK.Mathematics;

namespace BEditor.Core.Graphics
{
    public class OrthographicCamera : Camera
    {
        public OrthographicCamera(Vector3 position, float width, float height) : base(position)
        {
            Width = width;
            Height = height;
        }

        public float Width { get; set; }
        public float Height { get; set; }

        public override Matrix4 GetProjectionMatrix()
        {
            return Matrix4.CreateOrthographic(Width, Height, Near, Far);
        }
    }
}
