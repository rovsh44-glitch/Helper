using System;

namespace Helper.Runtime.Core;

public sealed class ProjectPlanningException : Exception
{
    public ProjectPlanningException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
