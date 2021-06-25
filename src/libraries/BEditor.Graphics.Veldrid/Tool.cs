using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;

using Veldrid;

namespace BEditor.Graphics.Veldrid
{
    internal static class Tool
    {
        public static RgbaFloat ToFloat(this Color color)
        {
            return new(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f,
                color.A / 255f);
        }

        public static void Dispose<T>(this IEnumerable<T> disposables)
            where T : IDisposable
        {
            foreach (var item in disposables)
            {
                item.Dispose();
            }
        }

        public static void Dispose<T>(this T[] disposables)
            where T : IDisposable
        {
            foreach (var item in disposables)
            {
                item.Dispose();
            }
        }

        public static global::Veldrid.ComparisonKind ToVeldrid(this ComparisonKind kind)
        {
            return kind switch
            {
                ComparisonKind.Never => global::Veldrid.ComparisonKind.Never,
                ComparisonKind.Less => global::Veldrid.ComparisonKind.Less,
                ComparisonKind.Equal => global::Veldrid.ComparisonKind.Equal,
                ComparisonKind.LessEqual => global::Veldrid.ComparisonKind.LessEqual,
                ComparisonKind.Greater => global::Veldrid.ComparisonKind.Greater,
                ComparisonKind.NotEqual => global::Veldrid.ComparisonKind.NotEqual,
                ComparisonKind.GreaterEqual => global::Veldrid.ComparisonKind.GreaterEqual,
                ComparisonKind.Always => global::Veldrid.ComparisonKind.Always,
                _ => global::Veldrid.ComparisonKind.Less,
            };
        }

        public static global::Veldrid.StencilOperation ToVeldrid(this StencilOperation op)
        {
            return op switch
            {
                StencilOperation.Keep => global::Veldrid.StencilOperation.Keep,
                StencilOperation.Zero => global::Veldrid.StencilOperation.Zero,
                StencilOperation.Replace => global::Veldrid.StencilOperation.Replace,
                StencilOperation.IncrementAndClamp => global::Veldrid.StencilOperation.IncrementAndClamp,
                StencilOperation.DecrementAndClamp => global::Veldrid.StencilOperation.DecrementAndClamp,
                StencilOperation.Invert => global::Veldrid.StencilOperation.Invert,
                StencilOperation.IncrementAndWrap => global::Veldrid.StencilOperation.IncrementAndWrap,
                StencilOperation.DecrementAndWrap => global::Veldrid.StencilOperation.DecrementAndWrap,
                _ => global::Veldrid.StencilOperation.Keep,
            };
        }

        public static DepthStencilStateDescription ToVeldrid(this DepthStencilState state)
        {
            return new(
                state.DepthTestEnabled, state.DepthWriteEnabled, ToVeldrid(state.DepthComparison),
                state.StencilTestEnabled, state.StencilFront.ToVeldrid(), state.StencilBack.ToVeldrid(), state.StencilReadMask, state.StencilWriteMask, state.StencilReference);
        }

        public static StencilBehaviorDescription ToVeldrid(this StencilBehavior state)
        {
            return new(state.Fail.ToVeldrid(), state.Pass.ToVeldrid(), state.DepthFail.ToVeldrid(), state.Comparison.ToVeldrid());
        }

        public static RasterizerStateDescription ToVeldrid(this RasterizerState state)
        {
            return new(state.CullMode.ToVeldrid(), state.FillMode.ToVeldrid(), state.FrontFace.ToVeldrid(), state.DepthClipEnabled, state.ScissorTestEnabled);
        }

        public static global::Veldrid.FaceCullMode ToVeldrid(this FaceCullMode mode)
        {
            return mode switch
            {
                FaceCullMode.Back => global::Veldrid.FaceCullMode.Back,
                FaceCullMode.Front => global::Veldrid.FaceCullMode.Front,
                FaceCullMode.None => global::Veldrid.FaceCullMode.None,
                _ => global::Veldrid.FaceCullMode.None,
            };
        }

        public static global::Veldrid.PolygonFillMode ToVeldrid(this PolygonFillMode mode)
        {
            return mode switch
            {
                PolygonFillMode.Solid => global::Veldrid.PolygonFillMode.Solid,
                PolygonFillMode.Wireframe => global::Veldrid.PolygonFillMode.Wireframe,
                _ => global::Veldrid.PolygonFillMode.Solid,
            };
        }

        public static global::Veldrid.FrontFace ToVeldrid(this FrontFace mode)
        {
            return mode switch
            {
                FrontFace.Clockwise => global::Veldrid.FrontFace.Clockwise,
                FrontFace.CounterClockwise => global::Veldrid.FrontFace.CounterClockwise,
                _ => global::Veldrid.FrontFace.Clockwise,
            };
        }

        public static BlendStateDescription ToBlendStateDescription(this BlendMode mode)
        {
            return mode switch
            {
                BlendMode.AlphaBlend => new(default,
                new BlendAttachmentDescription(
                    true,
                    sourceColorFactor: BlendFactor.SourceAlpha,
                    destinationColorFactor: BlendFactor.InverseSourceAlpha,
                    colorFunction: BlendFunction.Add,
                    sourceAlphaFactor: BlendFactor.One,
                    destinationAlphaFactor: BlendFactor.InverseSourceAlpha,
                    alphaFunction: BlendFunction.Add)),

                BlendMode.Additive => new(default,
                new BlendAttachmentDescription(
                    true,
                    sourceColorFactor: BlendFactor.SourceAlpha,
                    destinationColorFactor: BlendFactor.One,
                    colorFunction: BlendFunction.Add,
                    sourceAlphaFactor: BlendFactor.SourceAlpha,
                    destinationAlphaFactor: BlendFactor.One,
                    alphaFunction: BlendFunction.Add)),

                BlendMode.Subtract => new(default,
                new BlendAttachmentDescription(
                    true,
                    sourceColorFactor: BlendFactor.SourceAlpha,
                    destinationColorFactor: BlendFactor.One,
                    colorFunction: BlendFunction.ReverseSubtract,
                    sourceAlphaFactor: BlendFactor.SourceAlpha,
                    destinationAlphaFactor: BlendFactor.One,
                    alphaFunction: BlendFunction.ReverseSubtract)),

                BlendMode.Multiplication => new(default,
                new BlendAttachmentDescription(
                    true,
                    sourceColorFactor: BlendFactor.Zero,
                    destinationColorFactor: BlendFactor.SourceColor,
                    colorFunction: BlendFunction.Add,
                    sourceAlphaFactor: BlendFactor.Zero,
                    destinationAlphaFactor: BlendFactor.SourceAlpha,
                    alphaFunction: BlendFunction.Add)),

                _ => BlendStateDescription.SingleAlphaBlend,
            };
        }

        public static BallImpl ToImpl(this Ball ball)
        {
            return new(ball.RadiusX, ball.RadiusY, ball.RadiusZ);
        }

        public static CubeImpl ToImpl(this Cube cube)
        {
            return new(cube.Width, cube.Height, cube.Depth);
        }

        public static float[] Vertices(this Line line)
        {
            return new float[]
            {
                line.Start.X, line.Start.Y, line.Start.Z,
                line.End.X, line.End.Y, line.End.Z,
            };
        }

        public static TextureDescription ToTextureDescription(this Texture texture)
        {
            return TextureDescription.Texture2D(
                (uint)texture.Width,
                (uint)texture.Height,
                1,
                1,
                PixelFormat.B8_G8_R8_A8_UNorm,
                TextureUsage.Sampled);
        }
    }
}