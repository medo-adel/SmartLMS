namespace SmartLMS.Domain.Exceptions;

/// <summary>A business rule was broken. Mapped to HTTP 400 by the global exception middleware.</summary>
public class DomainException(string message) : Exception(message);

/// <summary>The requested entity does not exist or is not visible to the current tenant. Mapped to HTTP 404.</summary>
public class NotFoundException(string message) : DomainException(message);
