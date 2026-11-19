namespace Claims.Domain;

/// <summary>
/// A file the claimant sent in, e.g. a police report or receipts. Only metadata lives here.
/// The bytes are in file storage under <see cref="StorageKey"/>, never under the user's own file name.
/// </summary>
public class Attachment
{
    private Attachment() { }

    internal Attachment(Guid claimId, Guid? informationRequestId, Guid uploadedBy, string fileName,
        string contentType, long sizeBytes, DateTime now)
    {
        Id = Guid.NewGuid();
        ClaimId = claimId;
        InformationRequestId = informationRequestId;
        UploadedBy = uploadedBy;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StorageKey = $"{claimId:N}/{Id:N}";
        UploadedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public Guid? InformationRequestId { get; private set; }
    public Guid UploadedBy { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long SizeBytes { get; private set; }
    public string StorageKey { get; private set; } = "";
    public DateTime UploadedAt { get; private set; }
}
