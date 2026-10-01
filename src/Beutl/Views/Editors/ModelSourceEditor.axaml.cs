using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics3D.Models;
using Beutl.Language;
using Beutl.ViewModels.Editors;
using Silk.NET.Assimp;

namespace Beutl.Views.Editors;

public partial class ModelSourceEditor : UserControl
{
    // Assimp supported file extensions
    // Reference: https://github.com/assimp/assimp/blob/master/doc/Fileformats.md
    private static readonly string[] s_modelExtensions =
    [
        // Common formats
        "*.obj", // Wavefront OBJ
        "*.fbx", // Autodesk FBX
        "*.gltf", // glTF 1.0/2.0
        "*.glb", // glTF Binary
        "*.dae", // COLLADA
        "*.3ds", // 3D Studio Max
        "*.ase", // 3D Studio Max ASE
        "*.stl", // Stereolithography
        "*.ply", // Stanford PLY
        "*.x", // DirectX X
        "*.3mf", // 3D Manufacturing Format
        "*.usd", // Universal Scene Description
        "*.usda", // Universal Scene Description ASCII
        "*.usdc", // Universal Scene Description Binary
        "*.usdz", // Universal Scene Description Zip

        // Other formats (alphabetical)
        "*.3d", // Unreal
        "*.ac", // AC3D
        "*.acc", // AC3D
        "*.amj", // AMJ
        "*.ask", // ASK
        "*.b3d", // BlitzBasic 3D
        "*.blend", // Blender (deprecated)
        "*.bvh", // Biovision BVH
        "*.cob", // TrueSpace
        "*.csm", // Character Studio Motion
        "*.dxf", // AutoCAD DXF
        "*.enff", // Extended Neutral File Format
        "*.hmb", // HMB
        "*.ifc", // Industry Foundation Classes (IFC-STEP)
        "*.iqm", // Inter-Quake Model
        "*.irr", // Irrlicht Scene
        "*.irrmesh", // Irrlicht Mesh
        "*.lwo", // LightWave
        "*.lws", // LightWave Scene
        "*.lxo", // Modo
        "*.m3d", // Model 3D
        "*.md2", // Quake II
        "*.md3", // Quake III
        "*.md5anim", // Doom 3 Animation
        "*.md5camera", // Doom 3 Camera
        "*.md5mesh", // Doom 3 Mesh
        "*.mdc", // Return to Castle Wolfenstein
        "*.mdl", // Quake/Half-Life MDL
        "*.mesh", // Ogre Mesh
        "*.mesh.xml", // Ogre XML Mesh
        "*.mot", // LightWave Motion
        "*.ms3d", // Milkshape 3D
        "*.ndo", // Nendo
        "*.nff", // Neutral File Format
        "*.off", // Object File Format
        "*.ogex", // Open Game Engine Exchange
        "*.pmx", // MikuMikuDance
        "*.prj", // 3D Studio Project
        "*.q3o", // Quick3D
        "*.q3s", // Quick3D
        "*.raw", // Raw triangle data
        "*.scn", // TrueSpace Scene
        "*.sib", // Silo
        "*.skeleton.xml", // Ogre Skeleton
        "*.smd", // Valve SMD
        "*.step", // STEP
        "*.stp", // STEP
        "*.ter", // Terragen Terrain
        "*.uc", // Unreal Script
        "*.vta", // Valve VTA
        "*.x3d", // X3D
        "*.xgl", // XGL
        "*.zgl", // ZGL
    ];

    static ModelSourceEditor()
    {
        try
        {
            using var assimp = Assimp.GetApi();
            AssimpString str = default;
            assimp.GetExtensionList(ref str);

            s_modelExtensions = str.ToString()
                .Split(';').Select(s => s.StartsWith("*.") ? s : $"*.{s}").ToArray();
        }
        catch
        {
        }
    }

    public ModelSourceEditor()
    {
        InitializeComponent();

        FileEditor.OpenOptions = new FilePickerOpenOptions
        {
            FileTypeFilter = [new FilePickerFileType(Strings.ModelFile) { Patterns = s_modelExtensions }]
        };
        FileEditor.ValueConfirmed += FileEditorOnValueConfirmed;
        FileEditor.PropertyChanged += FileEditorOnPropertyChanged;
    }

    private readonly SemaphoreSlim _importGate = new(1, 1);
    private int _loadVersion;
    private string? _loadingPath;

    // テストから差し替え・完了待ちをするために公開している
    internal Func<Uri, ModelSource> ReadModel { get; set; } = static uri =>
    {
        var source = new ModelSource();
        source.ReadFrom(uri);
        return source;
    };

    internal Task LoadingTask { get; private set; } = Task.CompletedTask;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        CancelLoad();
    }

    // クリアやリセット、Undoなどで読み込み中のファイル以外に変わったら、その読み込みは無効にする
    private void FileEditorOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == StorageFileEditor.ValueProperty
            && _loadingPath != null
            && (e.NewValue as FileInfo)?.FullName != _loadingPath)
        {
            CancelLoad();
        }
    }

    private void CancelLoad()
    {
        _loadVersion++;
        _loadingPath = null;
        progress.IsVisible = false;
    }

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not ModelSourceEditorViewModel { IsDisposed: false } vm) return;
        if (e.NewValue is not FileInfo fi) return;

        LoadingTask = LoadAsync(vm, fi);
    }

    // 大きなモデルでUIが固まらないよう、Assimpによる読み込みはバックグラウンドで行う
    private async Task LoadAsync(ModelSourceEditorViewModel vm, FileInfo fi)
    {
        int version = ++_loadVersion;
        _loadingPath = fi.FullName;
        message.IsVisible = false;
        message.Text = null;
        progress.IsVisible = true;

        bool IsStale() => version != _loadVersion || vm.IsDisposed;

        // 同じパスのまま別のModelSourceに置き換わった場合(Undoなど)も、その読み込みは無効にする
        IDisposable sourceChanged = vm.Value.Skip(1).Subscribe(_ =>
        {
            if (version == _loadVersion) CancelLoad();
        });

        try
        {
            ModelSource newValue;
            // Assimpの読み込みは途中で中断できないため同時に1つまでとし、待機中に置き換えられたものは開始しない
            await _importGate.WaitAsync();
            try
            {
                if (IsStale()) return;

                var uri = new Uri(fi.FullName);
                newValue = await Task.Run(() => ReadModel(uri));
            }
            finally
            {
                _importGate.Release();
            }

            if (IsStale()) return;

            _loadingPath = null;
            sourceChanged.Dispose();
            vm.SetValue(newValue);
        }
        catch (Exception ex)
        {
            if (IsStale()) return;

            _loadingPath = null;
            FileEditor.Value = vm.FileInfo.Value;
            message.Text = ex.Message;
            message.IsVisible = true;
        }
        finally
        {
            sourceChanged.Dispose();
            if (version == _loadVersion)
                progress.IsVisible = false;
        }
    }
}
