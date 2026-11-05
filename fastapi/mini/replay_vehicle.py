"""Replay one vehicle capture (see vehicle_capture.py) through the live rules and the original package.

Run inside the AI image, with the capture folder and the original vehicle_violation package mounted:

    docker run --rm --gpus all \
      -v /path/to/<capture>:/capture -v /path/to/vehicle_violation:/package:ro \
      smartcctv-mini-api:latest python -m mini.replay_vehicle /capture /package --device cuda

Writes /capture/report.json and prints a short summary:
- live: the live pipeline's decision for every frame of the window, recomputed from the recorded
  RT-DETR records and frames (same code path as VehiclePipeline._infer), with the person/vehicle
  fields that led to it (status, evidence, veto, open-bed, motion);
- package: the frames turned into a 1 fps video and run through `vv scan` + `vv evaluate` with the
  package's own config.json, i.e. how the package author validates it. Its events.json is kept.

The 30-minute static-object probe cannot run on a 60 s window in either path.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from collections import deque
from pathlib import Path

WEIGHTS = Path("/app/app/ai/weights/vehicle")


def jsonable(value):
    if isinstance(value, dict):
        return {str(k): jsonable(v) for k, v in value.items()}
    if isinstance(value, (list, tuple, set)):
        return [jsonable(v) for v in value]
    if hasattr(value, "tolist"):
        return value.tolist()
    if hasattr(value, "item"):
        return value.item()
    return value


def load(capture):
    import cv2
    meta = json.loads((capture / "capture.json").read_text(encoding="utf-8"))
    frames = {}
    for record in meta["records"]:
        image = cv2.imread(str(capture / "frames" / ("%d.jpg" % record["frame"])))
        if image is None:
            raise SystemExit("frame %s hilang" % record["frame"])
        frames[record["frame"]] = image
    return meta, frames


def live(meta, frames, device):
    from app.ai.vehicle.pipeline import VehiclePipeline
    from app.ai.vehicle.evaluate import decide
    from app.ai.vehicle.rules import BED
    cal = meta["calibration"][0]
    profile = {"resolution": meta["resolution"], "a": cal["a"], "b": cal["b"],
               "vehicle_masks": meta["vehicle_masks"], "ignore_zones": meta["ignore_zones"]}
    settings = {"profiles": {meta["camera"]: profile}, "sample_interval_seconds": meta["sample_interval_s"],
                "window_seconds": 60, "max_records": 120, "max_gap_seconds": 8, "cpu_threads": 4, "device": device}
    pipeline = VehiclePipeline(WEIGHTS, settings, {"camera_id": 0, "vehicle_profile": meta["camera"]})
    records = [dict(r) for r in meta["records"]]
    pipeline._records = deque(records)
    pipeline._frames = dict(frames)
    w, h = meta["resolution"]
    result = decide(list(records), {"width": w, "height": h, "fps": 1 / meta["sample_interval_s"]}, pipeline.cfg,
                    log=lambda _: None, provider=pipeline._provider, vehicle_mask=pipeline.masks)
    out = []
    t0 = records[0]["time_s"]
    for frame in result["res"].frames:
        vehicles = {v["tid"]: v for v in frame.vehicles}
        flagged = []
        for person in frame.persons:
            vehicle = vehicles.get(person.get("tid"))
            alarm = (person.get("voted") == BED and vehicle is not None and vehicle.get("in_motion")
                     and vehicle.get("n_bed", 0) > 0)
            if person.get("status") not in ("ground", None) or alarm:
                flagged.append({"alarm": bool(alarm), "person": jsonable(person), "vehicle": jsonable(vehicle)})
        if flagged:
            out.append({"frame": frame.frame, "t": round(frame.time_s - t0, 2), "persons": flagged})
    return out


def package(meta, frames, capture, package_dir, device):
    import cv2
    work = capture / "package_run"
    shutil.rmtree(work, ignore_errors=True)
    project = work / "project"
    project.mkdir(parents=True)
    for item in ("src", "masks", "config.json"):
        src = package_dir / item
        (shutil.copytree if src.is_dir() else shutil.copy)(src, project / item)
    # Same weights the live service uses (the package ships identical models).
    (project / "models").symlink_to(WEIGHTS, target_is_directory=True)
    video = work / "window.mp4"
    w, h = meta["resolution"]
    writer = cv2.VideoWriter(str(video), cv2.VideoWriter_fourcc(*"mp4v"), 1.0, (w, h))
    for record in meta["records"]:
        writer.write(frames[record["frame"]])
    writer.release()
    env = dict(os.environ, PYTHONPATH=str(project / "src"), HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1",
               PYTHONIOENCODING="utf-8")
    config = str(project / "config.json")
    scan = work / "scan.jsonl"
    log = []
    for args in (["scan", str(video), "--out", str(scan), "--device", device],
                 ["evaluate", str(scan), "--video", str(video), "--out-dir", str(work / "evaluate"), "--no-sheets"]):
        run = subprocess.run([sys.executable, "-m", "vv", "--config", config, *args], cwd=project, env=env,
                             capture_output=True, text=True)
        log.append({"cmd": args[0], "code": run.returncode, "tail": (run.stdout + run.stderr)[-3000:]})
        if run.returncode:
            return {"error": "vv %s gagal" % args[0], "log": log}
    events_file = work / "evaluate" / "events.json"
    events = json.loads(events_file.read_text(encoding="utf-8")) if events_file.exists() else None
    return {"events": events, "log": log}


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("capture", type=Path)
    parser.add_argument("package", type=Path, help="folder vehicle_violation (berisi src/, config.json, masks/)")
    parser.add_argument("--device", default="cpu")
    args = parser.parse_args()
    meta, frames = load(args.capture)
    report = {"capture": str(args.capture), "camera": meta["camera"], "window_frames": len(meta["records"]),
              "live_detections_at_capture": meta["detections"]}
    report["live"] = live(meta, frames, args.device)
    report["package"] = package(meta, frames, args.capture, args.package, args.device)
    (args.capture / "report.json").write_text(json.dumps(jsonable(report), indent=1), encoding="utf-8")
    alarms = [f for f in report["live"] if any(p["alarm"] for p in f["persons"])]
    print("live: %d frame dengan alarm dari %d frame" % (len(alarms), len(meta["records"])))
    pkg = report["package"]
    if "error" in pkg:
        print("package: " + pkg["error"])
    else:
        ev = pkg["events"]
        items = ev.get("events", ev) if isinstance(ev, dict) else ev
        print("package: %s event" % (len(items) if isinstance(items, list) else "?"))
    print("report: %s" % (args.capture / "report.json"))


if __name__ == "__main__":
    main()
