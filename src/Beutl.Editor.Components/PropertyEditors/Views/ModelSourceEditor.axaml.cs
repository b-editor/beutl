using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Graphics3D.Models;
using Beutl.Language;
using Beutl.Serialization;
using Silk.NET.Assimp;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class ModelSourceEditor : UserControl
{
    // The file extensions Assimp reads.
    private static readonly string[] s_modelExtensions;

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
            // Without Assimp no model can be read, so the picker shows every file.
            s_modelExtensions = ["*"];
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

                var uri = UriHelper.CreateFromPath(fi.FullName);
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
