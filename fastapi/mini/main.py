"""Smart CCTV Mini — AI service (inference + video only; never touches the database).

The AI is the base image's live runtime, unchanged (app.demo_live.runtime + app.ai: same models,
thresholds, rules and voting). This file only wires it up:

- cameras: read from the web app (GET /api/cameras). The runtime takes its camera settings from
  CCTV_* environment variables at import time, so they are set from the web's answer before the
  runtime is imported. When the web's camera settings change, this process exits and Docker starts
  it again with the new settings.
- violations: the runtime's own durable outbox delivers them, with the evidence JPEG, to the web
  (POST /api/ingest). Pending events survive restarts in DETECTION_OUTBOX_DIR.
- video: MJPEG streams for browsers holding a token signed by the web (MINI_STREAM_KEY).
"""
import asyncio
import base64
import hashlib
import hmac
import json
import os
import threading
import time
import urllib.request
from contextlib import asynccontextmanager
from typing import Literal

from fastapi import FastAPI, HTTPException, Query
from fastapi.responses import JSONResponse, StreamingResponse
from starlette.concurrency import run_in_threadpool

WEB_URL = os.environ.get("MINI_WEB_URL", "http://smartcctv_mini_web:8080").rstrip("/")
INGEST_TOKEN = os.environ["MINI_INGEST_TOKEN"]
STREAM_KEY = os.environ["MINI_STREAM_KEY"].encode()
RELOAD_SECONDS = 15
SLOTS = (1, 2)
# Never opened: a slot without a camera has AI disabled, but the runtime still wants a valid URL.
UNUSED_URL = "rtsp://camera-disabled.invalid/"
BOUNDARY = "frame"


class Service:
    def __init__(self):
        self.state = "menunggu konfigurasi kamera dari web"
        self.error = None
        self.cameras = []
        self.runtime = None
        self.stop = threading.Event()

    # ---- web API ----
    @staticmethod
    def call(path, payload=None):
        request = urllib.request.Request(
            WEB_URL + path, data=None if payload is None else json.dumps(payload).encode(),
            headers={"Authorization": "Bearer " + INGEST_TOKEN, "Content-Type": "application/json"},
            method="GET" if payload is None else "POST")
        with urllib.request.urlopen(request, timeout=10) as response:
            return json.loads(response.read(1024 * 1024))

    @staticmethod
    def from_env():
        # First install only: the web has no cameras yet, so offer the CCTV_* values from .env.
        cameras = []
        for i in SLOTS:
            url = os.environ.get("CCTV_%d_URL" % i, "").strip().strip("'\"")
            if not url:
                continue
            profile = os.environ.get("CCTV_%d_VEHICLE_PROFILE" % i, "").strip()
            cameras.append({
                "id": i, "name": os.environ.get("CCTV_%d_NAME" % i, "CCTV %d" % i).strip().strip("'\"") or "CCTV %d" % i,
                "url": url,
                "apd_enabled": os.environ.get("CCTV_%d_AI_ENABLED" % i, "true").strip().lower() != "false",
                "vehicle_enabled": bool(profile), "vehicle_profile": profile,
                "apd_mask": os.environ.get("CCTV_%d_APD_MASK" % i, "").strip(),
                "apd_penutup": os.environ.get("CCTV_%d_APD_PENUTUP" % i, "").strip(),
            })
        return cameras

    def fetch_cameras(self):
        cameras = self.call("/api/cameras")["cameras"]
        if not cameras and self.from_env():
            self.call("/api/cameras/bootstrap", {"cameras": self.from_env()})
            cameras = self.call("/api/cameras")["cameras"]
        return sorted(cameras, key=lambda c: c["id"])

    # ---- runtime ----
    @staticmethod
    def apply(cameras):
        by_id = {c["id"]: c for c in cameras}
        for i in SLOTS:
            c = by_id.get(i) or {}
            url = c.get("url") or ""
            enabled = bool(url) and bool(c.get("apd_enabled") or c.get("vehicle_enabled"))
            os.environ["CCTV_%d_URL" % i] = url if enabled else UNUSED_URL
            os.environ["CCTV_%d_NAME" % i] = c.get("name") or "Slot %d" % i
            os.environ["CCTV_%d_AI_ENABLED" % i] = "true" if enabled else "false"
            os.environ["CCTV_%d_APD_MASK" % i] = c.get("apd_mask") or ""
            os.environ["CCTV_%d_APD_PENUTUP" % i] = c.get("apd_penutup") or ""
            # Without a profile the vehicle view is object detection only (no violation rules).
            os.environ["CCTV_%d_VEHICLE_PROFILE" % i] = (c.get("vehicle_profile") or "") if c.get("vehicle_enabled") else ""
        os.environ.update({
            "DETECTION_PERSIST_ENABLED": "true",
            "DETECTION_INGEST_URL": WEB_URL + "/api/ingest",
            "DETECTION_INGEST_TOKEN": INGEST_TOKEN,
            "DETECTION_OUTBOX_DIR": os.environ.get("DETECTION_OUTBOX_DIR", "/data/outbox"),
        })

    def run(self):
        while not self.stop.is_set():
            try:
                self.cameras = self.fetch_cameras()
                break
            except Exception as exc:
                self.error = "web tidak terjangkau (%s); mencoba lagi" % type(exc).__name__
                self.stop.wait(5)
        if self.stop.is_set():
            return
        self.error = None
        self.apply(self.cameras)
        from app.demo_live.runtime import Runtime  # reads the CCTV_* values set above
        self.runtime = Runtime()
        self.runtime.start()
        self.state = "jalan"
        # Settings changed in the web: restart the process (Docker restart policy brings it back).
        while not self.stop.wait(RELOAD_SECONDS):
            try:
                if self.fetch_cameras() != self.cameras:
                    self.state = "pengaturan kamera berubah; memulai ulang"
                    self.runtime.stop()
                    os._exit(0)
            except Exception:
                pass  # web briefly down: keep running with the current settings


