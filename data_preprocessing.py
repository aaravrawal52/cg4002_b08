import json
from pathlib import Path

import numpy as np
import pandas as pd

from data_ingestion import segment_windows, stream_rows

CSV_PATH = Path(__file__).parent / "dummy_dataset" / "sensor_stream_raw.csv"

def list_sessions(sessions_dir):
    sessions_dir = Path(sessions_dir)
    return sorted(session for session in sessions_dir.iterdir())

def train_val_test_sessions(SESSIONS_DIR, TRAIN_SESSIONS, VAL_SESSIONS, TEST_SESSIONS):
    sessions = list_sessions(SESSIONS_DIR)
    train_sessions = set(sessions[:TRAIN_SESSIONS])
    val_sessions = set(sessions[TRAIN_SESSIONS:TRAIN_SESSIONS+VAL_SESSIONS])
    test_sessions = set(sessions[TRAIN_SESSIONS+VAL_SESSIONS:])
    return train_sessions, val_sessions, test_sessions

def fit_zscore_stats(sessions, COLUMNS):
    combined = pd.concat([pd.read_csv(s / 'sensor_stream_raw.csv') for s in sessions], ignore_index=True)
    stats = {c: {"mean": float(np.mean(combined[c])), "std_dev": float(np.std(combined[c]))} for c in COLUMNS}
    with open("zscore_stats.json", "w") as f:
        json.dump(stats, f)
    return stats

def apply_zscore_normalization(df, COLUMNS):
    with open('zscore_stats.json') as json_file:
        stats = json.load(json_file)
    return stats

def label_window(window, df_label, window_size):
    """ This function assignes a class to a window, based on the percentage an action takes up in the window.

    args:
    - window
    - df_label: dataframe containing the labelled gesture class corresponding to the respective indexes from raw data
    - window_size

    returns:
    - most_likely_class: the class assigned to the window
    """
    class_counter = [0] * len(action_map)
    window_start_idx = int(window[0]["sample_idx"])
    window_end_idx = window_start_idx + window_size
    overlap = df_label[
        (df_label["start_idx"] < window_end_idx) & (df_label["end_idx"] > window_start_idx)
    ]
    THRESHOLD = 70
    for index, row in overlap.iterrows():
        lo = max(row["start_idx"], window_start_idx)
        hi = min(row["end_idx"], window_end_idx)
        percentage_overlap = (hi - lo) / window_size * 100

        class_counter[row["action_class"]] += percentage_overlap
    
    most_likely_class = class_counter.index(max(class_counter))
    if class_counter[most_likely_class] >= THRESHOLD:
        return most_likely_class

def build_dataset(sessions, window_size, COLUMNS, mode):
    """
    Preprocess dataset:
    - zscore normalization
    - label windows
    
    returns:
    - X: list of windows
    - y: list of action class for each window
    """
    X = []
    y = []
    for session in sessions:
        print("mode: ", mode)
        print("session: ", session)
        instance_label = session/'sensor_stream_labels.csv'
        instance_raw = session/'sensor_stream_raw.csv'
        df = pd.read_csv(instance_raw)
        df_label = pd.read_csv(instance_label)
        df_label["action_class"] = df_label["gesture"].map(action_map).fillna(0).astype(int)


        stats = apply_zscore_normalization(df, COLUMNS)
        print("applied zscore")

        for window in segment_windows(instance_raw, window_size, realtime=False):
            label = label_window(window, df_label, window_size)
            if label is None:
                continue
            window_values = []
            for row in window:
                row_values =[]
                for c in COLUMNS:
                    row_values.append((float(row[c]) - stats[c]["mean"]) / stats[c]["std_dev"])
                window_values.append(row_values)
            X.append(window_values)
            y.append(label)
    X = np.array(X)
    y = np.array(y)
    return X, y

if __name__ == "__main__":
    SESSIONS_DIR = 'dummy_dataset/sessions'
    label_map = 'dummy_dataset/label_map.json'
    window_size = 50
    TRAIN_SESSIONS = 2
    VAL_SESSIONS = 1
    TEST_SESSIONS = 1
    COLUMNS = ["hall_thumb", "hall_index", "hall_middle", "hall_ring", "hall_pinky", "accel_x", "accel_y", "gyro_z"]

    with open(label_map, "r") as file:
        action_map = json.load(file)

    train_sessions, val_sessions, test_sessions = train_val_test_sessions(SESSIONS_DIR, TRAIN_SESSIONS, VAL_SESSIONS, TEST_SESSIONS)
    fit_zscore_stats(train_sessions, COLUMNS)
    
    X_train, y_train = build_dataset(train_sessions, window_size, COLUMNS, "train")
    print("val session paths: ", val_sessions)
    X_val, y_val = build_dataset(val_sessions,window_size, COLUMNS, "val")
    X_test, y_test = build_dataset(test_sessions, window_size, COLUMNS, "test")

    # save training into file for training.py to load
    np.savez("preprocessed_data/training_data.npz", X = X_train, y = y_train)
    np.savez("preprocessed_data/val_data.npz", X = X_val, y = y_val)
    np.savez("preprocessed_data/test_data.npz", X = X_test, y = y_test)
            

