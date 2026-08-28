namespace Nudge.Core.Results;

/// <summary>
/// Outcome of an operation that is expected to fail sometimes (a folder the user picked is not a
/// VPX install, a file cannot be read). Genuine bugs still throw.
/// </summary>
public readonly record struct Result<T>
{
    private Result(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    private readonly T? _value;
    private readonly string? _error;

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>The produced value. Only meaningful when <see cref="IsSuccess"/> is true.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Result.Value read on a failed result. Check IsSuccess first.");

    /// <summary>Human-readable reason the operation failed. Safe to show to a user.</summary>
    public string Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("Result.Error read on a successful result. Check IsFailure first.");

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new Result<T>(false, default, error);
    }

    public T ValueOr(T fallback) => IsSuccess ? _value! : fallback;

    public bool TryGetValue(out T value)
    {
        value = _value!;
        return IsSuccess;
    }
}
