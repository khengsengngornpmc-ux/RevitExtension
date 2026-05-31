using System;

namespace CamboBIM.Revit2024.Addin
{
    internal class OperationResult
    {
        protected OperationResult(bool succeeded, string message, string errorCode, Exception exception)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
            Exception = exception;
        }

        public bool Succeeded { get; private set; }

        public string Message { get; private set; }

        public string ErrorCode { get; private set; }

        public Exception Exception { get; private set; }

        public static OperationResult Success(string message = "")
        {
            return new OperationResult(true, message, string.Empty, null);
        }

        public static OperationResult Failure(string message, string errorCode = "", Exception exception = null)
        {
            return new OperationResult(false, message, errorCode, exception);
        }

        public override string ToString()
        {
            if (Succeeded)
            {
                return string.IsNullOrWhiteSpace(Message) ? "Success" : Message;
            }

            if (!string.IsNullOrWhiteSpace(ErrorCode))
            {
                return ErrorCode + ": " + Message;
            }

            return string.IsNullOrWhiteSpace(Message) ? "Failure" : Message;
        }
    }

    internal sealed class OperationResult<T> : OperationResult
    {
        private OperationResult(bool succeeded, T value, string message, string errorCode, Exception exception)
            : base(succeeded, message, errorCode, exception)
        {
            Value = value;
        }

        public T Value { get; private set; }

        public static OperationResult<T> Success(T value, string message = "")
        {
            return new OperationResult<T>(true, value, message, string.Empty, null);
        }

        public static new OperationResult<T> Failure(string message, string errorCode = "", Exception exception = null)
        {
            return new OperationResult<T>(false, default(T), message, errorCode, exception);
        }
    }
}
