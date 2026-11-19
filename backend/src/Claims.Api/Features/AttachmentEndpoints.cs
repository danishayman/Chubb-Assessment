using System.Security.Claims;
using Claims.Api.Auth;
using Claims.Api.Data;
using Claims.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Features;

public static class AttachmentEndpoints
{
    public static void MapAttachments(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/claims/{id:guid}/attachments").WithTags("Attachments").RequireAuthorization();

        // Auth is a bearer token, not a cookie, so there is no CSRF risk to guard against here.
        g.MapPost("/", Upload).RequireAuthorization(Policies.Claimant).DisableAntiforgery();
        g.MapGet("/{attachmentId:guid}", Download);
    }

    private static async Task<IResult> Upload(Guid id, IFormFile file, [FromForm] Guid? informationRequestId,
        ClaimsPrincipal user, ClaimsDbContext db, IFileStore store, TimeProvider clock, CancellationToken ct)
    {
        var claim = await db.Claims
            .Include(c => c.InformationRequests)
            .Include(c => c.Attachments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null || claim.ClaimantId != user.UserId())
            return Results.NotFound();

        var fileName = FileSniffer.CleanFileName(file.FileName);
        var header = new byte[16];
        int read;
        await using (var peek = file.OpenReadStream())
            read = await peek.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);

        var contentType = FileSniffer.Detect(header.AsSpan(0, read), fileName);
        if (contentType is null)
            return Results.Problem($"Only {FileSniffer.AllowedDescription} files are accepted, and the extension must match the file.",
                statusCode: StatusCodes.Status400BadRequest, title: "Unsupported file");

        // Domain checks first (ownership, open claim, size, count), so nothing is written for a rejected upload.
        var attachment = claim.AddAttachment(user.UserId(), fileName, contentType, file.Length,
            informationRequestId, clock.GetUtcNow().UtcDateTime);

        await using (var content = file.OpenReadStream())
            await store.SaveAsync(attachment.StorageKey, content, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            store.Delete(attachment.StorageKey);
            throw;
        }

        return Results.Created($"/api/claims/{id}/attachments/{attachment.Id}",
            new AttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes,
                attachment.UploadedAt, attachment.InformationRequestId));
    }

    private static async Task<IResult> Download(Guid id, Guid attachmentId, ClaimsPrincipal user, ClaimsDbContext db,
        IFileStore store, HttpContext http)
    {
        var row = await (
            from a in db.Attachments
            join c in db.Claims on a.ClaimId equals c.Id
            where a.Id == attachmentId && a.ClaimId == id
            select new { a.FileName, a.ContentType, a.StorageKey, c.ClaimantId }).SingleOrDefaultAsync();

        if (row is null || (!user.IsStaff() && row.ClaimantId != user.UserId()))
            return Results.NotFound();

        var stream = store.OpenRead(row.StorageKey);
        if (stream is null) return Results.NotFound();

        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(stream, row.ContentType, row.FileName);
    }
}
