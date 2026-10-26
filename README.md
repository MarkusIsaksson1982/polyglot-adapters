# polyglot-adapters

Small adapters that let a stronger library in one language work alongside an application written in another – instead of rewriting it.
Here the "stronger library" is a Python AI-automation gateway (FastAPI, PII redaction before a local Ollama-compatible model, append-only audit log),
and the target languages are **PHP** and **C#**.

Two patterns, both in each language:

| pattern | when | PHP | C# |
|---|---|---|---|
| **HTTP adapter** – typed client for the gateway's REST API | the Python side runs as a service (shared, audited, scaled separately) | `php/src/GatewayClient.php` (stdlib streams, no ext-curl) | `csharp/PolyglotAdapters/GatewayClient.cs` (HttpClient + System.Text.Json) |
| **Process adapter** – one long-lived Python worker, JSON lines over stdin/stdout | call a Python function from a script/job without running a server | `php/src/PyWorker.php` (proc_open) | `csharp/PolyglotAdapters/PyWorker.cs` (Process, serialised calls) |

Design choices: the target language keeps its own domain logic and types; Python keeps what it is good at (AI/ML libraries, redaction, model calls).
Errors cross the boundary as one typed shape (`code`, `message`, HTTP status) – `GatewayError` in PHP, `GatewayException` in C# –
so callers branch on codes (`unauthorized`, `not_found`, `validation_error`, `job_failed`, `unreachable`, `unknown_op`) instead of parsing strings.
A job whose model step failed is never returned as success.

## Web layer (C# backend + JavaScript frontend)
`csharp/PolyglotAdapters.Web` – ASP.NET Core minimal API plus a vanilla-JS page (`wwwroot/`): a small "lab note" e-service in Swedish.
C# validates the request (sample-id format, length) and returns a per-field `ValidationProblem`; the summary comes from the Python gateway
(PII redacted before the local model, one audit row per call) through `GatewayClient`; model failure → 502, gateway down → 503.
`scripts/web_smoke.sh`: 6 HTTP checks (page, JS, validation, success without PII echo, model failure, audit).

## Run
```bash
./scripts/integration.sh   # starts a deterministic model stub + the gateway on a temp SQLite DB, then runs both suites
```
PHP 8.4: 12 tests · .NET 8: 11 tests + 6 web smoke checks (dependency-free runners). Synthetic data only.

## Honest scope
A demonstration of integration reasoning, not production code. My PHP experience is minimal and my C# comes from the
freeCodeCamp *Foundational C# with Microsoft* certification plus early Unity experiments – which is exactly why the approach is
adapters: thin, well-tested seams around a Python core, built with AI coding agents and verified by tests.
