using System;

#nullable enable

namespace Sayra.Backend.Shared
{
    public class Result
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public string? ErrorCode { get; }
        public string? ErrorMessage { get; }

        protected Result(bool isSuccess, string? errorCode = null, string? errorMessage = null)
        {
            IsSuccess = isSuccess;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public static Result Success() => new Result(true);
        public static Result Failure(string errorCode, string? errorMessage = null) => new Result(false, errorCode, errorMessage);

        public static Result<T> Success<T>(T value) => Result<T>.Success(value);
        public static Result<T> Failure<T>(string errorCode, string? errorMessage = null) => Result<T>.Failure(errorCode, errorMessage);
    }

    public class Result<T> : Result
    {
        public T? Value { get; }

        private Result(bool isSuccess, T? value, string? errorCode = null, string? errorMessage = null)
            : base(isSuccess, errorCode, errorMessage)
        {
            Value = value;
        }

        public static Result<T> Success(T value) => new Result<T>(true, value);
        public static new Result<T> Failure(string errorCode, string? errorMessage = null) => new Result<T>(false, default, errorCode, errorMessage);
    }
}
