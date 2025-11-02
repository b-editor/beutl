using System.Collections.Concurrent;

namespace Beutl.Protocol.Transport;

/// <summary>
/// Handles transport errors with retry logic and circuit breaker pattern.
/// </summary>
public class TransportErrorHandler
{
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly ConcurrentDictionary<Type, int> _errorCounts;
    private readonly object _circuitBreakerLock = new();
    private CircuitBreakerState _circuitState = CircuitBreakerState.Closed;
    private DateTime _circuitOpenedAt;
    private readonly TimeSpan _circuitBreakerTimeout;

    public TransportErrorHandler(
        int maxRetries = 3,
        TimeSpan? baseDelay = null,
        TimeSpan? circuitBreakerTimeout = null)
    {
        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _circuitBreakerTimeout = circuitBreakerTimeout ?? TimeSpan.FromMinutes(1);
        _errorCounts = new ConcurrentDictionary<Type, int>();
    }

    /// <summary>
    /// Executes an operation with retry logic and circuit breaker.
    /// </summary>
    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        // Check circuit breaker
        if (!TryEnterCircuit())
        {
            throw new InvalidOperationException("Circuit breaker is open. Too many failures detected.");
        }

        Exception? lastException = null;
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var result = await operation();

                // Success - close circuit if it was half-open
                OnSuccess();
                return result;
            }
            catch (Exception ex) when (IsTransientError(ex))
            {
                lastException = ex;

                if (attempt < _maxRetries)
                {
                    var delay = CalculateDelay(attempt);
                    await Task.Delay(delay, cancellationToken);
                }
                else
                {
                    OnFailure(ex);
                }
            }
            catch (Exception ex)
            {
                // Non-transient error - fail immediately
                OnFailure(ex);
                throw;
            }
        }

        OnFailure(lastException!);
        throw new TransportException($"Operation failed after {_maxRetries} retries", lastException!);
    }

    /// <summary>
    /// Executes an operation without return value.
    /// </summary>
    public async Task ExecuteWithRetryAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return true;
        }, cancellationToken);
    }

    private bool TryEnterCircuit()
    {
        lock (_circuitBreakerLock)
        {
            switch (_circuitState)
            {
                case CircuitBreakerState.Closed:
                    return true;

                case CircuitBreakerState.Open:
                    if (DateTime.UtcNow - _circuitOpenedAt >= _circuitBreakerTimeout)
                    {
                        _circuitState = CircuitBreakerState.HalfOpen;
                        return true;
                    }
                    return false;

                case CircuitBreakerState.HalfOpen:
                    return true;

                default:
                    return false;
            }
        }
    }

    private void OnSuccess()
    {
        lock (_circuitBreakerLock)
        {
            _circuitState = CircuitBreakerState.Closed;
            _errorCounts.Clear();
        }
    }

    private void OnFailure(Exception ex)
    {
        lock (_circuitBreakerLock)
        {
            var errorType = ex.GetType();
            var count = _errorCounts.AddOrUpdate(errorType, 1, (_, c) => c + 1);

            // Open circuit if too many failures
            if (count >= _maxRetries * 2)
            {
                _circuitState = CircuitBreakerState.Open;
                _circuitOpenedAt = DateTime.UtcNow;
            }
        }
    }

    private TimeSpan CalculateDelay(int attempt)
    {
        // Exponential backoff with jitter
        var exponentialDelay = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitter = Random.Shared.NextDouble() * _baseDelay.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(exponentialDelay + jitter);
    }

    private bool IsTransientError(Exception ex)
    {
        // Define which errors are transient and should be retried
        return ex is TimeoutException
            || ex is InvalidOperationException
            || (ex.InnerException != null && IsTransientError(ex.InnerException));
    }

    public CircuitBreakerState CircuitState
    {
        get
        {
            lock (_circuitBreakerLock)
            {
                return _circuitState;
            }
        }
    }

    public void Reset()
    {
        lock (_circuitBreakerLock)
        {
            _circuitState = CircuitBreakerState.Closed;
            _errorCounts.Clear();
        }
    }
}

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}

public class TransportException : Exception
{
    public TransportException(string message) : base(message)
    {
    }

    public TransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
