using System;

namespace Beutl.Audio.Graph.Exceptions;

public class AudioGraphException : Exception
{
    public AudioGraphException(string message) : base(message)
    {
    }

    public AudioGraphException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class AudioGraphBuildException : AudioGraphException
{
    public AudioGraphBuildException(string message) : base(message)
    {
    }

    public AudioGraphBuildException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class AudioNodeException : AudioGraphException
{
    public AudioNode? Node { get; }
    
    public AudioNodeException(string message, AudioNode? node = null) : base(message)
    {
        Node = node;
    }

    public AudioNodeException(string message, AudioNode? node, Exception innerException) : base(message, innerException)
    {
        Node = node;
    }

    public override string Message
    {
        get
        {
            string baseMessage = base.Message;
            if (Node != null)
            {
                return $"{baseMessage} (Node: {Node.GetType().Name})";
            }
            return baseMessage;
        }
    }
}

public class AudioBufferException : AudioGraphException
{
    public int? SampleRate { get; }
    public int? ChannelCount { get; }
    public int? SampleCount { get; }

    public AudioBufferException(string message) : base(message)
    {
    }

    public AudioBufferException(string message, int sampleRate, int channelCount, int sampleCount) : base(message)
    {
        SampleRate = sampleRate;
        ChannelCount = channelCount;
        SampleCount = sampleCount;
    }

    public AudioBufferException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public override string Message
    {
        get
        {
            string baseMessage = base.Message;
            if (SampleRate.HasValue && ChannelCount.HasValue && SampleCount.HasValue)
            {
                return $"{baseMessage} (Format: {SampleRate}Hz, {ChannelCount}ch, {SampleCount} samples)";
            }
            return baseMessage;
        }
    }
}

public class AudioEffectException : AudioNodeException
{
    public string? EffectType { get; }

    public AudioEffectException(string message, string? effectType = null, AudioNode? node = null) : base(message, node)
    {
        EffectType = effectType;
    }

    public AudioEffectException(string message, string? effectType, AudioNode? node, Exception innerException) : base(message, node, innerException)
    {
        EffectType = effectType;
    }

    public override string Message
    {
        get
        {
            string baseMessage = base.Message;
            if (!string.IsNullOrEmpty(EffectType))
            {
                return $"{baseMessage} (Effect: {EffectType})";
            }
            return baseMessage;
        }
    }
}

public class AudioAnimationException : AudioGraphException
{
    public string? PropertyName { get; }
    public TimeSpan? Time { get; }

    public AudioAnimationException(string message, string? propertyName = null, TimeSpan? time = null) : base(message)
    {
        PropertyName = propertyName;
        Time = time;
    }

    public AudioAnimationException(string message, string? propertyName, TimeSpan? time, Exception innerException) : base(message, innerException)
    {
        PropertyName = propertyName;
        Time = time;
    }

    public override string Message
    {
        get
        {
            string baseMessage = base.Message;
            if (!string.IsNullOrEmpty(PropertyName) && Time.HasValue)
            {
                return $"{baseMessage} (Property: {PropertyName}, Time: {Time})";
            }
            else if (!string.IsNullOrEmpty(PropertyName))
            {
                return $"{baseMessage} (Property: {PropertyName})";
            }
            else if (Time.HasValue)
            {
                return $"{baseMessage} (Time: {Time})";
            }
            return baseMessage;
        }
    }
}