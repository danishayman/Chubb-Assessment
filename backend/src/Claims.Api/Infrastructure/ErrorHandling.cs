using Claims.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Infrastructure;

/// <summary>Maps domain rule failures to ProblemDetails so the client gets a consistent error shape.</summary>
public class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            DomainException { Kind: DomainErrorKind.Conflict } e => (409, "Invalid claim state", e.Message),
            DomainException { Kind: DomainErrorKind.Forbidden } e => (403, "Not allowed", e.Message),
            DomainException e => (400, "Validation failed", e.Message),
            DbUpdateConcurrencyException => (409, "Concurrent update",
                "This claim was changed by someone else. Reload it and try again."),
            _ => (0, "", ""),
        };
        if (status == 0) return false;

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
