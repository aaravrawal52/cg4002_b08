"""Tests for data_ingestion.segment_gestures

The FireBeetle sends IDLE / ACTIVE tags; ML classifies gestures.

Run from the repo root with either:
    python -m pytest tests
    python tests/test_segment_gestures.py
"""
import csv
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from data_ingestion import (
    SENSOR_FIELDS,
    GestureSegmenter,
    format_sensor_message,
    parse_sensor_message,
    segment_gestures,
    stream_rows,
)

TAGGED_DIR = ROOT / "dummy_dataset_tagged"
PRE_BUFFER = GestureSegmenter.PRE_BUFFER
MAX_SEGMENT = GestureSegmenter.MAX_SEGMENT
MAX_ACTIVE = 225  # firmware cuts a gesture off after this many rows


def idx_range(segment):
    """(first sequence, last sequence) of a segment."""
    return int(segment[0]["sequence"]), int(segment[-1]["sequence"])


def run(rows):
    """List of (first, last) sequence for every segment yielded."""
    return [idx_range(s) for s in segment_gestures(rows)]


def make_rows(spec):
    """spec: list of (tag, count) -> row dicts with consecutive sequence."""
    rows = []
    for tag, count in spec:
        for _ in range(count):
            rows.append({"sequence": str(len(rows)), "tag": tag})
    return rows


def expected_segments(labels_path, n_rows):
    """(first, last) sequence of the segment each gesture should produce."""
    with open(labels_path, newline="") as f:
        labels = list(csv.DictReader(f))

    expected = []
    prev_active_end = 0  # the pre-buffer only holds IDLE rows after the previous gesture
    for label in labels:
        if label["gesture"] == "idle":
            continue
        start, end = int(label["start_idx"]), int(label["end_idx"])  # half-open [start, end)
        active_end = min(end, start + MAX_ACTIVE)
        first = max(start - PRE_BUFFER, prev_active_end)
        too_long = active_end - first >= MAX_SEGMENT
        no_idle_after = active_end >= n_rows  # stream ends mid-gesture
        if not too_long and not no_idle_after:
            expected.append((first, active_end - 1))
        prev_active_end = active_end
    return expected


def test_only_idle_yields_nothing():
    assert run(make_rows([("IDLE", 40)])) == []


def test_full_pre_buffer():
    # Only the last 25 of the 30 IDLE rows are kept, plus the 40 ACTIVE rows
    got = run(make_rows([("IDLE", 30), ("ACTIVE", 40), ("IDLE", 1)]))
    assert got == [(5, 69)], f"got {got}"


def test_short_pre_buffer():
    got = run(make_rows([("IDLE", 10), ("ACTIVE", 5), ("IDLE", 1)]))
    assert got == [(0, 14)], f"got {got}"


def test_no_pre_buffer():
    # Stream starts mid-gesture: segment is just the ACTIVE rows
    got = run(make_rows([("ACTIVE", 10), ("IDLE", 1)]))
    assert got == [(0, 9)], f"got {got}"


def test_ending_idle_row_not_in_segment():
    segments = list(segment_gestures(make_rows([("IDLE", 30), ("ACTIVE", 40), ("IDLE", 1)])))
    assert len(segments) == 1, f"got {len(segments)} segments"
    assert segments[0][-1]["tag"] == "ACTIVE", f"last row tag {segments[0][-1]['tag']}"


def test_one_segment_per_gesture():
    # Several IDLE rows after a gesture must not return the same segment again
    got = run(make_rows([("IDLE", 30), ("ACTIVE", 10), ("IDLE", 5)]))
    assert got == [(5, 39)], f"got {got}"


def test_pre_buffer_after_previous_gesture():
    # The next segment's pre-buffer starts at the IDLE row that ended the previous one
    got = run(make_rows([("IDLE", 30), ("ACTIVE", 10), ("IDLE", 5), ("ACTIVE", 10), ("IDLE", 1)]))
    assert got == [(5, 39), (40, 54)], f"got {got}"


