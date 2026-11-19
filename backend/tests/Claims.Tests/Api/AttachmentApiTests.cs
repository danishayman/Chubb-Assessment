using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Claims.Api.Features;
using Claims.Domain;

namespace Claims.Tests.Api;

public class AttachmentApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = ApiFactory.Json;
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\n%%EOF");

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string contentType, Guid? requestId = null)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        if (requestId is { } id) form.Add(new StringContent(id.ToString()), "informationRequestId");
        return form;
    }

    private static async Task<(ClaimDetailDto Claim, Guid RequestId)> ClaimAwaitingInfo(HttpClient claimant, HttpClient officer)
    {
        var policies = await claimant.GetFromJsonAsync<List<PolicyDto>>("/api/policies/mine", Json);
        var res = await claimant.PostAsJsonAsync("/api/claims", new SubmitClaimRequest(
            policies!.First(p => p.Type == ClaimType.Motor).Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            "Jurong", "Scratched by a trolley.", 2_000m), Json);
        var id = (await res.Content.ReadFromJsonAsync<ClaimDetailDto>(Json))!.Summary.Id;

        await officer.PostAsJsonAsync($"/api/claims/{id}/assign", new AssignRequest(null), Json);
        var withRequest = await (await officer.PostAsJsonAsync($"/api/claims/{id}/info-requests",
            new InfoRequestRequest("Photos please"), Json)).Content.ReadFromJsonAsync<ClaimDetailDto>(Json);
        return (withRequest!, withRequest!.InformationRequests.Single().Id);
    }

    [Fact]
    public async Task Claimant_uploads_a_document_and_the_officer_can_download_it()
    {
        var claimant = await factory.LoginAs("aisha@example.com");
        var officer = await factory.LoginAs("daniel@chubb.test");
        var (claim, requestId) = await ClaimAwaitingInfo(claimant, officer);
        var id = claim.Summary.Id;

        var upload = await claimant.PostAsync($"/api/claims/{id}/attachments", Form(Pdf, "police-report.pdf", "application/pdf", requestId));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var doc = (await upload.Content.ReadFromJsonAsync<AttachmentDto>(Json))!;
        Assert.Equal("application/pdf", doc.ContentType);
        Assert.Equal(requestId, doc.InformationRequestId);

        var detail = await officer.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{id}", Json);
        Assert.Contains(detail!.Attachments, a => a.Id == doc.Id && a.FileName == "police-report.pdf");

        var download = await officer.GetAsync($"/api/claims/{id}/attachments/{doc.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task Other_claimants_cannot_upload_or_download()
    {
        var aisha = await factory.LoginAs("aisha@example.com");
        var raj = await factory.LoginAs("raj@example.com");
        var officer = await factory.LoginAs("daniel@chubb.test");
        var (claim, _) = await ClaimAwaitingInfo(aisha, officer);
        var id = claim.Summary.Id;

        var doc = (await (await aisha.PostAsync($"/api/claims/{id}/attachments", Form(Pdf, "a.pdf", "application/pdf")))
            .Content.ReadFromJsonAsync<AttachmentDto>(Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await raj.GetAsync($"/api/claims/{id}/attachments/{doc.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await raj.PostAsync($"/api/claims/{id}/attachments", Form(Pdf, "b.pdf", "application/pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await officer.PostAsync($"/api/claims/{id}/attachments", Form(Pdf, "c.pdf", "application/pdf"))).StatusCode);
    }

    [Theory]
    [InlineData("invoice.pdf", "application/pdf")]   // an executable renamed to .pdf
    [InlineData("photo.png", "image/png")]
    [InlineData("script.exe", "application/octet-stream")]
    public async Task Files_that_are_not_what_they_claim_are_rejected(string fileName, string contentType)
    {
        var claimant = await factory.LoginAs("aisha@example.com");
        var officer = await factory.LoginAs("daniel@chubb.test");
        var (claim, _) = await ClaimAwaitingInfo(claimant, officer);

        var exe = Encoding.ASCII.GetBytes("MZ\u0090\0\u0003\0\0\0this is not a pdf");
        var res = await claimant.PostAsync($"/api/claims/{claim.Summary.Id}/attachments", Form(exe, fileName, contentType));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Pdf_with_wrong_extension_is_rejected()
    {
        var claimant = await factory.LoginAs("aisha@example.com");
        var officer = await factory.LoginAs("daniel@chubb.test");
        var (claim, _) = await ClaimAwaitingInfo(claimant, officer);

        var res = await claimant.PostAsync($"/api/claims/{claim.Summary.Id}/attachments", Form(Pdf, "report.html", "text/html"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
