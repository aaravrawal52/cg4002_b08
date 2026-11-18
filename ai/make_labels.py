"""Write label.csv next to every *_data.csv under Data/ (Data/<person>/<gesture>/<recording>_data.csv).

Tags are resolved like GestureSegmenter.add_row: a tag not in {IDLE, ACTIVE}
UNCLASSIFIED takes the previous valid tag; IDLE if it is the first row.
start_idx/end_idx are sequence numbers.

Use this script to check recordings (repetition count, junk lines, out-of-order sequence numbers).

Run from the repo root:
    python Data/make_labels.py data_path
"""
import csv
import sys
from pathlib import Path

DATA_DIR = Path(__file__).parent
TAGS = {"IDLE", "ACTIVE"}
EXPECTED_REPS = 20
FIELDS = ["gesture", "start_idx", "end_idx", "start_ms", "end_ms", "duration_ms"]


def make_labels(raw_path, gesture):
    with open(raw_path, newline="") as f:
        all_rows = list(csv.DictReader(f))
    # Skip junk lines whose sequence isn't a number
    rows = [r for r in all_rows if (r["sequence"] or "").strip().isdigit()]
    junk = len(all_rows) - len(rows)

    prev_tag = "IDLE"
    resolved = []
    for row in rows:
        if row["tag"] in TAGS:
            prev_tag = row["tag"]
        resolved.append(prev_tag)

    seqs = [int(r["sequence"]) for r in rows]
    times = [float(r["time"]) for r in rows]
    step_ms = times[-1] - times[-2] if len(times) > 1 else 0

    labels = []
    start = 0
    for i in range(1, len(rows) + 1):
        if i < len(rows) and resolved[i] == resolved[start]:
            continue
        end_idx = seqs[i] if i < len(rows) else seqs[-1] + 1
        end_ms = times[i] if i < len(rows) else times[-1] + step_ms
        if resolved[start] == "ACTIVE":
            is_gesture = True
        else:
            is_gesture = False
        labels.append({
            "gesture": gesture if is_gesture else "idle",
            "start_idx": seqs[start],
            "end_idx": end_idx,
            "start_ms": times[start],
            "end_ms": end_ms,
            "duration_ms": end_ms - times[start],
        })
        start = i

    with open(raw_path.parent / "label.csv", "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=FIELDS)
        writer.writeheader()
        writer.writerows(labels)

    backwards = [(a, b) for a, b in zip(seqs, seqs[1:]) if b <= a]
    other_tags = sorted({r["tag"] for r in rows} - TAGS)
    return labels, backwards, other_tags, junk


def main(data_dir):
    wrong_reps = []
    for raw_path in sorted(Path(data_dir).glob("*/*/*_data.csv")):
        gesture = raw_path.parent.name
        labels, backwards, other_tags, junk = make_labels(raw_path, gesture)
        n = sum(1 for l in labels if l["gesture"] != "idle")
        rel = raw_path.relative_to(data_dir).parent
        warn = []
        if n != EXPECTED_REPS:
            warn.append(f"{n} reps, expected {EXPECTED_REPS}")
            wrong_reps.append((str(rel), n))
        if backwards:
            warn.append(f"sequence goes backwards at {backwards}")
        if junk:
            warn.append(f"{junk} junk lines skipped")
        if labels[0]["gesture"] != "idle":
            warn.append("starts mid-gesture")
        if labels[-1]["gesture"] != "idle":
            warn.append("ends mid-gesture")

    print(f"\nRecordings without {EXPECTED_REPS} repetitions: {len(wrong_reps)}")
    for rel, n in wrong_reps:
        print(f"  {rel}: {n}")


if __name__ == "__main__":
    main(Path(sys.argv[1]) if len(sys.argv) > 1 else DATA_DIR)
