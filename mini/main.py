"""Mini web inference: the main project's live runtime (same models, same rules), shown as MJPEG.

No database, snapshots or login. AI code comes unchanged from the base image (app.demo_live, app.ai).
"""
import asyncio
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Literal

from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse, JSONResponse, StreamingResponse
from starlette.concurrency import run_in_threadpool

from app.demo_live.runtime import AI_ENABLED, NAMES, SOURCES, SETTINGS, Runtime

STATIC = Path(__file__).parent / "static"
BOUNDARY = "frame"

runtime = Runtime()


@asynccontextmanager
async def lifespan(app):
    runtime.start()
    try:
        yield
    finally:
        await run_in_threadpool(runtime.stop)


app = FastAPI(title="Smart CCTV mini", lifespan=lifespan)


def check_camera(camera_id):
    if camera_id not in SOURCES:
        raise HTTPException(404, "Unknown camera")


@app.get("/")
def index():
    return FileResponse(STATIC / "index.html", headers={"Cache-Control": "no-store"})


@app.get("/api/status")
def status():
    cameras = []
    for camera_id in SOURCES:
        s = runtime.status(camera_id)
        cameras.append({
            "id": camera_id, "name": NAMES[camera_id], "ai_enabled": AI_ENABLED[camera_id],
            "connected": s["capture"]["connected"], "ready": s["inference"]["ready"],
            "error": s["inference"]["error"] or s["capture"]["error"],
            "fps": round(s["inference"]["fps"], 2),
            "ms": round(1000 * s["inference"]["duration_seconds"]),
            "events": [{"label": e.get("label"), "item": e.get("item")} for e in s["events"]],
        })
    return JSONResponse({
        "models": "error" if runtime.model_error else "ready" if runtime.models_ready else "loading",
        "model_error": runtime.model_error, "device": SETTINGS["device"], "cameras": cameras,
    }, headers={"Cache-Control": "no-store"})


@app.get("/stream/{camera_id}/{mode}")
async def stream(camera_id: int, mode: Literal["apd", "vehicle"]):
    check_camera(camera_id)

    async def frames():
        last = None
        while True:
            result = runtime.frame(camera_id, mode)
            if result is not None and result[1] != last:
                data, last, _ = result
                yield (b"--" + BOUNDARY.encode() + b"\r\nContent-Type: image/jpeg\r\nContent-Length: "
                       + str(len(data)).encode() + b"\r\n\r\n" + data + b"\r\n")
            # Push a new frame as soon as inference finishes one; ~30 checks/s costs nothing.
            await asyncio.sleep(0.03)

    return StreamingResponse(frames(), media_type="multipart/x-mixed-replace; boundary=" + BOUNDARY,
                             headers={"Cache-Control": "no-store", "X-Accel-Buffering": "no"})
