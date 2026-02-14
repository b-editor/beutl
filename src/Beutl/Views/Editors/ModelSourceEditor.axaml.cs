using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics3D.Models;
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
            FileTypeFilter = [new FilePickerFileType("3D Model File") { Patterns = s_modelExtensions }]
        };
        FileEditor.ValueConfirmed += FileEditorOnValueConfirmed;
    }

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not ModelSourceEditorViewModel { IsDisposed: false } vm) return;
        if (e.NewValue is not FileInfo fi) return;

        ModelSource? oldValue = vm.PropertyAdapter.GetValue();
        var newValue = new ModelSource();
        newValue.ReadFrom(new Uri(fi.FullName));
        vm.SetValueAndDispose(oldValue, newValue);
    }
}
