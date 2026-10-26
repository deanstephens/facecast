#!/usr/bin/env python3
"""Serves Facecast's web build (Builds/WebGL) the way GitHub Pages does, for trying it on this machine.

    python3 Tools/serve_web.py                 # http://localhost:8790
    python3 Tools/serve_web.py --port 9000 --dir Builds/WebGL

Open the address in two browser windows (or two browsers) with the same #room to meet yourself. Browsers only
allow the camera and microphone on https:// pages or on localhost, so other devices on your network can't use
their cameras through this server; deploy to GitHub Pages (Tools/deploy_pages.sh) to try it across devices.

Like GitHub Pages, it sends the build's compressed files without a Content-Encoding header; the Unity loader
unpacks them itself (the build uses decompression fallback).
"""

import argparse
import functools
import http.server
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TYPES = {
    ".wasm": "application/wasm",
    ".js": "text/javascript",
    ".mjs": "text/javascript",
    ".data": "application/octet-stream",
    ".unityweb": "application/octet-stream",
    ".json": "application/json",
    ".html": "text/html; charset=utf-8",
    ".css": "text/css",
    ".svg": "image/svg+xml",
}


class Handler(http.server.SimpleHTTPRequestHandler):
    def guess_type(self, path):
        return TYPES.get(os.path.splitext(path)[1]) or super().guess_type(path)

    def end_headers(self):
        # A rebuild replaces the files under the same names; make sure browsers fetch the new ones.
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()

    def log_message(self, fmt, *args):
        if "404" in (fmt % args):
            super().log_message(fmt, *args)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=8790)
    parser.add_argument("--dir", default=os.path.join(ROOT, "Builds", "WebGL"))
    parser.add_argument("--host", default="127.0.0.1", help="address to listen on (default: this machine only)")
    args = parser.parse_args()
    if not os.path.isfile(os.path.join(args.dir, "index.html")):
        sys.exit(f"No web build in {args.dir}. Build it first: Facecast > Player Builds > Web (see README).")
    handler = functools.partial(Handler, directory=args.dir)
    server = http.server.ThreadingHTTPServer((args.host, args.port), handler)
    print(f"Serving {args.dir} at http://localhost:{args.port}  (Ctrl+C stops)", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
