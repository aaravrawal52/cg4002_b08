import argparse
import json
from pathlib import Path

import numpy as np
import torch
from sklearn.metrics import classification_report, confusion_matrix

from model import build_model


def load_split(npz_path):
    data = np.load(npz_path)
    X, y = data["X"], data["y"]
    X = np.transpose(X, axes=(0, 2, 1))
    X = torch.from_numpy(X).float().unsqueeze(2)
    y = torch.from_numpy(y).long()
    return X, y

def evaluate(run_dir):
    run_dir = Path(run_dir)
    with open(run_dir / "best_params.json") as f:
        cfg = json.load(f)

    model = build_model(cfg["channels"], cfg["kernels"])
    model.load_state_dict(torch.load(run_dir / "model.pt", map_location="cpu"))
    model.eval()

    X_test, y_test = load_split("preprocessed_data/test_data.npz")

    with torch.no_grad():
        logits = model(X_test)
        y_pred = logits.argmax(1)

    print(classification_report(y_test, y_pred, zero_division=0))
    print("confusion matrix (rows=true, cols=pred):")
    print(confusion_matrix(y_test, y_pred))

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("run_dir", help="path to a models/<timestamp> folder produced by train.py")
    args = parser.parse_args()
    evaluate(args.run_dir)
