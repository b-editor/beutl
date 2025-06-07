using System.Numerics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.OpenGL;
using NUnit.Framework;

namespace Beutl.UnitTests.Graphics;

[TestFixture]
public class Rendering3DTests
{
    [Test]
    public void Renderer3DFactory_ShouldReturnSupportedBackends()
    {
        // Arrange
        var factory = Renderer3DFactory.Instance;

        // Act
        var backends = factory.SupportedBackends;

        // Assert
        Assert.That(backends, Is.Not.Null);
        Assert.That(backends.Count, Is.GreaterThan(0));
        Assert.That(backends, Contains.Item("OpenGL"));
    }

    [Test]
    public void Camera3D_ShouldCalculateViewMatrix()
    {
        // Arrange
        var camera = new Camera3D
        {
            Position = new Vector3(0, 0, 5),
            Target = Vector3.Zero,
            Up = Vector3.UnitY
        };

        // Act
        var viewMatrix = camera.ViewMatrix;

        // Assert
        Assert.That(viewMatrix, Is.Not.EqualTo(Matrix4x4.Identity));
        
        // カメラが正しい方向を向いているかチェック
        var forward = camera.Forward;
        var expectedForward = Vector3.Normalize(Vector3.Zero - new Vector3(0, 0, 5));
        Assert.That(forward.X, Is.EqualTo(expectedForward.X).Within(0.001f));
        Assert.That(forward.Y, Is.EqualTo(expectedForward.Y).Within(0.001f));
        Assert.That(forward.Z, Is.EqualTo(expectedForward.Z).Within(0.001f));
    }

    [Test]
    public void Camera3D_ShouldCalculateProjectionMatrix()
    {
        // Arrange
        var camera = new Camera3D
        {
            FieldOfView = MathF.PI / 3, // 60度
            AspectRatio = 16f / 9f,
            NearClip = 0.1f,
            FarClip = 100f
        };

        // Act
        var projectionMatrix = camera.ProjectionMatrix;

        // Assert
        Assert.That(projectionMatrix, Is.Not.EqualTo(Matrix4x4.Identity));
        
        // 透視投影行列の特性をチェック
        Assert.That(projectionMatrix.M33, Is.LessThan(0)); // Z軸の符号
        Assert.That(projectionMatrix.M44, Is.EqualTo(0));   // 透視投影の特徴
    }

    [Test]
    public void BasicMesh_CreateCube_ShouldHaveCorrectVertexCount()
    {
        // Act
        var cubeMesh = BasicMesh.CreateCube();

        // Assert
        Assert.That(cubeMesh.Vertices.Length, Is.EqualTo(24)); // 6面 × 4頂点
        Assert.That(cubeMesh.Normals.Length, Is.EqualTo(24));
        Assert.That(cubeMesh.TexCoords.Length, Is.EqualTo(24));
        Assert.That(cubeMesh.Indices.Length, Is.EqualTo(36)); // 6面 × 6インデックス
    }

    [Test]
    public void BasicMesh_CreateSphere_ShouldHaveCorrectVertexCount()
    {
        // Arrange
        int segments = 16;
        int rings = 8;

        // Act
        var sphereMesh = BasicMesh.CreateSphere(1.0f, segments, rings);

        // Assert
        int expectedVertexCount = (rings + 1) * (segments + 1);
        Assert.That(sphereMesh.Vertices.Length, Is.EqualTo(expectedVertexCount));
        Assert.That(sphereMesh.Normals.Length, Is.EqualTo(expectedVertexCount));
        Assert.That(sphereMesh.TexCoords.Length, Is.EqualTo(expectedVertexCount));
        
        int expectedIndexCount = rings * segments * 6;
        Assert.That(sphereMesh.Indices.Length, Is.EqualTo(expectedIndexCount));
    }

    [Test]
    public void BasicMaterial_DefaultValues_ShouldBeCorrect()
    {
        // Act
        var material = new BasicMaterial();

        // Assert
        Assert.That(material.Albedo, Is.EqualTo(Vector3.One));
        Assert.That(material.Metallic, Is.EqualTo(0.0f));
        Assert.That(material.Roughness, Is.EqualTo(0.5f));
        Assert.That(material.Emission, Is.EqualTo(Vector3.Zero));
        Assert.That(material.AlbedoTexture, Is.Null);
        Assert.That(material.NormalTexture, Is.Null);
        Assert.That(material.MetallicRoughnessTexture, Is.Null);
    }

    [Test]
    public void BasicMaterial_CreateMetal_ShouldHaveCorrectProperties()
    {
        // Arrange
        var color = new Vector3(0.8f, 0.2f, 0.1f);
        float roughness = 0.1f;

        // Act
        var material = BasicMaterial.CreateMetal(color, roughness);

        // Assert
        Assert.That(material.Albedo, Is.EqualTo(color));
        Assert.That(material.Metallic, Is.EqualTo(1.0f));
        Assert.That(material.Roughness, Is.EqualTo(roughness));
    }

    [Test]
    public void DirectionalLight_DefaultValues_ShouldBeCorrect()
    {
        // Act
        var light = new DirectionalLight();

        // Assert
        Assert.That(light.Type, Is.EqualTo(LightType.Directional));
        Assert.That(light.Color, Is.EqualTo(Vector3.One));
        Assert.That(light.Intensity, Is.EqualTo(1.0f));
        Assert.That(light.Enabled, Is.True);
        Assert.That(light.CastShadows, Is.True);
        Assert.That(light.Direction, Is.EqualTo(new Vector3(0, -1, 0)));
    }

