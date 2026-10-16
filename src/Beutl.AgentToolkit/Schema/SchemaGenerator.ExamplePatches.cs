using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using static Beutl.AgentToolkit.Schema.SamplePatchBuilder;

namespace Beutl.AgentToolkit.Schema;

public sealed partial class SchemaGenerator
{
    private static readonly TimeSpan s_exampleElementLength = TimeSpan.FromSeconds(8);

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
        JsonObject opacityAnimation = CreateFloatAnimationWithLocalClock(
            (0, 0, typeof(LinearEasing)),
            (0.35, 100, typeof(SineEaseOut)),
            (1.65, 100, typeof(LinearEasing)),
            (2, 0, typeof(LinearEasing)));

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

        Element element = CreateElement("[role:camera-rig] Shot 1 rig", zIndex: 10, s_exampleElementLength, rig);
        JsonObject elementJson = SerializeWithoutIds(element);
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
        Element rigElement = CreateElement("[role:camera-rig] Shot 2 rig", zIndex: 10, s_exampleElementLength, rig);
        ((PortalObject)rigElement.Objects[0]).Count.CurrentValue = 2;

        Element plateElement = CreateElement(
            "[role:text-backing] Shot 2 title plate",
            zIndex: 11,
            s_exampleElementLength,
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
            s_exampleElementLength,
            new TextBlock
            {
                Name = "Shot 2 hero title",
                Text = { CurrentValue = "Camera move" },
                Size = { CurrentValue = 96 },
                Fill = { CurrentValue = new SolidColorBrush(Colors.White) }
            });

        JsonObject rigJson = SerializeWithoutIds(rigElement);
        AddCameraRigPushInAnimations(GetFlowGroupJson(rigJson));

        return new DeclarativeExample(
            "insert-camera-rig-portal",
            "Camera (viewpoint) move for a 2D shot, timeline-flow variant — preferred when grouped content needs its own Element timing or should stay visible as timeline layers. The shot's content stays as ordinary one-object Elements on contiguous ZIndex rows (11 and 12 here), and the rig Element directly below them (ZIndex 10) holds a PortalObject with Count=2 followed by a DrawableGroup with empty Children. The portal pulls every active Element in the inclusive ZIndex span rigZIndex+1..rigZIndex+Count (a span of rows, not an element count) out of normal composition into the flow and the DrawableGroup consumes them as children, so animating the rig's TransformGroup (eased push-in + pan here) moves the whole shot while each content Element keeps its own Start/Length. Keep the grouped layers ZIndex-contiguous directly above the rig and time-aligned with it — pulled Elements render ungrouped whenever the rig Element is not active. The same portal+flow grouping works for the other IFlowOperators: DrawableDecorator, SoundGroup (audio), and Scene3D.",
            new JsonObject
            {
                ["Elements"] = new JsonArray(
                    rigJson,
                    SerializeWithoutIds(plateElement),
                    SerializeWithoutIds(titleElement))
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

        var effects = new FilterEffectGroup
        {
            Children =
            {
                CreateBlur(8),
                CreateBrightness(115)
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
                        [nameof(Shape.Fill)] = SerializeWithoutIds(brush),
                        [nameof(Drawable.FilterEffect)] = SerializeWithoutIds(effects)
                    })
                })
            });
    }
}
