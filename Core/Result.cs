namespace Core;

public abstract class Result
{
    public sealed class Success : Result
    {
        public string Value { get; }

        public Success(string value)
        {
            Value = value;
        }
    }

    public sealed class Failure : Result
    {
        public string Error { get; }
        public string? StackTrace { get; }

        public Failure(string error, string? stackTrace = null)
        {
            Error = error;
            StackTrace = stackTrace;
        }
    }

    public T Match<T>(
        Func<string, T> onSuccess,
        Func<string, T> onFailure
    )
    {
        return this switch
        {
            Success s => onSuccess(s.Value),
            Failure f => onFailure(f.Error),
            _ => throw new InvalidOperationException()
        };
    }

    public void Match(
        Action<string> onSuccess,
        Action<string> onFailure
    )
    {
        switch (this)
        {
            case Success s:
                onSuccess(s.Value);
                break;
            case Failure f:
                onFailure(f.Error);
                break;
        }
    }

    public bool IsSuccess => this is Success;
    public bool IsFailure => this is Failure;
}

public abstract class Result<T>
{
    public sealed class Success : Result<T>
    {
        public T Value { get; }

        public Success(T value)
        {
            Value = value;
        }
    }

    public sealed class Failure : Result<T>
    {
        public string Error { get; }

        public Failure(string error)
        {
            Error = error;
        }
    }

    public TOut Match<TOut>(
        Func<T, TOut> onSuccess,
        Func<string, TOut> onFailure
    )
    {
        return this switch
        {
            Success s => onSuccess(s.Value),
            Failure f => onFailure(f.Error),
            _ => throw new InvalidOperationException()
        };
    }

    public void Match(
        Action<T> onSuccess,
        Action<string> onFailure
    )
    {
        switch (this)
        {
            case Success s:
                onSuccess(s.Value);
                break;
            case Failure f:
                onFailure(f.Error);
                break;
        }
    }

    public bool IsSuccess => this is Success;
    public bool IsFailure => this is Failure;

    public Result<TOut> Bind<TOut>(
        Func<T, Result<TOut>> bind
    )
    {
        return this switch
        {
            Success success => bind(success.Value),
            Failure failure => new Result<TOut>.Failure(failure.Error),
            _ => throw new InvalidOperationException()
        };
    }
}
