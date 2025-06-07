using System.Numerics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.OpenGL;
using Microsoft.Extensions.Logging;

namespace Beutl3DTest;

public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private Rendering3DManager? _renderingManager;
    private I3DRenderer? _renderer;
    private TestScene? _testScene;
    private Camera3D? _camera;
    private DispatcherTimer? _renderTimer;
    private readonly Stopwatch _frameStopwatch = new();
    private int _frameCount;

    public MainWindow()
    {
        InitializeComponent();
        
        // ロガーを初期化（簡略化）
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<MainWindow>();
        
        InitializeUI();
        SetupRenderTimer();
    }

    private void InitializeUI()
    {
        // 利用可能なバックエンドを表示
        var factory = Renderer3DFactory.Instance;
        foreach (string backend in factory.SupportedBackends)
        {
            BackendComboBox.Items.Add(backend);
        }
        
        if (BackendComboBox.Items.Count > 0)
        {
            BackendComboBox.SelectedIndex = 0;
        }
    }

    private void SetupRenderTimer()
    {
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
        };
        _renderTimer.Tick += RenderTimer_Tick;
    }

    private void InitializeButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Initializing...";
            
            // 3Dレンダリングマネージャーを初期化
            _renderingManager = Rendering3DManager.Instance;
            string? selectedBackend = BackendComboBox.SelectedItem?.ToString();
            
            bool success = _renderingManager.Initialize(selectedBackend);
            
            if (success)
            {
                _renderer = _renderingManager.CurrentRenderer;
                SetupTestScene();
                
                StatusText.Text = $"Initialized with {_renderingManager.CurrentBackendName}";
                RenderSceneButton.IsEnabled = true;
                InitializeButton.IsEnabled = false;
                
                UpdateRendererInfo();
                
                _logger.LogInformation("3D rendering system initialized successfully");
            }
            else
            {
                StatusText.Text = "Initialization failed";
                _logger.LogError("Failed to initialize 3D rendering system");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            _logger.LogError(ex, "Exception during 3D initialization");
        }
    }

    private void SetupTestScene()
    {
        if (_renderer == null)
            return;

        // テストシーンを作成
        _testScene = new TestScene(_renderer);
        
        // カメラを設定
        _camera = new Camera3D
        {
            Position = new Vector3(0, 2, 5),
            Target = Vector3.Zero,
            FieldOfView = MathF.PI / 3,
            AspectRatio = (float)RenderCanvas.Bounds.Width / (float)RenderCanvas.Bounds.Height,
            NearClip = 0.1f,
            FarClip = 100f
        };
    }

    private void RenderSceneButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_renderTimer?.IsEnabled == true)
        {
            // レンダリングを停止
            _renderTimer.Stop();
            RenderSceneButton.Content = "Start Rendering";
            StatusText.Text = "Rendering stopped";
        }
        else
        {
            // レンダリングを開始
            _renderTimer?.Start();
            RenderSceneButton.Content = "Stop Rendering";
            StatusText.Text = "Rendering...";
            _frameStopwatch.Start();
        }
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_renderingManager == null || _testScene == null || _camera == null)
                return;

            // シーンを更新
            _testScene.Update();

            // レンダーターゲットを作成（実際の実装では再利用）
            var renderTarget = _renderingManager.CreateRenderTarget(
                (int)RenderCanvas.Bounds.Width, 
                (int)RenderCanvas.Bounds.Height);

            if (renderTarget != null)
            {
                // シーンをレンダリング
                _renderingManager.RenderScene(_testScene, _camera, renderTarget, useDeferred: true);
                
                // フレーム統計を更新
                UpdateFrameStats();
                
                renderTarget.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rendering");
        }
    }

    private void UpdateRendererInfo()
    {
        if (_renderingManager != null)
        {
            var stats = _renderingManager.GetStatistics();
            RendererInfo.Text = $"Renderer: {stats.BackendName}";
        }
    }

    private void UpdateFrameStats()
    {
        _frameCount++;
        
        if (_frameStopwatch.ElapsedMilliseconds >= 1000)
        {
            double fps = _frameCount / (_frameStopwatch.ElapsedMilliseconds / 1000.0);
            FrameInfo.Text = $"FPS: {fps:F1}";
            
            if (_testScene != null)
            {
                StatsInfo.Text = $"Triangles: {_testScene.TriangleCount}";
            }
            
            _frameCount = 0;
            _frameStopwatch.Restart();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _renderTimer?.Stop();
        _renderingManager?.Shutdown();
        base.OnClosed(e);
    }
}

