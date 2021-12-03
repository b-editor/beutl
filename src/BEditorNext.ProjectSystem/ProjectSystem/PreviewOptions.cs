namespace BEditorNext.ProjectSystem;

public enum PreviewMode
{
    Default = 0,
    Memory = 1,
    Storage = 2,
}

public record PreviewOptions
{
    public PreviewMode PreviewMode { get; init; }

    public bool LowColor { get; init; }

    public bool LowResolution { get; init; }

    public int Start { get; init; }

    public int Length { get; init; }
}