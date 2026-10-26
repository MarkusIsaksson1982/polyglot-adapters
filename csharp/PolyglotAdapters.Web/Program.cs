// ASP.NET Core minimal API (C# backend) + vanilla-JS page (frontend): a small "lab note" e-service.
// C# owns validation and the domain shape; AI work (PII redaction, local-model summary, audit) is delegated
// to the Python gateway through PolyglotAdapters.GatewayClient. Synthetic data only.
using System.Text.RegularExpressions;
using PolyglotAdapters;

var builder = WebApplication.CreateBuilder(args);
var gatewayUrl = builder.Configuration["GATEWAY_URL"] ?? "http://127.0.0.1:8765";
var gatewayKey = builder.Configuration["GATEWAY_KEY"] ?? "test-key";
builder.Services.AddHttpClient("gateway", c => { c.BaseAddress = new Uri(gatewayUrl); c.Timeout = TimeSpan.FromSeconds(20); });
builder.Services.AddScoped(sp => new GatewayClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("gateway"), gatewayKey));
var app = builder.Build();
app.UseDefaultFiles(); app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/notes/summarize", async (NoteRequest req, GatewayClient g, CancellationToken ct) =>
{
    var errors = req.Validate();
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    try
    {
        var job = await g.ProcessAsync(JobType.Summarize, $"Prov {req.SampleId}: {req.Text}", ct);
        return Results.Ok(new NoteSummary(req.SampleId, job.Id, job.Result ?? ""));
    }
    catch (GatewayException e) when (e.Code == "job_failed") { return Results.Problem("AI-steget misslyckades", statusCode: 502); }
    catch (GatewayException e) when (e.Code == "unreachable") { return Results.Problem("AI-tjänsten är inte tillgänglig", statusCode: 503); }
});
app.MapGet("/api/audit", async (GatewayClient g, CancellationToken ct) => Results.Ok(await g.AuditAsync(ct)));
app.Run();

public sealed record NoteRequest(string SampleId, string Text)
{
    public Dictionary<string, string[]> Validate()
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(SampleId) || !Regex.IsMatch(SampleId, "^[A-Z]{2}-\\d{4,8}$"))
            e["sampleId"] = ["Format: två versaler, bindestreck, 4–8 siffror (t.ex. VB-202611)"];
        if (string.IsNullOrWhiteSpace(Text) || Text.Length > 4000) e["text"] = ["1–4000 tecken"];
        return e;
    }
}
public sealed record NoteSummary(string SampleId, int JobId, string Summary);
