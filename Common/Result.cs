using System;

namespace AkademiTrack.Common
{
    /// <summary>
    /// Represents the result of an operation that can succeed or fail
    /// </summary>
    public class Result
    {
        public bool Success { get; protected set; }
        public string? ErrorMessage { get; protected set; }
        public Exception? Exception { get; protected set; }

        protected Result(bool success, string? errorMessage = null, Exception? exception = null)
        {
            Success = success;
            ErrorMessage = errorMessage;
            Exception = exception;
        }

        public static Result Successful() => new(true);
        public static Result Failed(string errorMessage) => new(false, errorMessage);
        public static Result Failed(Exception exception) => new(false, exception.Message, exception);
        public static Result Failed(string errorMessage, Exception exception) => new(false, errorMessage, exception);

        public static implicit operator bool(Result result) => result.Success;
    }

    /// <summary>
    /// Represents the result of an operation that can succeed with a value or fail
    /// </summary>
    /// <typeparam name="T">The type of the success value</typeparam>
    public class Result<T> : Result
    {
        public T? Value { get; private set; }

        private Result(bool success, T? value = default, string? errorMessage = null, Exception? exception = null)
            : base(success, errorMessage, exception)
        {
            Value = value;
        }

        public static Result<T> Successful(T value) => new(true, value);
        public static new Result<T> Failed(string errorMessage) => new(false, default, errorMessage);
        public static new Result<T> Failed(Exception exception) => new(false, default, exception.Message, exception);
        public static new Result<T> Failed(string errorMessage, Exception exception) => new(false, default, errorMessage, exception);

        public static implicit operator Result<T>(T value) => Successful(value);
    }

    /// <summary>
    /// Extension methods for Result types
    /// </summary>
    public static class ResultExtensions
    {
        /// <summary>
        /// Executes an action if the result is successful
        /// </summary>
        public static Result OnSuccess(this Result result, Action action)
        {
            if (result.Success)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    return Result.Failed(ex);
                }
            }
            return result;
        }

        /// <summary>
        /// Executes an action if the result is successful, with access to the value
        /// </summary>
        public static Result<T> OnSuccess<T>(this Result<T> result, Action<T> action)
        {
            if (result.Success && result.Value != null)
            {
                try
                {
                    action(result.Value);
                }
                catch (Exception ex)
                {
                    return Result<T>.Failed(ex);
                }
            }
            return result;
        }

        /// <summary>
        /// Executes an action if the result failed
        /// </summary>
        public static Result OnFailure(this Result result, Action<string?> action)
        {
            if (!result.Success)
            {
                action(result.ErrorMessage);
            }
            return result;
        }

        /// <summary>
        /// Transforms a successful result to a different type
        /// </summary>
        public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> mapper)
        {
            if (result.Success && result.Value != null)
            {
                try
                {
                    var mappedValue = mapper(result.Value);
                    return Result<TOut>.Successful(mappedValue);
                }
                catch (Exception ex)
                {
                    return Result<TOut>.Failed(ex);
                }
            }
            return Result<TOut>.Failed(result.ErrorMessage ?? "Operation failed", result.Exception!);
        }
    }
}