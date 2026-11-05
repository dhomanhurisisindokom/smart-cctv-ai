"""Vehicle evidence recorder for false-positive analysis. Observes only; never changes a decision.

When the live vehicle rules report "Penumpang di bak kendaraan", the pipeline's own window (the
raw 1080p frames and RT-DETR records it just decided on, up to 60 s) is written to disk:

    VEHICLE_CAPTURE_DIR/<yyyy-mm-dd>/<hhmmss>-cam<id>/
        capture.json      camera, profile, wall time, detections, and every record
                          (frame id, time_s, boxes, labels, scores) in the window
        frames/<id>.jpg   the frames those records came from (JPEG quality 95)

tools/replay_vehicle.py replays a capture through the live rules and through the original
vehicle_violation package, so both decisions can be compared on exactly the same input.

One capture per camera per episode (CAPTURE_GAP_S apart); oldest captures are deleted beyond
VEHICLE_CAPTURE_MAX. Writing runs on a separate thread so inference never waits for the disk.
"""
import json
import os
import queue
import shutil
import threading
import time
from pathlib import Path

ENABLED = os.environ.get("VEHICLE_CAPTURE", "true").strip().lower() == "true"
ROOT = Path(os.environ.get("VEHICLE_CAPTURE_DIR", "/data/vehicle-captures"))
MAX_CAPTURES = int(os.environ.get("VEHICLE_CAPTURE_MAX", "100"))
CAPTURE_GAP_S = 20.0
# Testing only: also record one full window per camera without any alarm.
TEST = os.environ.get("VEHICLE_CAPTURE_TEST", "false").strip().lower() == "true"
_tested = set()

_queue = queue.Queue(maxsize=4)
_last = {}
_lock = threading.Lock()
status = {"enabled": ENABLED, "written": 0, "dropped": 0, "error": None}


def install():
    """Wrap VehiclePipeline.infer before the runtime builds its pipelines."""
    if not ENABLED:
        return
    from app.ai.vehicle.pipeline import VehiclePipeline
    if getattr(VehiclePipeline.infer, "_captures", False):
        return
    original = VehiclePipeline.infer

    def infer(self, frame_bgr, timestamp_s):
        detections = original(self, frame_bgr, timestamp_s)
        test = (TEST and self.cfg.evidence.camera not in _tested and len(self._records) > 1
                and self._records[-1]["time_s"] - self._records[0]["time_s"] >= 35)
        if detections or test:
            try:
                if test:
                    _tested.add(self.cfg.evidence.camera)
                _offer(self, detections)
            except Exception as exc:  # recording must never break inference
                status["error"] = "%s: %s" % (type(exc).__name__, exc)
        return detections

    infer._captures = True
    VehiclePipeline.infer = infer
    threading.Thread(target=_writer, daemon=True, name="vehicle-capture").start()


def _offer(pipeline, detections):
    camera = pipeline.cfg.evidence.camera
    now = time.time()
    with _lock:
        if now - _last.get(camera, 0) < CAPTURE_GAP_S:
            return
        _last[camera] = now
    # The pipeline replaces (never mutates) its records and frames, so references are enough.
    records = [dict(r) for r in pipeline._records]
    frames = {r["frame"]: pipeline._frames[r["frame"]] for r in records if r["frame"] in pipeline._frames}
    item = {
        "camera": camera, "wall_time": now, "resolution": list(pipeline.resolution),
        "calibration": pipeline.cfg.evidence.calibration, "vehicle_masks": pipeline.masks,
        "ignore_zones": pipeline.cfg.rules.ignore_zones, "sample_interval_s": pipeline.sample_interval,
        "detections": [{"label": d.label, "severity": d.severity, "confidence": d.confidence, "box": list(d.box)}
                       for d in detections],
        "decided_frame": records[-1]["frame"] if records else None,
        "records": records,
    }
    try:
        _queue.put_nowait((item, frames))
    except queue.Full:
        status["dropped"] += 1


def _jsonable(value):
    if isinstance(value, dict):
        return {k: _jsonable(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [_jsonable(v) for v in value]
    if hasattr(value, "item"):
        return value.item()
    return value


def _writer():
    import cv2
    while True:
        item, frames = _queue.get()
        try:
            stamp = time.localtime(item["wall_time"])
            folder = ROOT / time.strftime("%Y-%m-%d", stamp) / ("%s-cam-%s" % (time.strftime("%H%M%S", stamp), item["camera"]))
            (folder / "frames").mkdir(parents=True, exist_ok=True)
            for fi, frame in frames.items():
                cv2.imwrite(str(folder / "frames" / ("%d.jpg" % fi)), frame, [cv2.IMWRITE_JPEG_QUALITY, 95])
            (folder / "capture.json").write_text(json.dumps(_jsonable(item), indent=1), encoding="utf-8")
            status["written"] += 1
            status["error"] = None
            _prune()
        except Exception as exc:
            status["error"] = "%s: %s" % (type(exc).__name__, exc)


def _prune():
    captures = sorted(p for day in ROOT.iterdir() if day.is_dir() for p in day.iterdir() if p.is_dir())
    for old in captures[:max(0, len(captures) - MAX_CAPTURES)]:
        shutil.rmtree(old, ignore_errors=True)
    for day in ROOT.iterdir():
        if day.is_dir() and not any(day.iterdir()):
            day.rmdir()
