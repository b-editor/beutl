namespace BeUtl.Models.Extensions.Develop;

[Flags]
public enum ReleaseResourceFields
{
    None = 0,
    Title = 1 << 0,
    Body = 1 << 1,
    Culture = 1 << 2,
    All = Title | Body | Culture,
}
