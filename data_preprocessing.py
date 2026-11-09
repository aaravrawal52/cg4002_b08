import json
from pathlib import Path

import numpy as np
import pandas as pd

from data_ingestion import segment_gestures, stream_rows
from deploy.resample import resample_segment
from config import DATA_DIR, LABEL_MAP_PATH, PREPROCESSED_DIR

def list_sessions(person_dir):
    person_dir = Path(person_dir)
    return sorted(person for person in person_dir.iterdir())

def train_val_test_sessions(DATA_DIR, TRAIN_VAL_SESSIONS, TEST_SESSIONS):
    def recordings(people):
        return [r for person in people for r in list_sessions(Path(DATA_DIR) / person)]
    people = sorted(p.name for p in Path(DATA_DIR).iterdir() if p.is_dir())
    train_val_sessions = people[:TRAIN_VAL_SESSIONS]
    test_sessions = people[TRAIN_VAL_SESSIONS:]
    # return train_sessions, val_sessions, test_sessions
    return recordings(train_val_sessions), recordings(test_sessions)

def raw_csv(recording):
    return next(recording.glob("*_data.csv"))

def fit_zscore_stats(sessions, COLUMNS):
    combined = pd.concat([pd.read_csv(raw_csv(s)) for s in sessions], ignore_index=True)
    stats = {c: {"mean": float(np.mean(combined[c])), "std_dev": float(np.std(combined[c]))} for c in COLUMNS}
    with open("zscore_stats.json", "w") as f:
        json.dump(stats, f)
    return stats

def apply_zscore_normalization(df, COLUMNS):
    with open('zscore_stats.json') as json_file:
        stats = json.load(json_file)
    return stats

def label_window(window, df_label):
    """ This function assignes a class to a window, based on the percentage an action takes up in the window.
    The window can be any length (e.g. a gesture segment). Leading IDLE rows (the pre-buffer) are not counted.

    args:
    - window
    - df_label: dataframe containing the labelled gesture class corresponding to the respective indexes from raw data

    returns:
    - most_likely_class: the class assigned to the window
    """
    class_counter = [0] * len(action_map)
    # Skip the pre-buffer: start counting at the first non-IDLE row (or the first row if there is none)
    first_active = next((i for i, row in enumerate(window) if row["tag"] != "IDLE"), 0)
    window_start_idx = int(window[first_active]["sequence"])
    window_end_idx = int(window[-1]["sequence"]) + 1
    length = window_end_idx - window_start_idx
    overlap = df_label[
        (df_label["start_idx"] < window_end_idx) & (df_label["end_idx"] > window_start_idx)
    ]
    THRESHOLD = 70
    for index, row in overlap.iterrows():
        lo = max(row["start_idx"], window_start_idx)
        hi = min(row["end_idx"], window_end_idx)
        percentage_overlap = (hi - lo) / length * 100

        class_counter[row["action_class"]] += percentage_overlap
    
    most_likely_class = class_counter.index(max(class_counter))
    if class_counter[most_likely_class] >= THRESHOLD:
        return most_likely_class

def build_dataset(sessions, window_size, COLUMNS, mode):
    """
    Preprocess dataset:
    - label windows
    
    returns:
    - X: list of windows
    - y: list of action class for each window
    """
    TARGET_LEN = 50
    X = []
    y = []
    group = []
    for session in sessions:
        print("mode: ", mode)
        print("session: ", session)
        instance_label = Path(session)/'label.csv'
        instance_raw = raw_csv(session)
        df = pd.read_csv(instance_raw)
        df_label = pd.read_csv(instance_label)

        df_label = df_label[df_label["gesture"].isin(action_map)].copy()
        df_label["action_class"] = df_label["gesture"].map(action_map).astype(int)

        for segment in segment_gestures(stream_rows(instance_raw, realtime=False)):
            label = label_window(segment, df_label)
            if label is None:
                continue
            window_values = []
            for row in segment:
                row_values =[]
                for c in COLUMNS:
                    row_values.append(float(row[c]))
                window_values.append(row_values)
            window_values = resample_segment(window_values, TARGET_LEN)
            X.append(window_values)
            y.append(label)
            person = int(Path(session).parent.name.replace("person", ""))
            group.append(person)
    X = np.array(X)
    y = np.array(y)
    group = np.array(group)
    return X, y, group

if __name__ == "__main__":
    label_map = LABEL_MAP_PATH
    window_size = 50
    TRAIN_VAL_SESSIONS = 6
    TEST_SESSIONS = 1
    COLUMNS = ["ax", "ay", "az", "gx", "gy", "gz", "hall1", "hall2"]

    with open(label_map, "r") as file:
        action_map = json.load(file)

    train_val_sessions, test_sessions = train_val_test_sessions(DATA_DIR, TRAIN_VAL_SESSIONS, TEST_SESSIONS)
    
    X_train_val, y_train_val, group_train_val = build_dataset(train_val_sessions, window_size, COLUMNS, "train_val")
    X_test, y_test, group_test = build_dataset(test_sessions, window_size, COLUMNS, "test")

    # save training into file for training.py to load
    PREPROCESSED_DIR.mkdir(parents=True, exist_ok=True)
    np.savez(PREPROCESSED_DIR / "train_val_data.npz", X = X_train_val, y = y_train_val, group = group_train_val)
    np.savez(PREPROCESSED_DIR / "test_data.npz", X = X_test, y = y_test, group = group_test)
            

