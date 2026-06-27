namespace Beutl.Media.Proxy;

public sealed class ProxyGenerationSkippedException(string message) : Exception(message);

public sealed class ProxyGeneratorUnavailableException(string message) : Exception(message);
