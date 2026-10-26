"""Process adapter: exposes a Python function (the gateway's PII redaction) over JSON lines on stdin/stdout.
PHP and C# start this once and send many requests – no HTTP server needed, Python keeps owning the logic.
Protocol: one JSON object per line in: {"id": n, "op": "redact"|"ping", "text": "..."}; out: {"id": n, "ok": true, "result": ...} or {"id": n, "ok": false, "error": {"code", "message"}}."""
import json, os, sys
sys.path.insert(0, os.environ.get("GATEWAY_DIR", os.path.join(os.path.dirname(__file__), "..", "..", "fastapi-ai-automation-gateway")))
try:
    from app.redaction import redact
except Exception:  # standalone fallback keeps the protocol testable
    import re
    def redact(t):
        t = re.sub(r"[\w.+-]+@[\w-]+\.[\w.]+", "[EMAIL]", t)
        return re.sub(r"\b(19|20)?\d{6}[-+]?\d{4}\b", "[PERSONNUMMER]", t)
OPS = {"ping": lambda r: "pong", "redact": lambda r: redact(str(r["text"]))}
for line in sys.stdin:
    try:
        req = json.loads(line); rid = req.get("id")
    except json.JSONDecodeError:
        print(json.dumps({"id": None, "ok": False, "error": {"code": "bad_json", "message": "invalid JSON line"}}), flush=True); continue
    op = OPS.get(req.get("op"))
    if not op:
        print(json.dumps({"id": rid, "ok": False, "error": {"code": "unknown_op", "message": str(req.get("op"))}}), flush=True); continue
    try:
        print(json.dumps({"id": rid, "ok": True, "result": op(req)}, ensure_ascii=False), flush=True)
    except Exception as e:
        print(json.dumps({"id": rid, "ok": False, "error": {"code": "op_failed", "message": type(e).__name__}}), flush=True)
