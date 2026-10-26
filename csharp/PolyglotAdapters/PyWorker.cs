using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PolyglotAdapters;

/// <summary>Process adapter: one long-lived Python worker, JSON lines over stdin/stdout (python/redact_worker.py).
/// Lets C# call a Python library (here: the gateway's redaction) without running an HTTP server.</summary>
public sealed class PyWorker : IDisposable
{
    readonly Process _p; int _next = 1; readonly SemaphoreSlim _lock = new(1, 1);
    public PyWorker(string script, string python = "python3")
    {
        _p = Process.Start(new ProcessStartInfo(python, script) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })
             ?? throw new InvalidOperationException("could not start python worker");
    }
    public async Task<JsonNode?> CallAsync(string op, IDictionary<string, string>? args = null)
    {
        await _lock.WaitAsync();   // one request in flight: responses stay in order
        try
        {
            var id = _next++;
            var req = new JsonObject { ["id"] = id, ["op"] = op };
            foreach (var (k, v) in args ?? new Dictionary<string, string>()) req[k] = v;
            await _p.StandardInput.WriteLineAsync(req.ToJsonString());
            await _p.StandardInput.FlushAsync();
            var line = await _p.StandardOutput.ReadLineAsync() ?? throw new InvalidOperationException("python worker closed");
            var res = JsonNode.Parse(line)!.AsObject();
            if (res["id"]?.GetValue<int>() != id) throw new InvalidOperationException("response id mismatch");
            if (!res["ok"]!.GetValue<bool>()) throw new GatewayException(0, res["error"]!["code"]!.GetValue<string>(), res["error"]!["message"]!.GetValue<string>());
            return res["result"];
        }
        finally { _lock.Release(); }
    }
    public void Dispose() { try { _p.StandardInput.Close(); _p.WaitForExit(2000); } catch { } _p.Dispose(); }
}
