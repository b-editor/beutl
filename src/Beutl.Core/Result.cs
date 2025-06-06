using System.Diagnostics.CodeAnalysis;

namespace Beutl;

/// <summary>
/// Represents the result of an operation that can either succeed or fail.
/// Provides a more functional approach to error handling than exceptions.
/// </summary>
/// <typeparam name="T">The type of the success value</typeparam>
public readonly struct Result<T> : IEquatable<Result<T>>
{
    private readonly T? _value;
    private readonly string? _errorMessage;
    private readonly Exception? _exception;
    
    private Result(T value)
    {
        _value = value;
        _errorMessage = null;
        _exception = null;
        IsSuccess = true;
    }
    
    private Result(string errorMessage, Exception? exception = null)
    {
        _value = default;
        _errorMessage = errorMessage;
        _exception = exception;
        IsSuccess = false;
    }

    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the success value. Only valid when IsSuccess is true.
    /// </summary>
    public T? Value => _value;

    /// <summary>
    /// Gets the error message. Only valid when IsSuccess is false.
    /// </summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>
    /// Gets the exception that caused the failure, if any.
    /// </summary>
    public Exception? Exception => _exception;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    public static Result<T> Failure(string errorMessage) => new(errorMessage);

    /// <summary>
    /// Creates a failed result with an error message and exception.
    /// </summary>
    public static Result<T> Failure(string errorMessage, Exception exception) => new(errorMessage, exception);

    /// <summary>
    /// Creates a failed result from an exception.
    /// </summary>
    public static Result<T> Failure(Exception exception) => new(exception.Message, exception);

    /// <summary>
    /// Maps the success value to another type.
    /// </summary>
    public Result<TResult> Map<TResult>(Func<T, TResult> mapper)
    {
        return IsSuccess 
            ? Result<TResult>.Success(mapper(_value!))
            : Result<TResult>.Failure(_errorMessage!, _exception!);
    }

    /// <summary>
    /// Chains another operation that returns a Result.
    /// </summary>
    public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> binder)
    {
        return IsSuccess 
            ? binder(_value!)
            : Result<TResult>.Failure(_errorMessage!, _exception!);
    }

    /// <summary>
    /// Executes an action if the result is successful.
    /// </summary>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess)
        {
            action(_value!);
        }
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure.
    /// </summary>
    public Result<T> OnFailure(Action<string, Exception?> action)
    {
        if (IsFailure)
        {
            action(_errorMessage!, _exception);
        }
        return this;
    }

    /// <summary>
    /// Gets the value or throws an exception if the result is a failure.
    /// </summary>
    public T GetValueOrThrow()
    {
        if (IsFailure)
        {
            if (_exception is not null)
                throw _exception;
            throw new InvalidOperationException(_errorMessage);
        }
        return _value!;
    }

    /// <summary>
    /// Gets the value or returns a default value if the result is a failure.
    /// </summary>
    public T GetValueOrDefault(T defaultValue = default!)
    {
        return IsSuccess ? _value! : defaultValue;
    }

    /// <summary>
    /// Tries to get the value if the result is successful.
    /// </summary>
    public bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = _value;
        return IsSuccess;
    }

    public bool Equals(Result<T> other)
    {
        if (IsSuccess != other.IsSuccess) return false;
        
        return IsSuccess 
            ? EqualityComparer<T>.Default.Equals(_value, other._value)
            : _errorMessage == other._errorMessage;
    }

    public override bool Equals(object? obj) => obj is Result<T> other && Equals(other);

    public override int GetHashCode()
    {
        return IsSuccess 
            ? HashCode.Combine(IsSuccess, _value)
            : HashCode.Combine(IsSuccess, _errorMessage);
    }

    public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);
    public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);

    /// <summary>
    /// Implicit conversion from a value to a successful result.
    /// </summary>
    public static implicit operator Result<T>(T value) => Success(value);

    public override string ToString()
    {
        return IsSuccess 
            ? $"Success: {_value}"
            : $"Failure: {_errorMessage}";
    }
}

/// <summary>
/// Non-generic result for operations that don't return a value.
/// </summary>
public readonly struct Result : IEquatable<Result>
{
    private readonly string? _errorMessage;
    private readonly Exception? _exception;
    
    private Result(string errorMessage, Exception? exception = null)
    {
        _errorMessage = errorMessage;
        _exception = exception;
        IsSuccess = false;
    }

    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error message. Only valid when IsSuccess is false.
    /// </summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>
    /// Gets the exception that caused the failure, if any.
    /// </summary>
    public Exception? Exception => _exception;

    /// <summary>
    /// Represents a successful operation.
    /// </summary>
    public static readonly Result SuccessResult = new();

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result Success() => SuccessResult;

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    public static Result Failure(string errorMessage) => new(errorMessage);

    /// <summary>
    /// Creates a failed result with an error message and exception.
    /// </summary>
    public static Result Failure(string errorMessage, Exception exception) => new(errorMessage, exception);

    /// <summary>
    /// Creates a failed result from an exception.
    /// </summary>
    public static Result Failure(Exception exception) => new(exception.Message, exception);

    /// <summary>
    /// Executes an action if the result is successful.
    /// </summary>
    public Result OnSuccess(Action action)
    {
        if (IsSuccess)
        {
            action();
        }
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure.
    /// </summary>
    public Result OnFailure(Action<string, Exception?> action)
    {
        if (IsFailure)
        {
            action(_errorMessage!, _exception);
        }
        return this;
    }

    /// <summary>
    /// Throws an exception if the result is a failure.
    /// </summary>
    public void ThrowIfFailure()
    {
        if (IsFailure)
        {
            if (_exception is not null)
                throw _exception;
            throw new InvalidOperationException(_errorMessage);
        }
    }

    public bool Equals(Result other)
    {
        if (IsSuccess != other.IsSuccess) return false;
        return IsSuccess || _errorMessage == other._errorMessage;
    }

    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    public override int GetHashCode()
    {
        return IsSuccess 
            ? IsSuccess.GetHashCode()
            : HashCode.Combine(IsSuccess, _errorMessage);
    }

    public static bool operator ==(Result left, Result right) => left.Equals(right);
    public static bool operator !=(Result left, Result right) => !left.Equals(right);

    public override string ToString()
    {
        return IsSuccess 
            ? "Success"
            : $"Failure: {_errorMessage}";
    }
}