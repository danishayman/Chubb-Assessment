namespace Claims.Domain;

public enum DomainErrorKind { Validation, Conflict, Forbidden }

public class DomainException(string message, DomainErrorKind kind = DomainErrorKind.Validation) : Exception(message)
{
    public DomainErrorKind Kind { get; } = kind;

    public static DomainException Conflict(string message) => new(message, DomainErrorKind.Conflict);
    public static DomainException Forbidden(string message) => new(message, DomainErrorKind.Forbidden);
}
