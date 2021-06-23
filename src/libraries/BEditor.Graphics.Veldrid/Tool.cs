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

        public static DepthStencilStateDescription ToDepthStencilStateDescription(this DepthTestState state)
        {
            return new(state.Enabled, state.WriteEnabled, ToVeldrid(state.Comparison));
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
    }
}