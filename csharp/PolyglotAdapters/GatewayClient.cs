using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolyglotAdapters;

/// <summary>Typed error carrying the gateway's {"code","message"} body and HTTP status.</summary>
public sealed class GatewayException(int status, string code, string message) : Exception($"[{status} {code}] {message}")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public enum JobType { Summarize, Classify, Extract }

public sealed record Job(int Id, string Type, string Status, string? Result, string? Error);
public sealed record AuditRow([property: JsonPropertyName("job_id")] int JobId, [property: JsonPropertyName("prompt_hash")] string PromptHash, string Status);
sealed record ErrorBody(ErrorDetail? Error);
sealed record ErrorDetail(string Code, string Message);

/// <summary>
/// C# wrapper around the Python fastapi-ai-automation-gateway: a .NET web app keeps its own domain logic and typing,
/// while Python owns PII redaction, the local-model call and the audit log. Only System.Net.Http + System.Text.Json.
/// </summary>
public sealed class GatewayClient(HttpClient http, string apiKey)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<Job> CreateJobAsync(JobType type, string text, CancellationToken ct = default) =>
        SendAsync<Job>(HttpMethod.Post, "/jobs", new { type = type.ToString().ToLowerInvariant(), input_text = text }, ct);
    public Task<Job> RunJobAsync(int id, CancellationToken ct = default) => SendAsync<Job>(HttpMethod.Post, $"/jobs/{id}/run", null, ct);
    public Task<Job> GetJobAsync(int id, CancellationToken ct = default) => SendAsync<Job>(HttpMethod.Get, $"/jobs/{id}", null, ct);
    public Task<List<AuditRow>> AuditAsync(CancellationToken ct = default) => SendAsync<List<AuditRow>>(HttpMethod.Get, "/audit", null, ct);

    /// <summary>Create + run; throws if the model step failed so callers never see a half-done job as success.</summary>
    public async Task<Job> ProcessAsync(JobType type, string text, CancellationToken ct = default)
    {
        var job = await RunJobAsync((await CreateJobAsync(type, text, ct)).Id, ct);
        return job.Status == "completed" ? job : throw new GatewayException(502, "job_failed", job.Error ?? "job failed");
    }

    async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Add("X-API-Key", apiKey);
        if (body is not null) req.Content = JsonContent.Create(body);
        HttpResponseMessage res;
        try { res = await http.SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new GatewayException(0, "unreachable", e.Message); }
        using (res)
        {
            if (!res.IsSuccessStatusCode)
            {
                ErrorBody? err = null;
                try { err = await res.Content.ReadFromJsonAsync<ErrorBody>(Json, ct); } catch (JsonException) { }
                throw new GatewayException((int)res.StatusCode, err?.Error?.Code ?? "http_error", err?.Error?.Message ?? res.ReasonPhrase ?? "error");
            }
            return await res.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new GatewayException((int)res.StatusCode, "bad_response", "empty body");
        }
    }
}
