using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Primitives;
using Beutl.Graphics3D.Skyboxes;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class SkyboxTests
{
    private const int Size = 65;

    [Test]
    public void ProceduralSkybox_UsesWorldZenithNadirAndSunDirection()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var skybox = new ProceduralSkybox
            {
                SkyColor = { Value = Colors.Red },
                GroundColor = { Value = Colors.Blue },
                HorizonColor = { Value = Colors.Black },
                SunDirection = { Value = Vector3.UnitX },
                SunColor = { Value = Colors.White },
                SunIntensity = { Value = 2f },
            };
            using var scene = new TestScene(skybox);
            Look(scene.Camera, -Vector3.UnitY);
            AssertPixel(scene.Render().Center, Colors.Red.ToLinear());
            Look(scene.Camera, Vector3.UnitY);
            AssertPixel(scene.Render().Center, Colors.Blue.ToLinear());
            Look(scene.Camera, -Vector3.UnitX);
            AssertPixel(scene.Render().Center, Colors.Black.ToLinear());
            Look(scene.Camera, Vector3.UnitX);
            Vector4 sun = scene.Render().Center;
            Assert.That(sun.X, Is.GreaterThan(2f), "Sun radiance should retain its HDR value in the scene.");

            Look(scene.Camera, Vector3.UnitX, new Vector3(39, -22, 120));
            AssertPixel(scene.Render().Center, sun);
        });
    }

    [Test]
    public void EquirectangularSkybox_MapsAllDirectionsAndAppliesInverseSkyboxRotation()
    {
        GpuTestEnvironment.EnsureAvailable();
        WithImage(96, 48, (x, y) => y < 12 ? SKColors.Red
            : y >= 36 ? SKColors.Blue
            : x < 12 || x >= 84 ? SKColors.Magenta
            : x < 36 ? SKColors.Lime : x < 60 ? SKColors.Cyan : SKColors.Yellow, image =>
        {
            GpuTestEnvironment.InvokeOnRenderThread(() =>
            {
                var skybox = new EquirectangularSkybox { Texture = { Value = image } };
                using var scene = new TestScene(skybox);
                (Vector3 Direction, Color Expected)[] directions =
                [
                    (-Vector3.UnitY, Colors.Red), (Vector3.UnitY, Colors.Blue),
                    (Vector3.UnitX, Colors.Cyan), (-Vector3.UnitX, Colors.Magenta),
                    (Vector3.UnitZ, Colors.Lime), (-Vector3.UnitZ, Colors.Yellow),
                ];
                foreach (var (worldDirection, expected) in directions)
                {
                    Look(scene.Camera, worldDirection);
                    AssertPixel(scene.Render().Center, expected.ToLinear(), worldDirection.ToString());
                }

                Look(scene.Camera, Vector3.UnitZ);
                skybox.Rotation.Value = new Vector3(0, 90, 0);
                AssertPixel(scene.Render().Center, Colors.Cyan.ToLinear());
            });
        });
    }

    [Test]
    public void EquirectangularSkybox_HorizonPassesThroughAWorldMarkerWithCameraRoll()
    {
        GpuTestEnvironment.EnsureAvailable();
        WithImage(64, 32, (_, y) => y < 16 ? SKColors.Red : SKColors.Blue, image =>
        {
            GpuTestEnvironment.InvokeOnRenderThread(() =>
            {
                using var scene = new TestScene(new EquirectangularSkybox { Texture = { Value = image } });
                Look(scene.Camera, Vector3.UnitZ);
                scene.Camera.Up.Value = Vector3.Normalize(new Vector3(0.43f, -1f, 0f));
                var markerPosition = new Vector3(1, 0, 5);
                using var camera = (Camera3D.Resource)scene.Camera.ToResource(CompositionContext.Default);
                Matrix4x4 viewProjection = camera.GetViewMatrix() * camera.GetProjectionMatrix(1f);
                Vector4 clip = Vector4.Transform(new Vector4(markerPosition, 1), viewProjection);
                int x = (int)((clip.X / clip.W + 1f) * Size / 2);
                int y = (int)((1f - clip.Y / clip.W) * Size / 2);

                Frame panorama = scene.Render();
                Assert.That(panorama.Pixel(x, y).X, Is.InRange(0.2f, 0.8f));
                Assert.That(panorama.Pixel(x, y).Z, Is.InRange(0.2f, 0.8f));
                AssertPixel(panorama.Pixel(x, y - 7), Colors.Red.ToLinear());
                AssertPixel(panorama.Pixel(x, y + 7), Colors.Blue.ToLinear());

                Cube3D marker = MakeCube(0.45f, Colors.Lime);
                marker.Position.Value = markerPosition;
                scene.Scene.Objects.Add(marker);
                Frame withMarker = scene.Render();
                AssertPixel(withMarker.Pixel(x, y), Colors.Lime.ToLinear());
                Assert.That(withMarker.DepthAt(x, y), Is.LessThan(1f));
                Assert.That(withMarker.DepthAt(x, y - 7), Is.EqualTo(1f));
            });
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DrawableTextureSource_UpdatesTheSkyboxWhenTheDrawableChanges(bool cubemap)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            DrawableTextureSource texture = MakeDrawableTexture(Colors.Red);
            var rectangle = (RectShape)texture.Drawable.Value!;
            using var scene = new TestScene(cubemap
                ? new CubemapSkybox { NegativeZ = { Value = texture } }
                : new EquirectangularSkybox { Texture = { Value = texture } });
            Look(scene.Camera, Vector3.UnitZ);
            AssertPixel(scene.Render().Center, Colors.Red.ToLinear());
            rectangle.Fill.Value = new SolidColorBrush(Colors.Lime);
            AssertPixel(scene.Render().Center, Colors.Lime.ToLinear());
        });
    }

    [Test]
    public void CubemapSkybox_BindsSixFacesWithMixedSizesAndFormats()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            CompositionContext composition = CompositionContext.Default;
            using var renderer = IRenderer3D.Create(context);
            renderer.Initialize(Size, Size);
            var camera = new PerspectiveCamera();
            using var skybox = (CubemapSkybox.Resource)new CubemapSkybox().ToResource(composition);
            skybox.PositiveX = new SolidTexture(context, 15, 19, Colors.Red, TextureFormat.BGRA8Unorm);
            skybox.NegativeX = new SolidTexture(context, 24, 24, Colors.Lime, TextureFormat.RGBA16Float);
            skybox.PositiveY = new SolidTexture(context, 16, 9, Colors.Blue, TextureFormat.BGRA8Unorm);
            skybox.NegativeY = new SolidTexture(context, 3, 11, Colors.Yellow, TextureFormat.RGBA16Float);
            skybox.PositiveZ = new SolidTexture(context, 5, 5, Colors.Cyan, TextureFormat.BGRA8Unorm);
            skybox.NegativeZ = new SolidTexture(context, 9, 8, Colors.Magenta, TextureFormat.RGBA16Float);
            (Vector3 Direction, Color Expected)[] directions =
            [
                (Vector3.UnitX, Colors.Red), (-Vector3.UnitX, Colors.Lime),
                (-Vector3.UnitY, Colors.Blue), (Vector3.UnitY, Colors.Yellow),
                (-Vector3.UnitZ, Colors.Cyan), (Vector3.UnitZ, Colors.Magenta),
            ];
            foreach (var (worldDirection, expected) in directions)
                AssertPixel(Render(worldDirection), expected.ToLinear(), worldDirection.ToString());

            // The source can edit a GPU texture in place and signal the change with its resource version.
            var positiveX = (SolidTexture)skybox.PositiveX;
            positiveX.SetColor(Colors.White);
            AssertPixel(Render(Vector3.UnitX), Colors.Red.ToLinear(), "Unchanged source version reuses the uploaded face");
            positiveX.Version++;
            AssertPixel(Render(Vector3.UnitX), Colors.White.ToLinear());

            positiveX.Dispose();
            skybox.PositiveX = null;
            AssertPixel(Render(Vector3.UnitX), Vector4.Zero);
            AssertPixel(Render(-Vector3.UnitX), Colors.Lime.ToLinear());

            Vector4 Render(Vector3 worldDirection)
            {
                Look(camera, worldDirection);
                using var cameraResource = (Camera3D.Resource)camera.ToResource(composition);
                renderer.Render(composition, cameraResource, [], [], Colors.Transparent, Colors.White, 1f,
                    gizmoTarget: null, gizmoMode: GizmoMode.None, skybox);
                Half[] pixels = MemoryMarshal.Cast<byte, Half>(renderer.DownloadPixels()).ToArray();
                return ReadPixel(pixels, Size, Size / 2, Size / 2);
            }
        });
    }

    [Test]
    public void CubemapSkybox_RefreshesAnImageAfterItsUriChanges()
    {
        GpuTestEnvironment.EnsureAvailable();
        WithImage(12, 8, (_, _) => SKColors.Red, first => WithImage(12, 8, (_, _) => SKColors.Blue, second =>
        {
            GpuTestEnvironment.InvokeOnRenderThread(() =>
            {
                using var scene = new TestScene(new CubemapSkybox { NegativeZ = { Value = first } });
                Look(scene.Camera, Vector3.UnitZ);
                AssertPixel(scene.Render().Center, Colors.Red.ToLinear());
                first.Source.Value!.ReadFrom(second.Source.Value!.Uri);
                AssertPixel(scene.Render().Center, Colors.Blue.ToLinear());
            });
        }));
    }

    [Test]
    public void Msaa_UsesSkyboxOnPartiallyCoveredPixelsAndPreservesDepth()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var scene = new TestScene(SolidSky(Colors.Blue), AntiAliasingMode.Msaa4x);
            scene.Scene.BackgroundColor.Value = Colors.Lime;
            Look(scene.Camera, Vector3.UnitZ);
            Cube3D cube = MakeCube(1.75f, Colors.Red);
            cube.Position.Value = new Vector3(0, 0, 5);
            cube.Rotation.Value = new Vector3(0, 0, 26);
            scene.Scene.Objects.Add(cube);

            Frame frame = scene.Render();
            Assert.That(frame.SampleCount, Is.EqualTo(4));
            AssertPixel(frame.Center, Colors.Red.ToLinear());
            AssertPixel(frame.Pixel(3, 3), Colors.Blue.ToLinear());
            Assert.That(frame.DepthAt(3, 3), Is.EqualTo(1f));
            Assert.That(frame.DepthAt(Size / 2, Size / 2), Is.LessThan(1f));

            int partial = 0;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    Vector4 pixel = frame.Pixel(x, y);
                    if (pixel.X > 0.1f && pixel.Z > 0.1f)
                    {
                        partial++;
                        Assert.That(pixel.Y, Is.LessThan(0.005f), $"background color at ({x}, {y})");
                        Assert.That(pixel.X + pixel.Z, Is.EqualTo(1f).Within(0.005f));
                    }
                }
            }
            Assert.That(partial, Is.GreaterThan(20));

            scene.Scene.Skybox.Value = null;
            Assert.That(scene.Render().Depth, Is.EqualTo(frame.Depth));
        });
    }

    [Test]
    public void TransparentMaterial_BlendsOverSkyboxWithoutWritingDepth()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var scene = new TestScene(null);
            Look(scene.Camera, Vector3.UnitZ);
            Cube3D cube = MakeCube(2f, Colors.Red);
            cube.Position.Value = new Vector3(0, 0, 5);
            cube.Material.Value = new TransparentMaterial
            {
                Color = { Value = Colors.Red },
                Opacity = { Value = 0.4f },
            };
            scene.Scene.Objects.Add(cube);
            Vector4 foreground = scene.Render().Center;
            Assert.That(foreground.W, Is.InRange(0.3f, 0.6f));

            scene.Scene.Skybox.Value = SolidSky(Colors.Blue);
            Frame frame = scene.Render();
            AssertPixel(frame.Center, foreground + Colors.Blue.ToLinear() * (1f - foreground.W));
            Assert.That(frame.DepthAt(Size / 2, Size / 2), Is.EqualTo(1f));
        });
    }

    [TestCase(AntiAliasingMode.Off)]
    [TestCase(AntiAliasingMode.Msaa4x)]
    public void Skybox_CompositesTintedPremultipliedTextureOverTheSceneColor(AntiAliasingMode antiAliasing)
    {
        GpuTestEnvironment.EnsureAvailable();
        WithImage(24, 12, (_, _) => new SKColor(255, 255, 0, 128), image =>
        {
            GpuTestEnvironment.InvokeOnRenderThread(() =>
            {
                var skybox = new EquirectangularSkybox
                {
                    Texture = { Value = image },
                    Tint = { Value = Colors.Red },
                    Intensity = { Value = 2f },
                    Opacity = { Value = 0.5f },
                };
                using var scene = new TestScene(skybox, antiAliasing);
                scene.Scene.BackgroundColor.Value = Color.FromArgb(128, 0, 0, 255);
                float alpha = 0.5f * 128f / 255f;
                Vector4 sky = new(2f * alpha, 0, 0, alpha);
                AssertPixel(scene.Render().Center,
                    sky + scene.Scene.BackgroundColor.Value.ToLinearPremultiplied() * (1f - alpha));
            });
        });
    }

    [Test]
    public void OrthographicCamera_UsesAPerspectiveSkyAndFollowsCameraRotation()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var skybox = new ProceduralSkybox
            {
                SkyColor = { Value = Colors.Red },
                HorizonColor = { Value = Colors.Black },
                GroundColor = { Value = Colors.Blue },
                HorizonExponent = { Value = 1f },
                SunIntensity = { Value = 0f },
            };
            using var scene = new TestScene(skybox);
            scene.Scene.Camera.Value = new OrthographicCamera();
            Look(scene.Camera, Vector3.UnitZ);
            Frame horizon = scene.Render();
            Assert.That(horizon.Pixel(Size / 2, 3).X, Is.GreaterThan(0.4f));
            Assert.That(horizon.Pixel(Size / 2, Size - 4).Z, Is.GreaterThan(0.4f));
            AssertPixel(horizon.Center, Colors.Black.ToLinear());

            Look(scene.Camera, -Vector3.UnitY);
            AssertPixel(scene.Render().Center, Colors.Red.ToLinear());
        });
    }

    [Test]
    public void Scene_SwitchesSkyboxesAndResizes()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using (var scene = new TestScene(null))
            {
                scene.Scene.BackgroundColor.Value = Colors.Lime;
                AssertPixel(scene.Render().Center, Colors.Lime.ToLinear());

                ProceduralSkybox skybox = SolidSky(Colors.Red);
                skybox.IsEnabled.Value = false;
                scene.Scene.Skybox.Value = skybox;
                AssertPixel(scene.Render().Center, Colors.Lime.ToLinear());
                skybox.IsEnabled.Value = true;
                AssertPixel(scene.Render().Center, Colors.Red.ToLinear());

                scene.Scene.RenderWidth.Value = 81;
                scene.Scene.RenderHeight.Value = 49;
                AssertPixel(scene.Render().Pixel(79, 47), Colors.Red.ToLinear());

                scene.Scene.Skybox.Value = new EquirectangularSkybox
                {
                    Texture = { Value = MakeDrawableTexture(Colors.Blue) },
                };
                AssertPixel(scene.Render().Center, Colors.Blue.ToLinear());
                scene.Scene.Skybox.Value = skybox;
                skybox.Opacity.Value = 0f;
                AssertPixel(scene.Render().Center, Colors.Lime.ToLinear());

                scene.Scene.Skybox.Value = new EquirectangularSkybox();
                AssertPixel(scene.Render().Center, Colors.Lime.ToLinear(), "Missing panorama");
                scene.Scene.Skybox.Value = new CubemapSkybox();
                AssertPixel(scene.Render().Center, Colors.Lime.ToLinear(), "Missing cubemap");
                scene.Scene.Skybox.Value = null;
                AssertPixel(scene.Render().Center, Colors.Lime.ToLinear());
                scene.Scene.Skybox.Value = SolidSky(Colors.Blue);
                AssertPixel(scene.Render().Center, Colors.Blue.ToLinear());
            }
            context.WaitIdle();
        });
    }

    private static ProceduralSkybox SolidSky(Color color)
        => new()
        {
            SkyColor = { Value = color },
            HorizonColor = { Value = color },
            GroundColor = { Value = color },
            SunIntensity = { Value = 0f },
        };

    private static DrawableTextureSource MakeDrawableTexture(Color color)
        => new()
        {
            Drawable =
            {
                Value = new RectShape
                {
                    Width = { Value = 64 },
                    Height = { Value = 32 },
                    Fill = { Value = new SolidColorBrush(color) },
                },
            },
            TextureWidth = { Value = 64 },
            TextureHeight = { Value = 32 },
        };

    private static Cube3D MakeCube(float size, Color color)
        => new()
        {
            Width = { Value = size },
            Height = { Value = size },
            Depth = { Value = size },
            Material = { Value = new BasicMaterial { DiffuseColor = { Value = color } } },
        };

    private static void Look(Camera3D camera, Vector3 worldDirection, Vector3 position = default)
    {
        camera.Position.Value = position;
        camera.Target.Value = position + worldDirection;
        camera.Up.Value = MathF.Abs(worldDirection.Y) == 1f ? Vector3.UnitZ : -Vector3.UnitY;
    }

    private static void WithImage(int width, int height, Func<int, int, SKColor> color, Action<ImageTextureSource> use)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-skybox-{Guid.NewGuid():N}.png");
        try
        {
            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    bitmap.SetPixel(x, y, color(x, y));
            }
            using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using (FileStream stream = File.Create(path))
                data.SaveTo(stream);

            var image = new ImageSource();
            image.ReadFrom(new Uri(path));
            use(new ImageTextureSource { Source = { Value = image } });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertPixel(Vector4 actual, Vector4 expected, string? label = null)
        => Assert.That(Vector4.Distance(actual, expected), Is.LessThan(0.025f),
            $"{label} Expected {expected}, got {actual}.");

    private static Vector4 ReadPixel(ReadOnlySpan<Half> pixels, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return new Vector4((float)pixels[i], (float)pixels[i + 1], (float)pixels[i + 2], (float)pixels[i + 3]);
    }

    private sealed record Frame(Half[] Color, float[] Depth, PixelSize Size, int SampleCount)
    {
        public Vector4 Center => Pixel(Size.Width / 2, Size.Height / 2);
        public Vector4 Pixel(int x, int y) => ReadPixel(Color, Size.Width, x, y);
        public float DepthAt(int x, int y) => Depth[y * Size.Width + x];
    }

    private sealed class TestScene : IDisposable
    {
        private readonly CompositionContext _context = CompositionContext.Default;
        private readonly Scene3D.Resource _resource;
        private readonly Scene3DRenderNode _node;

        public TestScene(Skybox3D? skybox, AntiAliasingMode antiAliasing = AntiAliasingMode.Off)
        {
            Scene = new Scene3D
            {
                RenderWidth = { Value = Size },
                RenderHeight = { Value = Size },
                Skybox = { Value = skybox },
                AmbientIntensity = { Value = 1f },
                AntiAliasing = { Value = antiAliasing },
            };
            _resource = Scene.ToResource(_context);
            _node = new Scene3DRenderNode(_resource);
        }

        public Scene3D Scene { get; }

        public Camera3D Camera => Scene.Camera.Value!;

        public Frame Render()
        {
            bool changed = false;
            _resource.Reconcile(Scene, _context, ref changed);
            _node.Update(_resource);
            var size = new PixelSize((int)_resource.RenderWidth, (int)_resource.RenderHeight);
            using var renderer = new RenderNodeRenderer(_node, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = new Rect(0, 0, size.Width, size.Height),
                OutputScale = 1,
                MaxWorkingScale = 1,
                CacheOptions = RenderCacheOptions.Disabled,
            });
            using var raster = renderer.Rasterize();
            var depth = new float[size.Width * size.Height];
            Assert.That(_resource.TryCopyDepth(depth, out PixelSize actual), Is.True);
            Assert.That(actual, Is.EqualTo(size));
            Renderer3D gpu = _resource.Renderer!;
            return new Frame(MemoryMarshal.Cast<byte, Half>(gpu.DownloadPixels()).ToArray(), depth, size, gpu.SampleCount);
        }

        public void Dispose()
        {
            _node.Dispose();
            _resource.Dispose();
        }
    }

    private sealed class SolidTexture : TextureSource.Resource
    {
        private readonly ITexture2D _texture;

        public SolidTexture(IGraphicsContext context, int width, int height, Color color, TextureFormat format)
        {
            _texture = context.CreateTexture2D(width, height, format);
            IsEnabled = true;
            SetColor(color);
        }

        public override ITexture2D GetTexture(IGraphicsContext graphicsContext, float surfaceDensity = 1f)
            => _texture;

        public void SetColor(Color color)
        {
            int count = _texture.Width * _texture.Height;
            if (_texture.Format == TextureFormat.BGRA8Unorm)
            {
                byte[] pixel = [color.B, color.G, color.R, color.A];
                var bytes = new byte[count * 4];
                for (int i = 0; i < count; i++)
                    pixel.CopyTo(bytes, i * 4);
                _texture.Upload(bytes);
            }
            else
            {
                Vector4 linear = color.ToLinearPremultiplied();
                Half[] pixel = [(Half)linear.X, (Half)linear.Y, (Half)linear.Z, (Half)linear.W];
                var halfs = new Half[count * 4];
                for (int i = 0; i < count; i++)
                    pixel.CopyTo(halfs, i * 4);
                _texture.Upload(MemoryMarshal.AsBytes(halfs.AsSpan()));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _texture.Dispose();
            base.Dispose(disposing);
        }
    }
}