service = Service()


@asynccontextmanager
async def lifespan(app):
    threading.Thread(target=service.run, daemon=True, name="boot").start()
    try:
        yield
    finally:
        service.stop.set()
        if service.runtime is not None:
            await run_in_threadpool(service.runtime.stop)


app = FastAPI(title="Smart CCTV Mini AI", lifespan=lifespan, docs_url=None, redoc_url=None, openapi_url=None)


def check_token(t):
    # "<expiry>.<base64url HMAC-SHA256(expiry)>", made by web/src/Data/Security.cs.
    try:
        expiry, sig = (t or "").split(".", 1)
        expected = base64.urlsafe_b64encode(hmac.new(STREAM_KEY, expiry.encode(), hashlib.sha256).digest()).rstrip(b"=").decode()
        if hmac.compare_digest(sig, expected) and int(expiry) > time.time():
            return
    except ValueError:
        pass
    raise HTTPException(403, "Token stream tidak valid atau kedaluwarsa")


def camera_index(camera_id):
    if service.runtime is None:
        raise HTTPException(503, service.state)
    if camera_id not in service.runtime.cameras:
        raise HTTPException(404, "Kamera tidak dikenal")


@app.get("/")
def root():
    return {"service": "smart-cctv-mini-ai", "state": service.state}


@app.get("/health")
def health():
    runtime = service.runtime
    return {"state": service.state, "error": service.error or (runtime.model_error if runtime else None),
            "models": None if runtime is None else runtime.models_ready}


@app.get("/api/status")
def status(t: str = Query("")):
    check_token(t)
    runtime = service.runtime
    cameras = []
    if runtime is not None:
        from app.demo_live.runtime import AI_ENABLED, NAMES
        for camera_id in runtime.cameras:
            s = runtime.status(camera_id)
            cameras.append({
                "id": camera_id, "name": NAMES[camera_id], "ai_enabled": AI_ENABLED[camera_id],
                "connected": s["capture"]["connected"], "ready": s["inference"]["ready"],
                "fps": round(s["inference"]["fps"], 2), "ms": round(1000 * s["inference"]["duration_seconds"]),
                "outbox": s["persistence"].get("pending"),
            })
        models = "error" if runtime.model_error else "ready" if runtime.models_ready else "loading"
    else:
        models = "loading"
    return JSONResponse({"state": service.state, "models": models, "cameras": cameras},
                        headers={"Cache-Control": "no-store", "Access-Control-Allow-Origin": "*"})


def chunk(data):
    return (b"--" + BOUNDARY.encode() + b"\r\nContent-Type: image/jpeg\r\nContent-Length: "
            + str(len(data)).encode() + b"\r\n\r\n" + data + b"\r\n")


def raw_jpeg(camera_id, last):
    # Live tab: latest camera frame without AI, at most 960 px wide.
    import cv2
    state = service.runtime.cameras[camera_id]
    with state.lock:
        if state.raw is None or not state.connected or state.sequence == last:
            return None, last
        frame, sequence = state.raw, state.sequence
    h, w = frame.shape[:2]
    if w > 960:
        frame = cv2.resize(frame, (960, round(h * 960 / w)))
    ok, data = cv2.imencode(".jpg", frame, [cv2.IMWRITE_JPEG_QUALITY, 75])
    return (data.tobytes() if ok else None), sequence


@app.get("/stream/{camera_id}/{mode}")
async def stream(camera_id: int, mode: Literal["apd", "vehicle", "raw"], t: str = Query("")):
    check_token(t)
    camera_index(camera_id)
    runtime = service.runtime

    async def frames():
        last = None
        while True:
            if mode == "raw":
                data, last = await run_in_threadpool(raw_jpeg, camera_id, last)
                if data:
                    yield chunk(data)
                await asyncio.sleep(0.2)
                continue
            result = runtime.frame(camera_id, mode)
            if result is not None and result[1] != last:
                data, last, _ = result
                yield chunk(data)
            # Push a new frame as soon as inference finishes one; ~30 checks/s costs nothing.
            await asyncio.sleep(0.03)

    return StreamingResponse(frames(), media_type="multipart/x-mixed-replace; boundary=" + BOUNDARY,
                             headers={"Cache-Control": "no-store", "X-Accel-Buffering": "no"})
