"""Tests for data_ingestion.segment_gestures, using dummy_dataset_tagged.

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
    format_sensor_message,
    parse_sensor_message,
    segment_gestures,
    stream_rows,
)

TAGGED_DIR = ROOT / "dummy_dataset_tagged"
PRE_BUFFER = 25
MAX_SEGMENT = 250


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


def expected_segments(labels_path):
    """(first, last) sequence of the segment each unclassified gesture should produce."""
    with open(labels_path, newline="") as f:
        labels = list(csv.DictReader(f))

    expected = []
    prev_gesture_end = 0  # the pre-buffer only holds IDLE rows after the previous gesture
    for label in labels:
        if label["gesture"] == "idle":
            continue
        start, end = int(label["start_idx"]), int(label["end_idx"])  # half-open [start, end)
        if label["fw_outcome"] == "unclassified":
            first = max(start - PRE_BUFFER, prev_gesture_end)
            last = min(end, start + MAX_SEGMENT - PRE_BUFFER) - 1
            expected.append((first, last))
        prev_gesture_end = end
    return expected


# --- Tagged dataset files ---

def test_unittest_file():
    # 3 IDLE, 2 ACTIVE, 1 UNCLASSIFIED -> one segment covering sequence 0-5
    got = run(stream_rows(TAGGED_DIR / "unittest_raw.csv", realtime=False))
    assert got == [(0, 5)], f"got {got}"


def test_sessions_match_answer_key():
    failures = []
    for session_dir in sorted((TAGGED_DIR / "sessions").iterdir()):
        want = expected_segments(session_dir / "sensor_stream_labels.csv")
        got = run(stream_rows(session_dir / "sensor_stream_raw.csv", realtime=False))
        if len(got) != len(want):
            failures.append(f"{session_dir.name}: {len(got)} segments, expected {len(want)}")
            continue
        mismatches = [(g, w) for g, w in zip(got, want) if g != w]
        if mismatches:
            g, w = mismatches[0]
            failures.append(f"{session_dir.name}: {len(mismatches)} wrong boundaries, first got {g} expected {w}")
    assert not failures, "\n".join(failures)


# --- Hand-made edge cases ---

def test_only_idle_yields_nothing():
    assert run(make_rows([("IDLE", 40)])) == []


def test_full_pre_buffer():
    # Only the last 25 of the 30 IDLE rows are kept, plus 40 ACTIVE + 1 UNCLASSIFIED
    got = run(make_rows([("IDLE", 30), ("ACTIVE", 40), ("UNCLASSIFIED", 1)]))
    assert got == [(5, 70)], f"got {got}"


def test_short_pre_buffer():
    got = run(make_rows([("IDLE", 10), ("ACTIVE", 5), ("UNCLASSIFIED", 1)]))
    assert got == [(0, 15)], f"got {got}"


def test_classified_gesture_is_discarded():
    # ACTIVE -> IDLE means the FireBeetle classified it
    got = run(make_rows([("IDLE", 30), ("ACTIVE", 20), ("IDLE", 5)]))
    assert got == [], f"got {got}"


def test_pre_buffer_after_classified_gesture():
    # The next segment's pre-buffer should only hold the 5 IDLE rows after the classified gesture
    got = run(make_rows([("IDLE", 30), ("ACTIVE", 10), ("IDLE", 5), ("ACTIVE", 10), ("UNCLASSIFIED", 1)]))
    assert got == [(40, 55)], f"got {got}"


def test_back_to_back_unclassified():
    # Second gesture follows immediately, so it has no pre-buffer
    got = run(make_rows([("IDLE", 30), ("ACTIVE", 10), ("UNCLASSIFIED", 1), ("ACTIVE", 10), ("UNCLASSIFIED", 1)]))
    assert got == [(5, 40), (41, 51)], f"got {got}"


def test_unclassified_without_active_yields_nothing():
    # ACTIVE rows lost in transit: a lone UNCLASSIFIED shouldn't produce a segment
    got = run(make_rows([("IDLE", 30), ("UNCLASSIFIED", 1)]))
    assert got == [], f"got {got}"


def test_no_segment_exceeds_max_length():
    # UNCLASSIFIED never arrives within the firmware limit (e.g. it was lost)
    # segments = list(segment_gestures(make_rows([("IDLE", 30), ("ACTIVE", 300), ("UNCLASSIFIED", 1)])))
    got = run(make_rows([("IDLE", 30), ("ACTIVE", 300), ("UNCLASSIFIED", 1)]))
    assert got == [], f"got {got}"

def test_all_full():
    got = run(make_rows([("IDLE", 25), ("ACTIVE", 225), ("UNCLASSIFIED", 1)]))
    assert got == [], f"got {got}"


# --- Comms packet format ---

def test_csv_header_matches_comms_fields():
    with open(TAGGED_DIR / "unittest_raw.csv", newline="") as f:
        header = next(csv.reader(f))
    assert header == SENSOR_FIELDS, f"got {header}"


def test_parse_sensor_message():
    row = parse_sensor_message("SENSOR,42,15321,0.01,-0.02,1.0,0.5,-0.5,3.2,2050,2110,ACTIVE\n")
    assert row["sequence"] == "42" and row["hall2"] == "2110" and row["tag"] == "ACTIVE", f"got {row}"


def test_parse_rejects_bad_packets():
    bad = [
        "SENSOR,42,15321,0.01,-0.02,1.0,0.5,-0.5,3.2,2050,2110",             # no tag (current comms format)
        "SENSOR,42,15321,0.01,-0.02,1.0,0.5,-0.5,3.2,2050,2110,MOVING",      # unknown tag
        "DATA,42,15321,0.01,-0.02,1.0,0.5,-0.5,3.2,2050,2110,IDLE",          # wrong packet type
    ]
    for message in bad:
        try:
            parse_sensor_message(message)
        except ValueError:
            continue
        raise AssertionError(f"accepted {message!r}")


def test_csv_replayed_as_packets_gives_same_segments():
    # Simulates the live path: every CSV row sent as a comms packet, then parsed on the Ultra96
    session_dir = sorted((TAGGED_DIR / "sessions").iterdir())[0]
    csv_rows = list(stream_rows(session_dir / "sensor_stream_raw.csv", realtime=False))
    packet_rows = [parse_sensor_message(format_sensor_message(r)) for r in csv_rows]
    assert packet_rows == csv_rows
    assert run(packet_rows) == run(csv_rows)

if __name__ == "__main__":
    tests = [(name, fn) for name, fn in globals().items() if name.startswith("test_") and callable(fn)]
    passed = 0
    for name, fn in tests:
        try:
            fn()
            print(f"[PASS] {name}")
            passed += 1
        except AssertionError as e:
            print(f"[FAIL] {name}: {e}")
    print(f"\n{passed}/{len(tests)} passed")
