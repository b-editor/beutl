using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Schema;

public sealed partial class SchemaGenerator
{
    private static DeclarativeExample CreateNewElementSkeletonExample()
    {
        string elementType = IdentityHelper.WriteDiscriminator(typeof(Element));
        string textType = IdentityHelper.WriteDiscriminator(typeof(TextBlock));

        return new DeclarativeExample(
            "insert-new-element-skeleton",
            "Minimal structure-only patch for inserting one new timeline Element with one new TextBlock object. Use this to copy the required Element/Object $type shape without cloning a full-scene starter. Omit Id for genuinely new Elements and Objects; keep Id only when modifying an existing parent.",
            new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    ["$type"] = elementType,
                    [nameof(CoreObject.Name)] = "new-text-element",
                    [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
                    [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c"),
                    [nameof(Element.ZIndex)] = 10,
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        ["$type"] = textType,
                        [nameof(CoreObject.Name)] = "new-text",
                        [nameof(TextBlock.Text)] = "Title"
                    })
                })
            });
    }

    private static DeclarativeExample CreateGeometryShapePathExample()
    {
        string elementType = IdentityHelper.WriteDiscriminator(typeof(Element));
        string geometryShapeType = IdentityHelper.WriteDiscriminator(typeof(GeometryShape));
        string pathGeometryType = IdentityHelper.WriteDiscriminator(typeof(PathGeometry));
        string pathFigureType = IdentityHelper.WriteDiscriminator(typeof(PathFigure));
        string lineSegmentType = IdentityHelper.WriteDiscriminator(typeof(LineSegment));
        string solidBrushType = IdentityHelper.WriteDiscriminator(typeof(SolidColorBrush));

        return new DeclarativeExample(
            "insert-new-geometry-shape-path",
            "Minimal patch for a bespoke vector GeometryShape built from a typed PathGeometry. Reach for GeometryShape instead of RectShape/EllipseShape when you need arrows, chevrons, brackets, crop marks, icons, or letter fragments. Paths are authored as typed segment objects, NOT an SVG path string: a PathGeometry holds Figures, each PathFigure has a StartPoint plus Segments of LineSegment/CubicBezierSegment/QuadraticBezierSegment/ConicSegment/ArcSegment; Point values serialize as 'x, y'. GeometryShape sizes to its geometry bounds (no Width/Height), and the drawn center lands at the alignment-resolved center plus the path bounds origin — author coordinates with the artwork's top-left at (0, 0) as this example does, never centered on (0, 0). This example draws a closed right-pointing play/arrow triangle; add more Figures, set IsClosed=false for open strokes with a Pen, or swap LineSegment for CubicBezierSegment (ControlPoint1/ControlPoint2/EndPoint) to get curves. New Elements and Objects omit Id.",
            new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    ["$type"] = elementType,
                    [nameof(CoreObject.Name)] = "geometry-arrow-element",
                    [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
                    [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c"),
                    [nameof(Element.ZIndex)] = 15,
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        ["$type"] = geometryShapeType,
                        [nameof(CoreObject.Name)] = "geometry-arrow",
                        [nameof(Shape.Fill)] = new JsonObject
                        {
                            ["$type"] = solidBrushType,
                            [nameof(SolidColorBrush.Color)] = "#ff3f34f0"
                        },
                        [nameof(GeometryShape.Data)] = new JsonObject
                        {
                            ["$type"] = pathGeometryType,
                            [nameof(PathGeometry.Figures)] = new JsonArray(new JsonObject
                            {
                                ["$type"] = pathFigureType,
                                [nameof(PathFigure.StartPoint)] = "0, 0",
                                [nameof(PathFigure.IsClosed)] = true,
                                [nameof(PathFigure.Segments)] = new JsonArray(
                                    new JsonObject
                                    {
                                        ["$type"] = lineSegmentType,
                                        [nameof(LineSegment.Point)] = "100, 55"
                                    },
                                    new JsonObject
                                    {
                                        ["$type"] = lineSegmentType,
                                        [nameof(LineSegment.Point)] = "0, 110"
                                    })
                            })
                        }
                    })
                })
            });
    }

    private static DeclarativeExample CreateNewAnimatedTextElementExample()
    {
        string elementType = IdentityHelper.WriteDiscriminator(typeof(Element));
        string textType = IdentityHelper.WriteDiscriminator(typeof(TextBlock));
        JsonObject opacityAnimation = CreateFloatAnimation(
            (0, 0, typeof(LinearEasing)),
            (0.35, 100, typeof(SineEaseOut)),
            (1.65, 100, typeof(LinearEasing)),
            (2, 0, typeof(LinearEasing)));
        opacityAnimation[nameof(KeyFrameAnimation.UseGlobalClock)] = false;

        return new DeclarativeExample(
            "insert-new-animated-text-keyframes",
            "Minimal patch for inserting one new TextBlock object with a valid KeyFrameAnimation<float> on Opacity. New Elements and Objects omit Id; use schema-returned discriminators and keep UseGlobalClock=false key times inside Element.Length.",
            new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    ["$type"] = elementType,
                    [nameof(CoreObject.Name)] = "animated-text-element",
                    [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
                    [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c"),
                    [nameof(Element.ZIndex)] = 20,
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        ["$type"] = textType,
                        [nameof(CoreObject.Name)] = "animated-text",
                        [nameof(TextBlock.Text)] = "Animated",
                        ["Animations"] = new JsonObject
                        {
                            ["Opacity"] = opacityAnimation
                        }
                    })
                })
            });
    }

    private static DrawableGroup CreateCameraRigGroup(string name)
    {
        return new DrawableGroup
        {
            Name = name,
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children =
                    {
                        new TranslateTransform(0, 0),
                        new ScaleTransform()
                    }
                }
            }
        };
    }

    private static JsonObject GetFlowGroupJson(JsonObject elementJson)
    {
        string groupDiscriminator = IdentityHelper.WriteDiscriminator(typeof(DrawableGroup));
        return ((JsonArray)elementJson[nameof(Element.Objects)]!)
            .OfType<JsonObject>()
            .Single(obj => string.Equals(obj["$type"]?.GetValue<string>(), groupDiscriminator, StringComparison.Ordinal));
    }

    private static void AddCameraRigPushInAnimations(JsonObject rigJson)
    {
        JsonObject scaleJson = GetTransformChildJson(rigJson, typeof(ScaleTransform));
        AddFloatAnimation(scaleJson, nameof(ScaleTransform.Scale), (0, 100, typeof(SineEaseInOut)), (8, 106, typeof(SineEaseInOut)));
        JsonObject translateJson = GetTransformChildJson(rigJson, typeof(TranslateTransform));
        AddFloatAnimation(translateJson, nameof(TranslateTransform.X), (0, 0, typeof(SineEaseInOut)), (8, -48, typeof(SineEaseInOut)));
    }

    private static DeclarativeExample CreateCameraRigPushInExample()
    {
        DrawableGroup rig = CreateCameraRigGroup("[role:camera-rig] Shot 1 camera");
        rig.Children.Add(new RectShape
        {
            Name = "[role:text-backing] Shot 1 title plate",
            Width = { CurrentValue = 760 },
            Height = { CurrentValue = 240 },
            Fill = { CurrentValue = CreateLinearGradient("#ff1c2a4a", "#ff2f4a7a") }
        });
        rig.Children.Add(new TextBlock
        {
            Name = "Shot 1 hero title",
            Text = { CurrentValue = "Camera move" },
            Size = { CurrentValue = 96 },
            Fill = { CurrentValue = new SolidColorBrush(Colors.White) }
        });

        Element element = CreateElement("[role:camera-rig] Shot 1 rig", zIndex: 10, rig);
        JsonObject elementJson = SerializeExampleElement(element);
        AddCameraRigPushInAnimations(GetFlowGroupJson(elementJson));

        return new DeclarativeExample(
            "insert-camera-rig-push-in",
            "Camera (viewpoint) move for a 2D shot, nested variant: Beutl has no scene camera, so wrap the shot's content in a [role:camera-rig] DrawableGroup and animate the rig's TransformGroup. This example is a slow eased push-in (ScaleTransform.Scale 100 -> 106; Scale is percent) with a slight pan (TranslateTransform.X; camera-left = rig-right, camera-in = scale-up). Keep the PortalObject entry that precedes the DrawableGroup in Element.Objects — a flow operator without it is rejected; its Count stays 0 here, pulling no timeline rows, so with the portal as the Element's first object the group transforms only its nested Children. Nested children have no Element timing of their own, so when grouped content needs per-item Start/Length or should stay visible as timeline layers, prefer the portal variant in insert-camera-rig-portal. Replace the placeholder children with the shot's drawables, keep locked background plates outside the rig in their own Elements, use a fast large translate for a whip-pan cut bridge, and animate per-depth-band rigs at different translate amplitudes for parallax.",
            new JsonObject
            {
                ["Elements"] = new JsonArray(elementJson)
            });
    }

    private static DeclarativeExample CreateCameraRigPortalExample()
    {
        DrawableGroup rig = CreateCameraRigGroup("[role:camera-rig] Shot 2 camera");
        Element rigElement = CreateElement("[role:camera-rig] Shot 2 rig", zIndex: 10, rig);
        ((PortalObject)rigElement.Objects[0]).Count.CurrentValue = 2;

        Element plateElement = CreateElement(
            "[role:text-backing] Shot 2 title plate",
            zIndex: 11,
            new RectShape
            {
                Name = "[role:text-backing] Shot 2 title plate",
                Width = { CurrentValue = 760 },
                Height = { CurrentValue = 240 },
                Fill = { CurrentValue = CreateLinearGradient("#ff1c2a4a", "#ff2f4a7a") }
            });

        Element titleElement = CreateElement(
            "Shot 2 hero title",
            zIndex: 12,
            new TextBlock
            {
                Name = "Shot 2 hero title",
                Text = { CurrentValue = "Camera move" },
                Size = { CurrentValue = 96 },
                Fill = { CurrentValue = new SolidColorBrush(Colors.White) }
            });

        JsonObject rigJson = SerializeExampleElement(rigElement);
        AddCameraRigPushInAnimations(GetFlowGroupJson(rigJson));

        return new DeclarativeExample(
            "insert-camera-rig-portal",
            "Camera (viewpoint) move for a 2D shot, timeline-flow variant — preferred when grouped content needs its own Element timing or should stay visible as timeline layers. The shot's content stays as ordinary one-object Elements on contiguous ZIndex rows (11 and 12 here), and the rig Element directly below them (ZIndex 10) holds a PortalObject with Count=2 followed by a DrawableGroup with empty Children. The portal pulls every active Element in the inclusive ZIndex span rigZIndex+1..rigZIndex+Count (a span of rows, not an element count) out of normal composition into the flow and the DrawableGroup consumes them as children, so animating the rig's TransformGroup (eased push-in + pan here) moves the whole shot while each content Element keeps its own Start/Length. Keep the grouped layers ZIndex-contiguous directly above the rig and time-aligned with it — pulled Elements render ungrouped whenever the rig Element is not active. The same portal+flow grouping works for the other IFlowOperators: DrawableDecorator, SoundGroup (audio), and Scene3D.",
            new JsonObject
            {
                ["Elements"] = new JsonArray(
                    rigJson,
                    SerializeExampleElement(plateElement),
                    SerializeExampleElement(titleElement))
            });
    }

    private static DeclarativeExample CreateEmptySceneMotionExample()
    {
        Element background = CreateElement(
            "Background plate",
            zIndex: 0,
            new RectShape
            {
                Name = "Midnight gradient",
                Width = { CurrentValue = 1920 },
                Height = { CurrentValue = 1080 },
                Fill = { CurrentValue = CreateLinearGradient("#ff06121f", "#ff123f63") }
            });

        Element ribbon = CreateElement(
            "Animated ribbon",
            zIndex: 4,
            new RectShape
            {
                Name = "Diagonal gradient ribbon",
                Width = { CurrentValue = 1180 },
                Height = { CurrentValue = 86 },
                Fill = { CurrentValue = CreateLinearGradient("#ff20d6ff", "#ffffe28a") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-120, 38),
                            new RotationTransform(-10)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(5),
                            CreateBrightness(112)
                        }
                    }
                }
            });

        Element title = CreateElement(
            "Visible title",
            zIndex: 20,
            new TextBlock
            {
                Name = "Hero title",
                Text = { CurrentValue = "Beutl motion" },
                Size = { CurrentValue = 116 },
                Spacing = { CurrentValue = 8 },
                Fill = { CurrentValue = new SolidColorBrush(Colors.White) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(0, -16)
                        }
                    }
                }
            });

        JsonObject backgroundJson = SerializeExampleElement(background);
        JsonObject ribbonJson = SerializeExampleElement(ribbon);
        JsonObject titleJson = SerializeExampleElement(title);

        JsonObject ribbonObject = GetFirstObjectJson(ribbonJson);
        AddFloatAnimation(ribbonObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.2, 100, typeof(CubicEaseOut)), (8, 100, typeof(SineEaseInOut)));
        JsonObject ribbonTranslate = GetTransformChildJson(ribbonObject, typeof(TranslateTransform));
        AddFloatAnimation(ribbonTranslate, nameof(TranslateTransform.X), (0, -460, typeof(CubicEaseOut)), (4, 130, typeof(SineEaseInOut)), (8, 520, typeof(SineEaseInOut)));

        JsonObject titleObject = GetFirstObjectJson(titleJson);
        AddFloatAnimation(titleObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.85, 100, typeof(CubicEaseOut)), (6.8, 100, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));
        AddFloatAnimation(titleObject, nameof(TextBlock.Spacing), (0, 22, typeof(CubicEaseOut)), (1.4, 8, typeof(SineEaseInOut)), (8, 14, typeof(SineEaseInOut)));

        return new DeclarativeExample(
            "create-empty-scene-motion-graphics",
            "Patch snippet for an empty scene. Use as a schema reference or explicit starter; for original creative briefs, adapt the structure instead of copying it unchanged. It appends visible elements without Id fields so the toolkit mints stable Ids.",
            new JsonObject
            {
                ["Duration"] = TimeSpan.FromSeconds(8).ToString("c"),
                ["Elements"] = new JsonArray(
                    backgroundJson,
                    ribbonJson,
                    titleJson)
            });
    }

    private static DeclarativeExample CreateOrbitalRadarExample()
    {
        Element background = CreateElement(
            "Orbital radar background",
            zIndex: 0,
            new RectShape
            {
                Name = "Deep teal field",
                Width = { CurrentValue = 1920 },
                Height = { CurrentValue = 1080 },
                Fill = { CurrentValue = CreateLinearGradient("#ff020711", "#ff063835") }
            });

        Element outerRing = CreateElement(
            "Orbital radar outer ring",
            zIndex: 4,
            new EllipseShape
            {
                Name = "Cyan orbit ring",
                Width = { CurrentValue = 720 },
                Height = { CurrentValue = 720 },
                Fill = { CurrentValue = null },
                Pen = { CurrentValue = CreatePen("#ff43e7ff", 7) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-270, 0),
                            new RotationTransform(0)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(1.5f),
                            CreateDropShadow(0, 0, 16, "#aa43e7ff")
                        }
                    }
                }
            });

        Element innerRing = CreateElement(
            "Orbital radar amber ring",
            zIndex: 5,
            new EllipseShape
            {
                Name = "Amber offset ring",
                Width = { CurrentValue = 430 },
                Height = { CurrentValue = 430 },
                Fill = { CurrentValue = null },
                Pen = { CurrentValue = CreatePen("#ffffd36b", 4) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-270, 0),
                            new RotationTransform(0)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(0.8f)
                        }
                    }
                }
            });

        Element signalNode = CreateElement(
            "Orbital radar signal node",
            zIndex: 9,
            new EllipseShape
            {
                Name = "Moving signal node",
                Width = { CurrentValue = 86 },
                Height = { CurrentValue = 86 },
                Fill = { CurrentValue = CreateLinearGradient("#ffff4da3", "#ff36f0ff") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-620, -230)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateDropShadow(0, 0, 22, "#cc36f0ff"),
                            CreateBrightness(118)
                        }
                    }
                }
            });

        Element sweep = CreateElement(
            "Orbital radar sweep",
            zIndex: 7,
            new RectShape
            {
                Name = "Thin scanning sweep",
                Width = { CurrentValue = 820 },
                Height = { CurrentValue = 10 },
                Fill = { CurrentValue = CreateLinearGradient("#0036f0ff", "#dd36f0ff") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-270, 0),
                            new RotationTransform(0)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(2)
                        }
                    }
                }
            });

        Element title = CreateElement(
            "Orbital radar title",
            zIndex: 20,
            new TextBlock
            {
                Name = "Orbital title",
                Text = { CurrentValue = "Orbit map" },
                Size = { CurrentValue = 92 },
                Spacing = { CurrentValue = 10 },
                Fill = { CurrentValue = new SolidColorBrush(Colors.White) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(420, -72)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateDropShadow(10, 16, 12, "#aa000000")
                        }
                    }
                }
            });

        Element subtitle = CreateElement(
            "Orbital radar caption",
            zIndex: 21,
            new TextBlock
            {
                Name = "Orbital caption",
                Text = { CurrentValue = "Signal route notes" },
                Size = { CurrentValue = 34 },
                Spacing = { CurrentValue = 5 },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#ffc9faff")) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(420, 20)
                        }
                    }
                }
            });

        JsonObject outerRingJson = SerializeExampleElement(outerRing);
        JsonObject innerRingJson = SerializeExampleElement(innerRing);
        JsonObject signalNodeJson = SerializeExampleElement(signalNode);
        JsonObject sweepJson = SerializeExampleElement(sweep);
        JsonObject titleJson = SerializeExampleElement(title);
        JsonObject subtitleJson = SerializeExampleElement(subtitle);

        JsonObject outerObject = GetFirstObjectJson(outerRingJson);
        AddFloatAnimation(outerObject, nameof(Drawable.Opacity), (0, 20, typeof(CubicEaseOut)), (1.4, 100, typeof(CubicEaseOut)), (8, 72, typeof(SineEaseInOut)));
        AddFloatAnimation(outerObject, nameof(EllipseShape.Width), (0, 640, typeof(CubicEaseOut)), (4, 780, typeof(SineEaseInOut)), (8, 700, typeof(SineEaseInOut)));
        AddFloatAnimation(outerObject, nameof(EllipseShape.Height), (0, 640, typeof(CubicEaseOut)), (4, 780, typeof(SineEaseInOut)), (8, 700, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(outerObject, typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, 0, typeof(CubicEaseOut)), (8, 360, typeof(SineEaseInOut)));

        JsonObject innerObject = GetFirstObjectJson(innerRingJson);
        AddFloatAnimation(innerObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1, 88, typeof(CubicEaseOut)), (8, 46, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(innerObject, typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, 18, typeof(CubicEaseOut)), (8, -210, typeof(SineEaseInOut)));

        JsonObject signalObject = GetFirstObjectJson(signalNodeJson);
        AddFloatAnimation(signalObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.7, 100, typeof(CubicEaseOut)), (7.5, 100, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));
        JsonObject signalTranslate = GetTransformChildJson(signalObject, typeof(TranslateTransform));
        AddFloatAnimation(signalTranslate, nameof(TranslateTransform.X), (0, -620, typeof(CubicEaseOut)), (3.4, -160, typeof(SineEaseInOut)), (8, 140, typeof(SineEaseInOut)));
        AddFloatAnimation(signalTranslate, nameof(TranslateTransform.Y), (0, -230, typeof(CubicEaseOut)), (3.4, 230, typeof(SineEaseInOut)), (8, -80, typeof(SineEaseInOut)));

        JsonObject sweepObject = GetFirstObjectJson(sweepJson);
        AddFloatAnimation(sweepObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.2, 70, typeof(CubicEaseOut)), (8, 0, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(sweepObject, typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, -18, typeof(CubicEaseOut)), (8, 205, typeof(SineEaseInOut)));

        JsonObject titleObject = GetFirstObjectJson(titleJson);
        AddFloatAnimation(titleObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1, 100, typeof(CubicEaseOut)), (7, 100, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));
        AddFloatAnimation(titleObject, nameof(TextBlock.Spacing), (0, 22, typeof(CubicEaseOut)), (1.5, 10, typeof(SineEaseInOut)), (8, 16, typeof(SineEaseInOut)));

        JsonObject subtitleObject = GetFirstObjectJson(subtitleJson);
        AddFloatAnimation(subtitleObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.4, 90, typeof(CubicEaseOut)), (7.2, 90, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));

        return new DeclarativeExample(
            "create-empty-scene-orbital-radar",
            "Patch snippet for an empty scene with orbit rings, a moving signal node, scan sweep, glow, pens, gradients, and title typography. Use only when the user explicitly asks for an orbit/radar style; otherwise treat it as a shape/effect reference, not a full-scene starter.",
            new JsonObject
            {
                ["Duration"] = TimeSpan.FromSeconds(8).ToString("c"),
                ["Elements"] = new JsonArray(
                    SerializeExampleElement(background),
                    outerRingJson,
                    innerRingJson,
                    signalNodeJson,
                    sweepJson,
                    titleJson,
                    subtitleJson)
            });
    }

    private static DeclarativeExample CreateSplitScreenTypographyExample()
    {
        Element background = CreateElement(
            "Split typography background",
            zIndex: 0,
            new RectShape
            {
                Name = "Blue violet field",
                Width = { CurrentValue = 1920 },
                Height = { CurrentValue = 1080 },
                Fill = { CurrentValue = CreateLinearGradient("#ff071225", "#ff2e1446") },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateSaturate(115),
                            CreateHueRotate(4)
                        }
                    }
                }
            });

        Element panel = CreateElement(
            "Split typography panel",
            zIndex: 3,
            new RoundedRectShape
            {
                Name = "Left editorial panel",
                Width = { CurrentValue = 760 },
                Height = { CurrentValue = 620 },
                CornerRadius = { CurrentValue = new CornerRadius(54) },
                Fill = { CurrentValue = CreateLinearGradient("#eeffffff", "#aa6cf3ff") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-420, 0)
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
                            CreateDropShadow(24, 34, 18, "#99000000")
                        }
                    }
                }
            });

        Element headline = CreateElement(
            "Split typography headline",
            zIndex: 12,
            new TextBlock
            {
                Name = "Stacked headline",
                Text = { CurrentValue = "Frame flow" },
                Size = { CurrentValue = 96 },
                Spacing = { CurrentValue = 4 },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#ff081225")) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-420, -72)
                        }
                    }
                }
            });

        Element caption = CreateElement(
            "Split typography caption",
            zIndex: 13,
            new TextBlock
            {
                Name = "Panel caption",
                Text = { CurrentValue = "Kinetic layout notes" },
                Size = { CurrentValue = 28 },
                Spacing = { CurrentValue = 3 },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#ff10223c")) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-420, 48)
                        }
                    }
                }
            });

        Element wideBlock = CreateElement(
            "Split typography wide block",
            zIndex: 7,
            new RectShape
            {
                Name = "Right cyan block",
                Width = { CurrentValue = 680 },
                Height = { CurrentValue = 96 },
                Fill = { CurrentValue = CreateLinearGradient("#ff34e6ff", "#fffff072") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(520, -200)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBrightness(116),
                            CreateDropShadow(18, 20, 14, "#88000000")
                        }
                    }
                }
            });

        Element tallBlock = CreateElement(
            "Split typography vertical block",
            zIndex: 8,
            new RectShape
            {
                Name = "Magenta vertical block",
                Width = { CurrentValue = 92 },
                Height = { CurrentValue = 520 },
                Fill = { CurrentValue = CreateLinearGradient("#ffff4aa8", "#ff6b7cff") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(270, 130)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(1.2f)
                        }
                    }
                }
            });

        Element label = CreateElement(
            "Split typography right label",
            zIndex: 18,
            new TextBlock
            {
                Name = "Right label",
                Text = { CurrentValue = "Variant 02" },
                Size = { CurrentValue = 54 },
                Spacing = { CurrentValue = 12 },
                Fill = { CurrentValue = new SolidColorBrush(Colors.White) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(520, 96)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateDropShadow(8, 10, 10, "#aa000000")
                        }
                    }
                }
            });

        JsonObject panelJson = SerializeExampleElement(panel);
        JsonObject headlineJson = SerializeExampleElement(headline);
        JsonObject captionJson = SerializeExampleElement(caption);
        JsonObject wideBlockJson = SerializeExampleElement(wideBlock);
        JsonObject tallBlockJson = SerializeExampleElement(tallBlock);
        JsonObject labelJson = SerializeExampleElement(label);

        JsonObject panelObject = GetFirstObjectJson(panelJson);
        AddFloatAnimation(panelObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.7, 100, typeof(CubicEaseOut)), (8, 100, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(panelObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, -620, typeof(CubicEaseOut)), (1.1, -420, typeof(CubicEaseOut)), (8, -380, typeof(SineEaseInOut)));

        JsonObject headlineObject = GetFirstObjectJson(headlineJson);
        AddFloatAnimation(headlineObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1, 100, typeof(CubicEaseOut)), (7.2, 100, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));
        AddFloatAnimation(headlineObject, nameof(TextBlock.Spacing), (0, 26, typeof(CubicEaseOut)), (1.6, 4, typeof(SineEaseInOut)), (8, 10, typeof(SineEaseInOut)));

        JsonObject captionObject = GetFirstObjectJson(captionJson);
        AddFloatAnimation(captionObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.5, 92, typeof(CubicEaseOut)), (8, 92, typeof(SineEaseInOut)));

        JsonObject wideObject = GetFirstObjectJson(wideBlockJson);
        AddFloatAnimation(wideObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.9, 100, typeof(CubicEaseOut)), (8, 80, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(wideObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, 880, typeof(CubicEaseOut)), (1.4, 520, typeof(CubicEaseOut)), (8, 460, typeof(SineEaseInOut)));

        JsonObject tallObject = GetFirstObjectJson(tallBlockJson);
        AddFloatAnimation(tallObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.2, 88, typeof(CubicEaseOut)), (8, 88, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(tallObject, typeof(TranslateTransform)), nameof(TranslateTransform.Y), (0, 420, typeof(CubicEaseOut)), (1.7, 130, typeof(CubicEaseOut)), (8, 190, typeof(SineEaseInOut)));

        JsonObject labelObject = GetFirstObjectJson(labelJson);
        AddFloatAnimation(labelObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.8, 100, typeof(CubicEaseOut)), (7.2, 100, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));

        return new DeclarativeExample(
            "create-empty-scene-split-screen-typography",
            "Patch snippet for an empty scene with a split-screen editorial layout, animated panels, kinetic typography, blocks, gradients, and layered effects. Use as an explicit starter or adapt the parts into an original brief-driven composition.",
            new JsonObject
            {
                ["Duration"] = TimeSpan.FromSeconds(8).ToString("c"),
                ["Elements"] = new JsonArray(
                    SerializeExampleElement(background),
                    panelJson,
                    headlineJson,
                    captionJson,
                    wideBlockJson,
                    tallBlockJson,
                    labelJson)
            });
    }

    private static DeclarativeExample CreateBrushAndEffectExample()
    {
        var brush = new LinearGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x1a, 0xd8, 0xff), 0),
                new GradientStop(Color.FromRgb(0xff, 0x45, 0xb5), 1)
            }
        };

        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(8, 8);
        var brightness = new Brightness();
        brightness.Amount.CurrentValue = 115;
        var effects = new FilterEffectGroup
        {
            Children =
            {
                blur,
                brightness
            }
        };

        return new DeclarativeExample(
            "apply-gradient-fill-and-effect-chain",
            "Patch snippet for applying a gradient brush and a filter effect chain to a drawable such as RectShape. Omit Id on new nested objects to insert them; include Ids from read_document to update or delete existing GradientStops and effect Children.",
            new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = "<element-id>",
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = "<drawable-id>",
                        [nameof(Shape.Fill)] = SerializeExampleObject(brush),
                        [nameof(Drawable.FilterEffect)] = SerializeExampleObject(effects)
                    })
                })
            });
    }

    private static Element CreateElement(string name, int zIndex, EngineObject obj)
    {
        var element = new Element
        {
            Name = name,
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(8),
            ZIndex = zIndex
        };
        element.AddObject(obj);
        return element;
    }
}
