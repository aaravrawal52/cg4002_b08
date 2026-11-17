import argparse
import json
from pathlib import Path

import numpy as np
import torch
from sklearn.metrics import classification_report, confusion_matrix

from model import build_model
from config import PREPROCESSED_DIR


def load_split(npz_path, run_dir):
    data = np.load(npz_path)
    X, y = data["X"], data["y"]
    COLUMNS = ["ax", "ay", "az", "gx", "gy", "gz", "hall1", "hall2"]
    with open(Path(run_dir) / "zscore_stats.json") as f:
        stats = json.load(f)
    mean = np.array([stats[c]["mean"] for c in COLUMNS])
    std  = np.array([stats[c]["std_dev"] for c in COLUMNS])
    X = (X - mean) / std
    X = np.transpose(X, axes=(0, 2, 1))
    X = torch.from_numpy(X).float().unsqueeze(2)
    y = torch.from_numpy(y).long()
    return X, y

def evaluate(run_dir):
    run_dir = Path(run_dir)
    with open(run_dir / "best_params.json") as f:
        cfg = json.load(f)

    with open(run_dir / 'label_map.json') as f:
        label_map = json.load(f)
    class_names = [None] * len(label_map)   # class_names[index] = gesture name
    for name, index in label_map.items():
        class_names[index] = name
    num_classes = len(class_names)

    model = build_model(cfg["channels"], cfg["kernels"], num_classes)
    model.load_state_dict(torch.load(run_dir / "model.pt", map_location="cpu"))
    model.eval()

    X_test, y_test = load_split(PREPROCESSED_DIR / "test_data.npz", run_dir)


    with torch.no_grad():
        logits = model(X_test)
        y_pred = logits.argmax(1)

    idx = list(range(len(class_names)))
    print(classification_report(y_test, y_pred, zero_division=0))
    print("confusion matrix (rows=true, cols=pred):")
    cm = confusion_matrix(y_test, y_pred, labels=idx)
    width = 0
    for name in class_names:
        width = max(width, len(name))

    header = " " * width + " "      # column header: class numbers
    for i in idx:
        header += f"{i:>4}"
    print(header)

    for i, row in enumerate(cm):
        line = f"{class_names[i]:>{width}} "
        for count in row:
            line += f"{count:>4}"
        line += f"   ({i})"
        print(line)

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("run_dir", help="path to a models/<timestamp> folder produced by train.py")
    args = parser.parse_args()
    evaluate(args.run_dir)