/// <summary>
/// テスト用の3Dシーン
/// </summary>
public class TestScene : I3DScene
{
    private readonly I3DRenderer _renderer;
    private readonly List<TestRenderableObject> _objects = [];
    private readonly List<ILight> _lights = [];
    private float _animationTime;

    public IReadOnlyList<I3DRenderableObject> Objects => _objects.Cast<I3DRenderableObject>().ToList();
    public IReadOnlyList<ILight> Lights => _lights;
    public IEnvironmentMap? EnvironmentMap => null;
    public int TriangleCount => _objects.Sum(obj => obj.TriangleCount);

    public TestScene(I3DRenderer renderer)
    {
        _renderer = renderer;
        CreateTestObjects();
        CreateTestLights();
    }

    private void CreateTestObjects()
    {
        // キューブを作成
        var cubeMesh = _renderer.CreateMesh(BasicMesh.CreateCube());
        var cubeMaterial = _renderer.CreateMaterial(BasicMaterial.CreateMetal(new Vector3(0.8f, 0.2f, 0.2f), 0.1f));
        _objects.Add(new TestRenderableObject
        {
            Mesh = cubeMesh,
            Material = cubeMaterial,
            Transform = Matrix4x4.CreateTranslation(-2, 0, 0),
            TriangleCount = 12
        });

        // スフィアを作成
        var sphereMesh = _renderer.CreateMesh(BasicMesh.CreateSphere(1.0f, 32, 16));
        var sphereMaterial = _renderer.CreateMaterial(BasicMaterial.CreateDielectric(new Vector3(0.2f, 0.8f, 0.2f), 0.3f));
        _objects.Add(new TestRenderableObject
        {
            Mesh = sphereMesh,
            Material = sphereMaterial,
            Transform = Matrix4x4.CreateTranslation(0, 0, 0),
            TriangleCount = 32 * 16 * 2
        });

        // プレーンを作成
        var planeMesh = _renderer.CreateMesh(BasicMesh.CreatePlane(new Vector2(10, 10), 1));
        var planeMaterial = _renderer.CreateMaterial(BasicMaterial.CreateDielectric(new Vector3(0.7f, 0.7f, 0.7f), 0.8f));
        _objects.Add(new TestRenderableObject
        {
            Mesh = planeMesh,
            Material = planeMaterial,
            Transform = Matrix4x4.CreateTranslation(0, -1, 0),
            TriangleCount = 2
        });
    }

    private void CreateTestLights()
    {
        // 方向光源（太陽光）
        _lights.Add(new DirectionalLight
        {
            Direction = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.3f)),
            Color = new Vector3(1f, 0.95f, 0.8f),
            Intensity = 3.0f,
            CastShadows = true
        });

        // 点光源
        _lights.Add(new PointLight
        {
            Position = new Vector3(3, 2, 2),
            Color = new Vector3(0.2f, 0.5f, 1f),
            Intensity = 5.0f,
            Range = 10f,
            CastShadows = true
        });
    }

    public void Update()
    {
        _animationTime += 0.016f; // 60 FPS想定

        // キューブを回転
        if (_objects.Count > 0)
        {
            var rotationY = Matrix4x4.CreateRotationY(_animationTime);
            var translation = Matrix4x4.CreateTranslation(-2, 0, 0);
            _objects[0].Transform = rotationY * translation;
        }

        // スフィアを浮遊
        if (_objects.Count > 1)
        {
            float height = MathF.Sin(_animationTime * 2) * 0.5f;
            _objects[1].Transform = Matrix4x4.CreateTranslation(2, height, 0);
        }
    }
}

/// <summary>
/// テスト用のレンダリング可能オブジェクト
/// </summary>
public class TestRenderableObject : I3DRenderableObject
{
    public required I3DMeshResource Mesh { get; init; }
    public required I3DMaterialResource Material { get; init; }
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
    public bool CastShadows { get; init; } = true;
    public bool ReceiveShadows { get; init; } = true;
    public int TriangleCount { get; init; }
}