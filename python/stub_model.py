"""Deterministic Ollama-compatible stub (/api/generate) so the adapters can be tested without a real model."""
import json, sys
from http.server import BaseHTTPRequestHandler, HTTPServer
class H(BaseHTTPRequestHandler):
    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        prompt = body["prompt"]
        if "FAIL_MODEL" in prompt:
            self.send_response(500); self.end_headers(); return
        out = json.dumps({"response": f"stub:{len(prompt)}:" + prompt.splitlines()[-1][:80]}).encode()
        self.send_response(200); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(out))); self.end_headers(); self.wfile.write(out)
    def log_message(self, *a): pass
HTTPServer(("127.0.0.1", int(sys.argv[1])), H).serve_forever()