def test_segments_not_modified_later():
    # A returned segment must be a fresh list, not the buffer the segmenter keeps appending to
    segments = list(segment_gestures(make_rows([("IDLE", 30), ("ACTIVE", 10), ("IDLE", 5), ("ACTIVE", 10), ("IDLE", 1)])))
    got = [idx_range(s) for s in segments]
    assert got == [(5, 39), (40, 54)], f"got {got}"
    lengths = [len(s) for s in segments]
    assert lengths == [35, 15], f"got lengths {lengths}"


def test_gesture_at_end_of_stream_yields_nothing():
    # No IDLE row arrives, so the gesture never finishes
    got = run(make_rows([("IDLE", 30), ("ACTIVE", 10)]))
    assert got == [], f"got {got}"


def test_no_segment_exceeds_max_length():
    got = run(make_rows([("IDLE", 30), ("ACTIVE", 300), ("IDLE", 1)]))
    assert got == [], f"got {got}"


def test_all_full():
    # 25 pre-buffer + 225 ACTIVE reaches MAX_SEGMENT -> discarded
    got = run(make_rows([("IDLE", 25), ("ACTIVE", 225), ("IDLE", 1)]))
    assert got == [], f"got {got}"


def test_just_under_max_length():
    got = run(make_rows([("IDLE", 25), ("ACTIVE", 224), ("IDLE", 1)]))
    assert got == [(0, 248)], f"got {got}"


def test_gesture_after_discarded_one():
    # The segmenter recovers after a too-long gesture
    got = run(make_rows([("IDLE", 30), ("ACTIVE", 300), ("IDLE", 5), ("ACTIVE", 10), ("IDLE", 1)]))
    assert got == [(330, 344)], f"got {got}"


def test_add_row_push_style():
    segmenter = GestureSegmenter()
    rows = make_rows([("IDLE", 30), ("ACTIVE", 40), ("IDLE", 5)])
    finished = []
    for i, row in enumerate(rows):
        segment = segmenter.add_row(row)
        if segment is not None:
            finished.append((i, idx_range(segment)))
    assert finished == [(70, (5, 69))], f"got {finished}"



def test_corrupted_tags():
    kept_active = run(make_rows([("IDLE", 30), ("ACTIVE", 10), ("BAD", 1), ("IDLE", 1)]))
    assert kept_active == [(5,40)], f"got {kept_active}"
    kept_idle = run(make_rows([("IDLE", 30),("BAD", 5), ("ACTIVE", 10), ("IDLE", 1)]))
    assert kept_idle == [(10,44)], f"got {kept_idle}"
    corrupted_first = run(make_rows([("BAD", 5), ("ACTIVE", 10), ("IDLE", 1)]))
    assert corrupted_first == [(0,14)], f"got {corrupted_first}"
    no_phantom = run(make_rows([("IDLE", 1), ("BAD", 5), ("IDLE", 10), ]))
    assert no_phantom == [], f"got {no_phantom}"


def test_csv_replayed_as_packets_gives_same_segments():
    # Simulates the live path: every CSV row sent as a comms packet, then parsed on the Ultra96
    session_dir = sorted((TAGGED_DIR / "sessions").iterdir())[0]
    csv_rows = list(stream_rows(session_dir / "sensor_stream_raw.csv", realtime=False))
    packet_rows = [parse_sensor_message(format_sensor_message(r)) for r in csv_rows]
    assert packet_rows == csv_rows
    assert run(packet_rows) == run(csv_rows)

if __name__ == "__main__":
    tests = []
    for name, fn in list(globals().items()):
        if name.startswith("test_") and callable(fn):
            tests.append((name, fn))
    passed = 0
    for name, fn in tests:
        try:
            fn()
            print(f"[PASS] {name}")
            passed += 1
        except Exception as e:
            print(f"[FAIL] {name}: {type(e).__name__}: {e}")
    print(f"\n{passed}/{len(tests)} passed")
