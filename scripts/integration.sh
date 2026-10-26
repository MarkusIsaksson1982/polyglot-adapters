#!/usr/bin/env bash
# Starts a model stub + the Python gateway on a temp SQLite DB, then runs the PHP and C# adapter tests against it.
set -euo pipefail
cd "$(dirname "$0")/.."; ROOT=$PWD
GW=${GATEWAY_DIR:-$ROOT/../fastapi-ai-automation-gateway}  # clone the gateway next to this repo
PY=${GATEWAY_PY:-$GW/.venv/bin/python}
TMP=$(mktemp -d); trap 'kill $(jobs -p) 2>/dev/null; rm -rf $TMP' EXIT
python3 python/stub_model.py 8766 & 
( cd "$GW" && DATABASE_URL=sqlite:///$TMP/t.db OLLAMA_URL=http://127.0.0.1:8766 API_KEYS=$(printf test-key | sha256sum | cut -d' ' -f1) \
  "$PY" -m uvicorn app.main:app --port 8765 --log-level warning ) &
for i in $(seq 50); do curl -sf http://127.0.0.1:8765/healthz >/dev/null && break; sleep 0.2; done
export GATEWAY_URL=http://127.0.0.1:8765 GATEWAY_KEY=test-key GATEWAY_DIR=$GW
rc=0
php php/tests/run.php || rc=1
if command -v dotnet >/dev/null || [ -x "$HOME/.dotnet/dotnet" ]; then
  DN=$(command -v dotnet || echo "$HOME/.dotnet/dotnet")
  ( cd csharp/PolyglotAdapters.Tests && WORKER=$ROOT/python/redact_worker.py "$DN" run -v q --nologo ) || rc=1
  # web layer: ASP.NET minimal API + static JS page, smoke-tested over HTTP
  ( cd csharp/PolyglotAdapters.Web && "$DN" build -v q --nologo >/dev/null && GATEWAY_URL=$GATEWAY_URL GATEWAY_KEY=$GATEWAY_KEY "$DN" run --no-build --urls http://127.0.0.1:8770 >$TMP/web.log 2>&1 ) &
  for i in $(seq 150); do curl -sf http://127.0.0.1:8770/api/health >/dev/null && break; sleep 0.3; done
  bash scripts/web_smoke.sh http://127.0.0.1:8770 || rc=1
  pkill -f "PolyglotAdapters.Web" 2>/dev/null || true
fi
exit $rc