    [Test]
    public void PointLight_DefaultValues_ShouldBeCorrect()
    {
        // Act
        var light = new PointLight();

        // Assert
        Assert.That(light.Type, Is.EqualTo(LightType.Point));
        Assert.That(light.Position, Is.EqualTo(Vector3.Zero));
        Assert.That(light.Range, Is.EqualTo(10.0f));
        Assert.That(light.AttenuationConstant, Is.EqualTo(1.0f));
    }

    [Test]
    public void RenderPipelineSettings_DefaultValues_ShouldBeCorrect()
    {
        // Act
        var settings = new RenderPipelineSettings();

        // Assert
        Assert.That(settings.EnableDeferred, Is.True);
        Assert.That(settings.EnableShadows, Is.True);
        Assert.That(settings.EnableEnvironmentMapping, Is.True);
        Assert.That(settings.ShadowMapSize, Is.EqualTo(2048));
        Assert.That(settings.MaxLights, Is.EqualTo(32));
        Assert.That(settings.ShadowBias, Is.EqualTo(0.001f));
    }

    [Test]
    public void LightingEnvironment_AddLight_ShouldIncreaseCount()
    {
        // Arrange
        var environment = new LightingEnvironment();
        var light = new DirectionalLight();

        // Act
        environment.AddLight(light);

        // Assert
        Assert.That(environment.Lights.Count, Is.EqualTo(1));
        Assert.That(environment.Lights[0], Is.SameAs(light));
    }

    [Test]
    public void LightingEnvironment_GetActiveLights_ShouldReturnOnlyEnabledLights()
    {
        // Arrange
        var environment = new LightingEnvironment();
        var enabledLight = new DirectionalLight { Enabled = true };
        var disabledLight = new PointLight { Enabled = false };
        
        environment.AddLight(enabledLight);
        environment.AddLight(disabledLight);

        // Act
        var activeLights = environment.GetActiveLights().ToList();

        // Assert
        Assert.That(activeLights.Count, Is.EqualTo(1));
        Assert.That(activeLights[0], Is.SameAs(enabledLight));
    }

    [Test]
    public void Camera3D_OrbitAroundTarget_ShouldMaintainDistance()
    {
        // Arrange
        var camera = new Camera3D
        {
            Position = new Vector3(0, 0, 5),
            Target = Vector3.Zero
        };
        
        float originalDistance = Vector3.Distance(camera.Position, camera.Target);

        // Act
        camera.OrbitAroundTarget(MathF.PI / 4, 0); // 45度回転

        // Assert
        float newDistance = Vector3.Distance(camera.Position, camera.Target);
        Assert.That(newDistance, Is.EqualTo(originalDistance).Within(0.001f));
    }

    [Test]
    public void Camera3D_Zoom_ShouldChangeFOV()
    {
        // Arrange
        var camera = new Camera3D
        {
            FieldOfView = MathF.PI / 3 // 60度
        };
        
        float originalFOV = camera.FieldOfView;

        // Act
        camera.Zoom(-0.1f); // ズームイン

        // Assert
        Assert.That(camera.FieldOfView, Is.LessThan(originalFOV));
        Assert.That(camera.FieldOfView, Is.GreaterThan(0.1f)); // 最小値チェック
    }

    [Test]
    public void PbrMaterialPresets_GetAvailableMaterials_ShouldReturnKnownMaterials()
    {
        // Act
        var materials = PbrMaterialPresets.GetAvailableMaterials();

        // Assert
        Assert.That(materials, Is.Not.Null);
        Assert.That(materials.Count, Is.GreaterThan(0));
        Assert.That(materials, Contains.Item("Water"));
        Assert.That(materials, Contains.Item("Gold"));
        Assert.That(materials, Contains.Item("Chrome"));
    }

    [Test]
    public void PbrMaterialPresets_CreateMaterial_ShouldReturnCorrectMaterial()
    {
        // Act
        var goldMaterial = PbrMaterialPresets.CreateMaterial("Gold");

        // Assert
        Assert.That(goldMaterial, Is.Not.Null);
        Assert.That(goldMaterial.Metallic, Is.GreaterThan(0.5f)); // ゴールドは金属
        Assert.That(goldMaterial.Albedo.X, Is.GreaterThan(0.5f)); // ゴールドは黄色系
    }
}

[TestFixture]
public class Rendering3DIntegrationTests
{
    [Test]
    public void Rendering3DManager_Initialize_ShouldSucceedWithValidBackend()
    {
        // Arrange
        var manager = new Rendering3DManager();

        try
        {
            // Act
            bool result = manager.Initialize("OpenGL");

            // Assert
            if (result) // OpenGLが利用可能な場合のみテスト
            {
                Assert.That(manager.IsInitialized, Is.True);
                Assert.That(manager.CurrentBackendName, Is.EqualTo("OpenGL"));
                Assert.That(manager.CurrentRenderer, Is.Not.Null);
            }
            else
            {
                Assert.That(manager.IsInitialized, Is.False);
            }
        }
        finally
        {
            // Cleanup
            manager.Dispose();
        }
    }

    [Test]
    public void Renderer3DFactory_CreateBestAvailableRenderer_ShouldReturnRenderer()
    {
        // Arrange
        var factory = Renderer3DFactory.Instance;

        // Act
        using var renderer = factory.CreateBestAvailableRenderer();

        // Assert
        // 環境によってはレンダラーが利用できない場合があるので、NullでもOK
        if (renderer != null)
        {
            Assert.That(renderer.Name, Is.Not.Null.And.Not.Empty);
        }
    }
}