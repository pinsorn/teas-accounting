namespace Accounting.Domain.Common;

/// <summary>
/// Thrown when a domain invariant is violated. Application layer maps to a
/// 422 ProblemDetails — never a 500.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}
