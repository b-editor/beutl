namespace BEditor.Graphics.Veldrid
{
    public class CubeImpl : DrawableImpl
    {
        public CubeImpl(float width, float height, float depth)
        {
            Width = width;
            Height = height;
            Depth = depth;

            width /= 2;
            height /= 2;
            depth /= 2;

            Vertices = new float[]
            {
                // Top
                -width,  height, -depth,
                 width,  height, -depth,
                 width,  height,  depth,
                -width,  height,  depth,
                // Bottom
                -width, -height,  depth,
                 width, -height,  depth,
                 width, -height, -depth,
                -width, -height, -depth,
                // Left
                -width,  height, -depth,
                -width,  height,  depth,
                -width, -height,  depth,
                -width, -height, -depth,
                // Right
                 width,  height,  depth,
                 width,  height, -depth,
                 width, -height, -depth,
                 width, -height,  depth,
                // Back
                 width,  height, -depth,
                -width,  height, -depth,
                -width, -height, -depth,
                 width, -height, -depth,
                // Front
                -width,  height,  depth,
                 width,  height,  depth,
                 width, -height,  depth,
                -width, -height,  depth,
            };
        }

        public float Width { get; }

        public float Height { get; }

        public float Depth { get; }

        public float[] Vertices { get; }

        public static ushort[] GetCubeIndices()
        {
            ushort[] indices =
            {
                00, 01, 02,  00, 02, 03,
                04, 05, 06,  04, 06, 07,
                08, 09, 10,  08, 10, 11,
                12, 13, 14,  12, 14, 15,
                16, 17, 18,  16, 18, 19,
                20, 21, 22,  20, 22, 23,
            };

            return indices;
        }
    }
}