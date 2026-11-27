namespace Beutl.Editor.Components.TimelineTab.Models;

// Both endpoints are frame boundaries. End is the boundary just after the last selected frame.
internal sealed record FrameSelectionRange(MediaTime Start, MediaTime End);
