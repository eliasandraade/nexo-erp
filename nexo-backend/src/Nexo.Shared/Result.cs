namespace Nexo.Shared;

/// <summary>
/// Non-generic Result for operations that return no value.
/// Use for commands/mutations where success vs failure is all that matters.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);

    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Failure<T>(string error) => Result<T>.Fail(error);
}

/// <summary>
/// Generic Result for operations that return a value on success.
/// </summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(true, null) => _value = value;
    private Result(string error) : base(false, error) => _value = default;

    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot access Value on a failed result.");

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(string error) => new(error);

    /// <summary>Allows implicit conversion from T so callers can just return the value.</summary>
    public static implicit operator Result<T>(T value) => Ok(value);
}
