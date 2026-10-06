using System.Text.Json.Nodes;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using static Beutl.AgentToolkit.Schema.SamplePatchBuilder;

namespace Beutl.AgentToolkit.Schema;

public sealed partial class CompositionTemplateCatalog
{
    private static CompositionRender RenderKineticRibbon(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 0);
        string title = ReadString(context.ResolvedProps, "title", "Beutl motion");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "Declarative composition");
        float density = ReadFloat(context.ResolvedProps, "density", 1);
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Kinetic ribbon background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Seeded gradient field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) }
                })
        ];

        for (int i = 0; i < Math.Clamp((int)MathF.Round(3 * density), 2, 6); i++)
        {
            float y = -170 + (i * 140) + context.Random.Range(-36, 36);
            float rotation = -15 + (i * 6) + context.Random.Range(-6, 6);
            float startX = -780 + context.Random.Range(-140, 120);
            float endX = 620 + context.Random.Range(-160, 180);
            Element ribbon = CreateElement(
                $"Kinetic ribbon band {i + 1}",
                4 + i,
                fullLength,
                new RectShape
                {
                    Name = $"Seeded ribbon {i + 1}",
                    Width = { CurrentValue = 1060 + context.Random.Range(-120, 180) },
                    Height = { CurrentValue = 58 + context.Random.Range(0, 34) },
                    Fill = { CurrentValue = CreateLinearGradient(i % 2 == 0 ? palette.Accent : palette.SecondaryAccent, i % 2 == 0 ? palette.SecondaryAccent : palette.Accent) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(startX, y),
                                new RotationTransform(rotation)
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateBlur(2.5f * intensity),
                                CreateBrightness(106 + (8 * intensity)),
                                CreateDropShadow(0, 0, 14 * intensity, "#9936f0ff")
                            }
                        }
                    }
                });

            elements.Add(WithFirstObjectAnimations(ribbon, ribbonObject =>
            {
                AddFloatAnimation(ribbonObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.7 + (i * 0.12), 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 80, typeof(SineEaseInOut)));
                AddFloatAnimation(GetTransformChildJson(ribbonObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, startX, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, endX, typeof(SineEaseInOut)));
            }));
        }

        Element titleElement = CreateElement(
            "Kinetic ribbon title",
            30,
            fullLength,
            new TextBlock
            {
                Name = "Seeded title",
                Text = { CurrentValue = title },
                Size = { CurrentValue = 104 + context.Random.Range(-10, 8) },
                Spacing = { CurrentValue = 8 + context.Random.Range(0, 8) },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse(palette.Foreground)) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(context.Random.Range(-80, 80), -20 + context.Random.Range(-28, 26))
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateDropShadow(10, 16, 12, "#99000000")
                        }
                    }
                }
            });
        elements.Add(WithFirstObjectAnimations(titleElement, titleObject =>
        {
            AddFloatAnimation(titleObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.9, 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds - 0.6, 100, typeof(SineEaseInOut)), (context.Metadata.DurationSeconds, 0, typeof(SineEaseInOut)));
            AddFloatAnimation(titleObject, nameof(TextBlock.Spacing), (0, 22, typeof(CubicEaseOut)), (1.4, 8, typeof(SineEaseInOut)), (context.Metadata.DurationSeconds, 14, typeof(SineEaseInOut)));
        }));

        elements.Add(CreateTextElement("Kinetic ribbon subtitle", "Seeded subtitle", subtitle, 31, 32, 5, 0, 82, palette.Foreground, fullLength));
        AddNoiseDots(elements, context, palette, fullLength, zStart: 16, count: Math.Clamp((int)MathF.Round(12 * density), 6, 24));

        return CreateRender(context, elements);
    }

    private static CompositionRender RenderOrbitalRadar(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 1);
        string title = ReadString(context.ResolvedProps, "title", "Orbit map");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "Signal route notes");
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        float density = ReadFloat(context.ResolvedProps, "density", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Orbital composition background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Instrument gradient field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) }
                })
        ];

        OrbitalLayout layout = ResolveOrbitalLayout(context.Random.NextInt(3));
        float centerX = layout.CenterBaseX + context.Random.Range(-140, 80);
        float centerY = layout.CenterBaseY + context.Random.Range(-70, 60);
        AddOrbitalRings(elements, context, palette, layout, centerX, centerY, intensity, fullLength);
        elements.Add(CreateOrbitalSweep(context, palette, layout, centerX, centerY, intensity, fullLength));
        AddOrbitalSignalNodes(elements, context, palette, layout, centerX, centerY, intensity, density, fullLength);

        float titleX = Math.Clamp(layout.TitleBaseX + context.Random.Range(-20, 80), -560, 560);
        elements.Add(CreateTextElement("Orbital title", "Technical title", title, 30, 88, 10, titleX, layout.TitleBaseY, palette.Foreground, fullLength));
        elements.Add(CreateTextElement("Orbital subtitle", "Technical subtitle", subtitle, 31, 32, 5, titleX, layout.TitleBaseY + 92, palette.Foreground, fullLength));

        return CreateRender(context, elements);
    }

    private static OrbitalLayout ResolveOrbitalLayout(int layoutVariant)
    {
        float centerBaseX = layoutVariant switch
        {
            1 => 250,
            2 => -20,
            _ => -250
        };
        float centerBaseY = layoutVariant switch
        {
            2 => -120,
            _ => 0
        };
        float titleBaseX = layoutVariant switch
        {
            1 => -520,
            2 => -420,
            _ => 420
        };
        float titleBaseY = layoutVariant == 2 ? 300 : -72;
        int ringCount = layoutVariant == 2 ? 4 : 3;
        float ringBaseSize = layoutVariant == 2 ? 300 : 360;
        float ringGap = layoutVariant == 2 ? 150 : 190;
        float sweepWidth = layoutVariant switch
        {
            1 => 760,
            2 => 1120,
            _ => 820
        };
        float sweepStartRotation = layoutVariant switch
        {
            1 => 150,
            2 => -70,
            _ => -20
        };
        float sweepEndRotation = sweepStartRotation + (layoutVariant == 1 ? -260 : 260);
        int nodeBaseCount = layoutVariant == 2 ? 5 : 4;
        float nodeSpreadX = layoutVariant == 2 ? 520 : 420;
        float nodeSpreadY = layoutVariant == 2 ? 250 : 300;
        return new OrbitalLayout(
            centerBaseX,
            centerBaseY,
            titleBaseX,
            titleBaseY,
            ringCount,
            ringBaseSize,
            ringGap,
            sweepWidth,
            sweepStartRotation,
            sweepEndRotation,
            nodeBaseCount,
            nodeSpreadX,
            nodeSpreadY);
    }

    private static void AddOrbitalRings(
        List<Element> elements,
        CompositionContext context,
        Palette palette,
        OrbitalLayout layout,
        float centerX,
        float centerY,
        float intensity,
        TimeSpan fullLength)
    {
        for (int i = 0; i < layout.RingCount; i++)
        {
            float size = layout.RingBaseSize + (i * layout.RingGap) + context.Random.Range(-34, 44);
            Element ring = CreateElement(
                $"Orbital radar ring {i + 1}",
                4 + i,
                fullLength,
                new EllipseShape
                {
                    Name = $"Seeded orbit ring {i + 1}",
                    Width = { CurrentValue = size },
                    Height = { CurrentValue = size },
                    Fill = { CurrentValue = null },
                    Pen = { CurrentValue = CreatePen(i % 2 == 0 ? palette.Accent : palette.SecondaryAccent, 3 + i) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(centerX, centerY),
                                new RotationTransform(context.Random.Range(-20, 20))
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateBlur(0.5f + (i * 0.5f)),
                                CreateDropShadow(0, 0, (12 + (i * 5)) * intensity, "#aa43e7ff")
                            }
                        }
                    }
                });

            elements.Add(WithFirstObjectAnimations(ring, ringObject =>
            {
                AddFloatAnimation(ringObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.5 + (i * 0.18), 92 - (i * 12), typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 45 + (i * 8), typeof(SineEaseInOut)));
                AddFloatAnimation(GetTransformChildJson(ringObject, typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, i * 16, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, (i % 2 == 0 ? 360 : -260), typeof(SineEaseInOut)));
            }));
        }
    }

    private static Element CreateOrbitalSweep(
        CompositionContext context,
        Palette palette,
        OrbitalLayout layout,
        float centerX,
        float centerY,
        float intensity,
        TimeSpan fullLength)
    {
        Element sweep = CreateElement(
            "Orbital radar sweep",
            10,
            fullLength,
            new RectShape
            {
                Name = "Seeded scan sweep",
                Width = { CurrentValue = layout.SweepWidth },
                Height = { CurrentValue = 9 },
                Fill = { CurrentValue = CreateLinearGradient("#0036f0ff", palette.Accent) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(centerX, centerY),
                            new RotationTransform(layout.SweepStartRotation)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(2.2f * intensity)
                        }
                    }
                }
            });
        return WithFirstObjectAnimations(sweep, sweepObject =>
        {
            AddFloatAnimation(sweepObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.8, 78, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 0, typeof(SineEaseInOut)));
            AddFloatAnimation(GetTransformChildJson(sweepObject, typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, layout.SweepStartRotation, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, layout.SweepEndRotation, typeof(SineEaseInOut)));
        });
    }

    private static void AddOrbitalSignalNodes(
        List<Element> elements,
        CompositionContext context,
        Palette palette,
        OrbitalLayout layout,
        float centerX,
        float centerY,
        float intensity,
        float density,
        TimeSpan fullLength)
    {
        int nodeCount = Math.Clamp((int)MathF.Round(layout.NodeBaseCount * density), 2, 9);
        for (int i = 0; i < nodeCount; i++)
        {
            float x = centerX + context.Random.Range(-layout.NodeSpreadX, layout.NodeSpreadX);
            float y = centerY + context.Random.Range(-layout.NodeSpreadY, layout.NodeSpreadY);
            Element node = CreateElement(
                $"Orbital signal node {i + 1}",
                14 + i,
                fullLength,
                new EllipseShape
                {
                    Name = $"Seeded signal node {i + 1}",
                    Width = { CurrentValue = 48 + context.Random.Range(0, 38) },
                    Height = { CurrentValue = 48 + context.Random.Range(0, 38) },
                    Fill = { CurrentValue = CreateLinearGradient(palette.SecondaryAccent, palette.Accent) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x, y)
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateDropShadow(0, 0, 20 * intensity, "#cc36f0ff"),
                                CreateBrightness(116)
                            }
                        }
                    }
                });

            elements.Add(WithFirstObjectAnimations(node, nodeObject =>
            {
                AddFloatAnimation(nodeObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.6 + (i * 0.2), 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds - 0.3, 100, typeof(SineEaseInOut)), (context.Metadata.DurationSeconds, 0, typeof(SineEaseInOut)));
                JsonObject translate = GetTransformChildJson(nodeObject, typeof(TranslateTransform));
                AddFloatAnimation(translate, nameof(TranslateTransform.X), (0, x, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, x + context.Random.Range(-120, 120), typeof(SineEaseInOut)));
                AddFloatAnimation(translate, nameof(TranslateTransform.Y), (0, y, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, y + context.Random.Range(-120, 120), typeof(SineEaseInOut)));
            }));
        }
    }

    private static CompositionRender RenderSplitScreen(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 2);
        string title = ReadString(context.ResolvedProps, "title", "Frame flow");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "Kinetic layout notes");
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Split composition background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Editorial gradient field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateSaturate(110 + (8 * intensity)),
                                CreateHueRotate(context.Random.Range(-8, 8))
                            }
                        }
                    }
                })
        ];

        float panelX = -410 + context.Random.Range(-90, 110);
        Element panel = CreateElement(
            "Split screen panel",
            4,
            fullLength,
            new RoundedRectShape
            {
                Name = "Seeded editorial panel",
                Width = { CurrentValue = 700 + context.Random.Range(-70, 110) },
                Height = { CurrentValue = 580 + context.Random.Range(-60, 80) },
                CornerRadius = { CurrentValue = new CornerRadius(48 + context.Random.Range(-12, 14)) },
                Fill = { CurrentValue = CreateLinearGradient("#eeffffff", palette.SecondaryAccent) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(panelX - 220, 0)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(0.8f),
                            CreateDropShadow(24, 34, 18 * intensity, "#99000000")
                        }
                    }
                }
            });
        elements.Add(WithFirstObjectAnimations(panel, panelObject =>
        {
            AddFloatAnimation(panelObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.7, 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 100, typeof(SineEaseInOut)));
            AddFloatAnimation(GetTransformChildJson(panelObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, panelX - 260, typeof(CubicEaseOut)), (1.1, panelX, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, panelX + 38, typeof(SineEaseInOut)));
        }));

        elements.Add(CreateTextElement("Split screen headline", "Stacked headline", title, 16, 92 + context.Random.Range(-12, 10), 4, panelX, -72, "#ff081225", fullLength));
        elements.Add(CreateTextElement("Split screen caption", "Panel caption", subtitle, 17, 28, 3, panelX, 48, "#ff10223c", fullLength));

        for (int i = 0; i < 4; i++)
        {
            float x = 310 + (i * 120) + context.Random.Range(-80, 80);
            float y = -250 + (i * 135) + context.Random.Range(-50, 50);
            Element block = CreateElement(
                $"Split screen block {i + 1}",
                8 + i,
                fullLength,
                new RectShape
                {
                    Name = $"Seeded editorial block {i + 1}",
                    Width = { CurrentValue = i % 2 == 0 ? 610 + context.Random.Range(-60, 90) : 82 + context.Random.Range(-12, 24) },
                    Height = { CurrentValue = i % 2 == 0 ? 86 + context.Random.Range(-10, 24) : 420 + context.Random.Range(-60, 90) },
                    Fill = { CurrentValue = CreateLinearGradient(i % 2 == 0 ? palette.Accent : palette.SecondaryAccent, i % 2 == 0 ? palette.SecondaryAccent : palette.Accent) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x + 260, y)
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateBrightness(112),
                                CreateDropShadow(18, 20, 14 * intensity, "#88000000")
                            }
                        }
                    }
                });

            elements.Add(WithFirstObjectAnimations(block, blockObject =>
            {
                AddFloatAnimation(blockObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.8 + (i * 0.16), 96, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 78, typeof(SineEaseInOut)));
                AddFloatAnimation(GetTransformChildJson(blockObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, x + 320, typeof(CubicEaseOut)), (1.2 + (i * 0.18), x, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, x - 80, typeof(SineEaseInOut)));
            }));
        }

        elements.Add(CreateTextElement("Split screen variant label", "Variant label", $"Variant {(int)(StableHash(context.Seed) % 97):00}", 30, 50, 10, 520, 96, palette.Foreground, fullLength));

        return CreateRender(context, elements);
    }

    private static CompositionRender RenderLiquidGradient(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 3);
        string title = ReadString(context.ResolvedProps, "title", "Liquid signal");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "Soft gradient field");
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        float density = ReadFloat(context.ResolvedProps, "density", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Liquid gradient background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Soft liquid field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateSaturate(118 + (8 * intensity)),
                                CreateBrightness(104)
                            }
                        }
                    }
                })
        ];

        int blobCount = Math.Clamp((int)MathF.Round(5 * density), 4, 9);
        for (int i = 0; i < blobCount; i++)
        {
            float size = 260 + context.Random.Range(0, 260);
            float x = context.Random.Range(-760, 760);
            float y = context.Random.Range(-340, 340);
            float endX = x + context.Random.Range(-220, 220);
            float endY = y + context.Random.Range(-180, 180);
            Element blob = CreateElement(
                $"Liquid gradient blob {i + 1}",
                3 + i,
                fullLength,
                new EllipseShape
                {
                    Name = $"Seeded liquid blob {i + 1}",
                    Width = { CurrentValue = size },
                    Height = { CurrentValue = size * context.Random.Range(0.62f, 1.18f) },
                    Fill = { CurrentValue = CreateRadialGradient(i % 2 == 0 ? palette.Accent : palette.SecondaryAccent, "#00111111") },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x, y),
                                new RotationTransform(context.Random.Range(-24, 24))
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateBlur((6 + (i * 1.2f)) * intensity),
                                CreateDropShadow(0, 0, 28 * intensity, i % 2 == 0 ? "#7734e6ff" : "#77ff7a59")
                            }
                        }
                    }
                });

            elements.Add(WithFirstObjectAnimations(blob, blobObject =>
            {
                AddFloatAnimation(blobObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.5 + (i * 0.1), 84, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 54, typeof(SineEaseInOut)));
                JsonObject translate = GetTransformChildJson(blobObject, typeof(TranslateTransform));
                AddFloatAnimation(translate, nameof(TranslateTransform.X), (0, x, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, endX, typeof(SineEaseInOut)));
                AddFloatAnimation(translate, nameof(TranslateTransform.Y), (0, y, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, endY, typeof(SineEaseInOut)));
                AddFloatAnimation(GetTransformChildJson(blobObject, typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, context.Random.Range(-18, 18), typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, context.Random.Range(90, 220), typeof(SineEaseInOut)));
            }));
        }

        elements.Add(CreateTextElement("Liquid title", "Floating title", title, 30, 84, 7, -620 + context.Random.Range(-60, 90), 245 + context.Random.Range(-40, 30), palette.Foreground, fullLength));
        elements.Add(CreateTextElement("Liquid subtitle", "Floating subtitle", subtitle, 31, 30, 4, -618 + context.Random.Range(-60, 90), 338, palette.Foreground, fullLength));
        AddNoiseDots(elements, context, palette, fullLength, zStart: 18, count: Math.Clamp((int)MathF.Round(10 * density), 6, 20));

        return CreateRender(context, elements);
    }

    private static CompositionRender RenderDataDashboard(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 4);
        string title = ReadString(context.ResolvedProps, "title", "Signal index");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "Live metrics");
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        float density = ReadFloat(context.ResolvedProps, "density", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Dashboard background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Editorial dashboard field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateHighContrast(8 + (6 * intensity)),
                                CreateSaturate(122)
                            }
                        }
                    }
                })
        ];

        for (int i = 0; i < 7; i++)
        {
            float y = -300 + (i * 100);
            Element rule = CreateElement(
                $"Dashboard scanline {i + 1}",
                2,
                fullLength,
                new RectShape
                {
                    Name = $"Metric rule {i + 1}",
                    Width = { CurrentValue = 1380 },
                    Height = { CurrentValue = 2 },
                    Fill = { CurrentValue = new SolidColorBrush(Color.Parse(i % 2 == 0 ? "#33ffffff" : "#2243e7ff")) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(120, y)
                            }
                        }
                    }
                });
            elements.Add(rule);
        }

        int barCount = Math.Clamp((int)MathF.Round(12 * density), 8, 18);
        for (int i = 0; i < barCount; i++)
        {
            float targetHeight = 140 + (float)((context.Random.Noise("bar-height", i) + 1) * 190);
            float x = -620 + (i * (1120f / Math.Max(1, barCount - 1))) + context.Random.Range(-18, 18);
            float y = 220 - (targetHeight / 2);
            Element bar = CreateElement(
                $"Dashboard metric bar {i + 1}",
                5 + i,
                fullLength,
                new RoundedRectShape
                {
                    Name = $"Seeded metric bar {i + 1}",
                    Width = { CurrentValue = 46 + context.Random.Range(-8, 16) },
                    Height = { CurrentValue = 20 },
                    CornerRadius = { CurrentValue = new CornerRadius(18) },
                    Fill = { CurrentValue = CreateLinearGradient(i % 3 == 0 ? palette.Accent : palette.SecondaryAccent, i % 3 == 0 ? palette.SecondaryAccent : palette.Accent) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x, y + (targetHeight / 2))
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateDropShadow(0, 10, 16 * intensity, "#77000000"),
                                CreateBrightness(110)
                            }
                        }
                    }
                });

            elements.Add(WithFirstObjectAnimations(bar, barObject =>
            {
                AddFloatAnimation(barObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.35 + (i * 0.05), 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 82, typeof(SineEaseInOut)));
                AddFloatAnimation(barObject, nameof(RectShape.Height), (0, 20, typeof(CubicEaseOut)), (0.65 + (i * 0.04), targetHeight, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, targetHeight + context.Random.Range(-80, 80), typeof(SineEaseInOut)));
                JsonObject translate = GetTransformChildJson(barObject, typeof(TranslateTransform));
                AddFloatAnimation(translate, nameof(TranslateTransform.Y), (0, y + (targetHeight / 2), typeof(CubicEaseOut)), (0.65 + (i * 0.04), y, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, y + context.Random.Range(-30, 30), typeof(SineEaseInOut)));
            }));
        }

        elements.Add(CreateTextElement("Dashboard title", "Metric title", title, 30, 76, 6, -710, -360, palette.Foreground, fullLength));
        elements.Add(CreateTextElement("Dashboard subtitle", "Metric subtitle", subtitle, 31, 28, 4, -708, -270, palette.Foreground, fullLength));
        elements.Add(CreateTextElement("Dashboard value label", "Metric value", $"{(int)(StableHash(context.Seed) % 9000) + 1000}", 32, 96, 2, 520, -335, palette.Accent, fullLength));

        return CreateRender(context, elements);
    }

    private static CompositionRender RenderGlitchCollage(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 5);
        string title = ReadString(context.ResolvedProps, "title", "Glitch cut");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "Chromatic collage");
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        float density = ReadFloat(context.ResolvedProps, "density", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Glitch collage background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Chromatic cutout field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateHighContrast(12),
                                CreateColorShift((int)(4 * intensity))
                            }
                        }
                    }
                })
        ];

        int sliceCount = Math.Clamp((int)MathF.Round(8 * density), 6, 14);
        for (int i = 0; i < sliceCount; i++)
        {
            float width = 280 + context.Random.Range(80, 420);
            float height = 34 + context.Random.Range(20, 110);
            float x = context.Random.Range(-720, 720);
            float y = context.Random.Range(-310, 290);
            float endX = x + context.Random.Range(-260, 260);
            Element slice = CreateElement(
                $"Glitch collage slice {i + 1}",
                4 + i,
                fullLength,
                new RectShape
                {
                    Name = $"Seeded glitch slice {i + 1}",
                    Width = { CurrentValue = width },
                    Height = { CurrentValue = height },
                    Fill = { CurrentValue = CreateLinearGradient(i % 2 == 0 ? palette.Accent : "#eeffffff", i % 2 == 0 ? palette.SecondaryAccent : palette.Accent) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x, y),
                                new RotationTransform(context.Random.Range(-7, 7))
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateMosaic(5 + (i % 4 * 3)),
                                CreateColorShift((int)(3 + (i % 5) * intensity)),
                                CreateDropShadow(16, 16, 8 * intensity, "#88000000")
                            }
                        }
                    }
                });

            elements.Add(WithFirstObjectAnimations(slice, sliceObject =>
            {
                AddFloatAnimation(sliceObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.2 + (i * 0.06), 88, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 52 + (i % 4 * 8), typeof(SineEaseInOut)));
                AddFloatAnimation(GetTransformChildJson(sliceObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, x + context.Random.Range(-420, 420), typeof(CubicEaseOut)), (0.55 + (i * 0.05), x, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, endX, typeof(SineEaseInOut)));
            }));
        }

        Element titleElement = CreateElement(
            "Glitch title",
            30,
            fullLength,
            new TextBlock
            {
                Name = "Hard cut title",
                Text = { CurrentValue = title },
                Size = { CurrentValue = 112 + context.Random.Range(-12, 10) },
                Spacing = { CurrentValue = 2 },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse(palette.Foreground)) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-610 + context.Random.Range(-80, 120), 118 + context.Random.Range(-40, 60))
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateColorShift((int)(8 * intensity)),
                            CreateDropShadow(12, 18, 10, "#aa000000")
                        }
                    }
                }
            });
        elements.Add(WithFirstObjectAnimations(titleElement, titleObject =>
        {
            AddFloatAnimation(titleObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.45, 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds - 0.25, 100, typeof(SineEaseInOut)), (context.Metadata.DurationSeconds, 0, typeof(SineEaseInOut)));
            AddFloatAnimation(titleObject, nameof(TextBlock.Spacing), (0, 18, typeof(CubicEaseOut)), (0.9, 2, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 8, typeof(SineEaseInOut)));
        }));
        elements.Add(CreateTextElement("Glitch subtitle", "Hard cut subtitle", subtitle, 31, 30, 6, -600, 238, palette.Foreground, fullLength));

        return CreateRender(context, elements);
    }

    private static void AddNoiseDots(List<Element> elements, CompositionContext context, Palette palette, TimeSpan length, int zStart, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float x = (float)(context.Random.Noise("noise-x", i) * 820);
            float y = (float)(context.Random.Noise("noise-y", i) * 420);
            float size = 10 + (float)((context.Random.Noise("noise-size", i) + 1) * 10);
            Element dot = CreateElement(
                $"Seeded noise dot {i + 1}",
                zStart + i,
                length,
                new EllipseShape
                {
                    Name = $"Deterministic noise dot {i + 1}",
                    Width = { CurrentValue = size },
                    Height = { CurrentValue = size },
                    Fill = { CurrentValue = new SolidColorBrush(Color.Parse(i % 2 == 0 ? palette.Accent : palette.SecondaryAccent)) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x, y)
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateBlur(1.4f),
                                CreateDropShadow(0, 0, 10, "#8836f0ff")
                            }
                        }
                    }
                });

            elements.Add(WithFirstObjectAnimations(dot, dotObject =>
            {
                AddFloatAnimation(dotObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.8 + (i * 0.05), 78, typeof(CubicEaseOut)), (length.TotalSeconds, 0, typeof(SineEaseInOut)));
            }));
        }
    }

    private static CompositionRender CreateRender(CompositionContext context, IReadOnlyList<Element> elements)
    {
        return new CompositionRender(
            context.Spec.Name,
            context.Seed,
            CloneObject(context.InputProps),
            CloneObject(context.ResolvedProps),
            context.Metadata,
            context.Sequences.ToArray(),
            context.Transitions.ToArray(),
            new JsonObject
            {
                ["Duration"] = context.Metadata.Duration,
                ["Elements"] = new JsonArray(elements
                    .Select(SerializeWithoutIds)
                    .ToArray<JsonNode?>())
            });
    }

    private static Element CreateElement(string name, int zIndex, TimeSpan length, EngineObject obj)
    {
        var element = new Element
        {
            Name = name,
            Start = TimeSpan.Zero,
            Length = length,
            ZIndex = zIndex
        };
        element.AddObject(obj);
        return element;
    }

    private static Element CreateTextElement(
        string elementName,
        string objectName,
        string text,
        int zIndex,
        float size,
        float spacing,
        float x,
        float y,
        string color,
        TimeSpan length)
    {
        Element element = CreateElement(
            elementName,
            zIndex,
            length,
            new TextBlock
            {
                Name = objectName,
                Text = { CurrentValue = text },
                Size = { CurrentValue = size },
                Spacing = { CurrentValue = spacing },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse(color)) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(x, y)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateDropShadow(8, 10, 10, "#88000000")
                        }
                    }
                }
            });

        return WithFirstObjectAnimations(element, textObject =>
        {
            AddFloatAnimation(textObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.9, 100, typeof(CubicEaseOut)), (length.TotalSeconds - 0.3, 100, typeof(SineEaseInOut)), (length.TotalSeconds, 0, typeof(SineEaseInOut)));
        });
    }

    private static RadialGradientBrush CreateRadialGradient(string innerColor, string outerColor)
    {
        return new RadialGradientBrush
        {
            Center = { CurrentValue = RelativePoint.Center },
            GradientOrigin = { CurrentValue = RelativePoint.Center },
            Radius = { CurrentValue = 72 },
            GradientStops =
            {
                new GradientStop(Color.Parse(innerColor), 0),
                new GradientStop(Color.Parse(outerColor), 1)
            }
        };
    }

    private static ColorShift CreateColorShift(int offset)
    {
        var colorShift = new ColorShift();
        colorShift.RedOffset.CurrentValue = new PixelPoint(offset, 0);
        colorShift.GreenOffset.CurrentValue = PixelPoint.Origin;
        colorShift.BlueOffset.CurrentValue = new PixelPoint(-offset, 0);
        colorShift.AlphaOffset.CurrentValue = PixelPoint.Origin;
        return colorShift;
    }

    // Keyframes are authored on the serialized form (AddFloatAnimation works on JSON), so the element
    // is serialized, its first object animated, and the element rebuilt from that JSON.
    private static Element WithFirstObjectAnimations(Element element, Action<JsonObject> animate)
    {
        JsonObject elementJson = SerializeWithoutIds(element);
        animate(GetFirstObjectJson(elementJson));
        return DeserializeElement(elementJson);
    }

    private static Element DeserializeElement(JsonObject json)
    {
        return (Element)CoreSerializer.DeserializeFromJsonObject(CloneObject(json), typeof(Element))!;
    }

    private readonly record struct OrbitalLayout(
        float CenterBaseX,
        float CenterBaseY,
        float TitleBaseX,
        float TitleBaseY,
        int RingCount,
        float RingBaseSize,
        float RingGap,
        float SweepWidth,
        float SweepStartRotation,
        float SweepEndRotation,
        int NodeBaseCount,
        float NodeSpreadX,
        float NodeSpreadY);
}
