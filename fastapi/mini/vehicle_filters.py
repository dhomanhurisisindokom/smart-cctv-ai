"""Live-only vetoes on top of the vehicle package's "Penumpang di bak kendaraan" alarms.

The package's rules only reason about cars and trucks; a motorbike is recorded but never used.
In live use a rider next to (or briefly overlapping) a truck was alarmed as a passenger in the bed
(Kamera Real, 05-11-2026 11:21). A person sitting on a detected motorbike is riding it, not in a
vehicle bed, so such alarms are dropped.

Only the alarm output is filtered; the package's tracking, voting and evidence state is untouched.
Set VEHICLE_RIDER_VETO=false to turn it off. Suppressed alarms are counted in /health and still
recorded by vehicle_capture (with "suppressed" in capture.json) for review.
"""
import os

ENABLED = os.environ.get("VEHICLE_RIDER_VETO", "true").strip().lower() == "true"
MOTORBIKE_SCORE = 0.4
status = {"rider_veto": ENABLED, "suppressed": 0}


def is_rider(person, motorbike):
    """Person box sits on the motorbike box: mostly within its width, feet inside its lower part."""
    px1, py1, px2, py2 = person
    mx1, my1, mx2, my2 = motorbike
    pw, mh = max(1.0, px2 - px1), max(1.0, my2 - my1)
    overlap_x = max(0.0, min(px2, mx2) - max(px1, mx1)) / pw
    centre_x = (px1 + px2) / 2.0
    feet_in_bike = my1 + 0.3 * mh <= py2 <= my2 + 0.15 * mh
    return overlap_x >= 0.6 and mx1 <= centre_x <= mx2 and feet_in_bike and py1 < my1


def riders(detections, raw):
    """Detections (pipeline.Detection) that are motorbike riders in the current frame's raw RT-DETR output."""
    if not raw:
        return []
    bikes = [box for box, label, score in zip(raw["boxes"], raw["labels"], raw["scores"])
             if label == "motorbike" and score >= MOTORBIKE_SCORE]
    return [d for d in detections if any(is_rider(d.box, bike) for bike in bikes)]


def install():
    """Wrap VehiclePipeline.infer (before vehicle_capture.install, so captures see the filtered result)."""
    if not ENABLED:
        return
    from app.ai.vehicle.pipeline import VehiclePipeline
    if getattr(VehiclePipeline.infer, "_filters", False):
        return
    original = VehiclePipeline.infer

    def infer(self, frame_bgr, timestamp_s):
        detections = original(self, frame_bgr, timestamp_s)
        self.last_suppressed = []
        if detections:
            dropped = riders(detections, self.last_raw)
            if dropped:
                self.last_suppressed = [{"reason": "rider_veto", "box": list(d.box), "confidence": d.confidence}
                                        for d in dropped]
                status["suppressed"] += len(dropped)
                detections = [d for d in detections if d not in dropped]
        return detections

    infer._filters = True
    VehiclePipeline.infer = infer
