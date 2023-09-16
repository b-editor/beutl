using Beutl.Graphics;

namespace Beutl.Models;

public record struct ElementDescription(
    TimeSpan Start,
    TimeSpan Length,
    int Layer,
    string Name = "",
    Type? InitialOperator = null,
    string? FileName = null,
    Point Position = default);
