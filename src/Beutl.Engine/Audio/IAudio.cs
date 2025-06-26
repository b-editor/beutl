using Beutl.Media.Music;

namespace Beutl.Audio;

// Simplified interface for compatibility
// The actual implementation now uses the graph-based system
public interface IAudio : IDisposable
{
    int SampleRate { get; }
    
    void Write(in IPcm pcm);
}