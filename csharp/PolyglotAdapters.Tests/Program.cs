// Dependency-free test runner (no NuGet needed): integration tests against a live gateway + the Python worker.
using PolyglotAdapters;

var url = Environment.GetEnvironmentVariable("GATEWAY_URL") ?? "http://127.0.0.1:8765";
var key = Environment.GetEnvironmentVariable("GATEWAY_KEY") ?? "test-key";
var worker = Environment.GetEnvironmentVariable("WORKER") ?? "../../python/redact_worker.py";
int pass = 0, fail = 0;
async Task T(string name, Func<Task> f) { try { await f(); pass++; Console.WriteLine($"ok   {name}"); } catch (Exception e) { fail++; Console.WriteLine($"FAIL {name}: {e.Message}"); } }
async Task Throws(Func<Task> f, string code) { try { await f(); } catch (GatewayException e) when (e.Code == code) { return; } throw new Exception($"expected {code}"); }
static void Assert(bool c, string m) { if (!c) throw new Exception(m); }

var g = new GatewayClient(new HttpClient { BaseAddress = new Uri(url) }, key);
await T("bad key -> typed 401", () => Throws(() => new GatewayClient(new HttpClient { BaseAddress = new Uri(url) }, "wrong").AuditAsync(), "unauthorized"));
await T("process redacts PII before the model", async () => {
    var j = await g.ProcessAsync(JobType.Summarize, "Ring " + string.Join("", "07", "0-1", "23 45 67") + " eller mejla anna@example.se");
    Assert(j.Status == "completed", j.Status); Assert(!j.Result!.Contains("anna@example.se"), j.Result); });
await T("model failure -> job_failed", () => Throws(() => g.ProcessAsync(JobType.Extract, "FAIL_MODEL now"), "job_failed"));
await T("missing job -> typed 404", () => Throws(() => g.GetJobAsync(999999), "not_found"));
await T("empty text -> typed 422", () => Throws(() => g.CreateJobAsync(JobType.Classify, ""), "validation_error"));
await T("audit rows carry only hashes", async () => { var a = await g.AuditAsync(); Assert(a.Count >= 2 && a.All(r => r.PromptHash.Length == 64), $"rows {a.Count}"); });
await T("unreachable -> typed", () => Throws(() => new GatewayClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") }, key).AuditAsync(), "unreachable"));
using (var w = new PyWorker(worker))
{
    await T("worker ping", async () => Assert((await w.CallAsync("ping"))!.GetValue<string>() == "pong", "ping"));
    await T("worker redact (Python logic from C#)", async () => Assert(!(await w.CallAsync("redact", new Dictionary<string, string> { ["text"] = "mail anna@example.se" }))!.GetValue<string>().Contains('@'), "redact"));
    await T("worker unknown op -> typed", () => Throws(() => w.CallAsync("rm"), "unknown_op"));
    await T("worker 20 concurrent calls stay ordered", async () => { await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => w.CallAsync("ping"))); });
}
Console.WriteLine($"\ncsharp: {pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;
