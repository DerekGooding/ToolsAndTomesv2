"""HTTP front end shared by the image and audio services.

    python -m asset_services.server --kind image --port 8411
    python -m asset_services.server --kind audio --port 8412

GET  /health    -> {"kind", "backends": {name: {"ready", "reason", "placeholder"}}}
POST /generate  -> request {"backend", "prompt", "negativePrompt", "seed", "count", "palette": [hex], "params": {...}}
                   response {"backend", "model", "candidates": [{"seed", "format", "data" (base64), "metrics"}]}

Files travel in the response body, so the orchestrator works the same against a local GPU or a rented one.
Requests are served one at a time: a single GPU is the bottleneck anyway.
"""

from __future__ import annotations

import argparse
import base64
import json
import sys
import time
import traceback
from http.server import BaseHTTPRequestHandler, HTTPServer

from . import audio_backends, image_backends


def make_handler(kind: str):
    registry = image_backends.BACKENDS if kind == "image" else audio_backends.BACKENDS

    class Handler(BaseHTTPRequestHandler):
        def _send(self, status: int, body: dict) -> None:
            payload = json.dumps(body).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)

        def do_GET(self) -> None:  # noqa: N802 (http.server naming)
            if self.path != "/health":
                self._send(404, {"error": "not found"})
                return
            backends = {}
            for name, backend in registry.items():
                ready, reason = backend.status()
                backends[name] = {"ready": ready, "reason": reason, "placeholder": backend.placeholder}
            self._send(200, {"kind": kind, "backends": backends})

        def do_POST(self) -> None:  # noqa: N802
            if self.path != "/generate":
                self._send(404, {"error": "not found"})
                return
            try:
                length = int(self.headers.get("Content-Length", "0"))
                request = json.loads(self.rfile.read(length) or b"{}")
                name = request.get("backend", "")
                backend = registry.get(name)
                if backend is None:
                    self._send(400, {"error": f"unknown {kind} backend '{name}'; known: {sorted(registry)}"})
                    return

                started = time.time()
                outputs = backend.generate(
                    prompt=request.get("prompt", ""),
                    negative_prompt=request.get("negativePrompt", ""),
                    seed=int(request.get("seed", 0)),
                    count=max(1, int(request.get("count", 1))),
                    palette=list(request.get("palette", [])),
                    params=dict(request.get("params", {})),
                )
                candidates = [
                    {
                        "seed": o.seed,
                        "format": o.format,
                        "data": base64.b64encode(o.data).decode("ascii"),
                        "metrics": o.metrics,
                    }
                    for o in outputs
                ]
                print(f"[{kind}] {name}: {len(candidates)} candidate(s) in {time.time() - started:.1f}s", flush=True)
                self._send(200, {"backend": name, "model": backend.model_id, "candidates": candidates})
            except Exception as exc:  # report generator failures to the orchestrator instead of dropping the socket
                traceback.print_exc()
                self._send(500, {"error": f"{type(exc).__name__}: {exc}"})

        def log_message(self, format: str, *args) -> None:  # quieter than the default per-request log
            pass

    return Handler


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--kind", choices=["image", "audio"], required=True)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=None, help="default 8411 (image) / 8412 (audio)")
    args = parser.parse_args(argv)

    port = args.port or (8411 if args.kind == "image" else 8412)
    server = HTTPServer((args.host, port), make_handler(args.kind))
    print(f"{args.kind} service listening on http://{args.host}:{port}", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
