import csv
import time
from pathlib import Path
from data_ingestion import stream_rows, segment_windows
import pandas as pd
from pathlib import Path
import json
import numpy as np

CSV_PATH = Path(__file__).parent / "dummy_dataset" / "sensor_stream_raw.csv"

def list_sessions(sessions_dir):
    """Each session folder has its own sensor_stream_raw.csv + sensor_stream_labels.csv"""
    sessions_dir = Path(sessions_dir)
    return sorted(session for session in sessions_dir.iterdir())

def train_val_test_sessions(SESSIONS_DIR, TRAIN_SESSIONS, VAL_SESSIONS, TEST_SESSIONS):
    sessions = list_sessions(SESSIONS_DIR)
    train_sessions = set(sessions[:TRAIN_SESSIONS])
    val_sessions = set(sessions[TRAIN_SESSIONS:TRAIN_SESSIONS+VAL_SESSIONS])
    test_sessions = set(sessions[TRAIN_SESSIONS+VAL_SESSIONS:])
    return train_sessions, val_sessions, test_sessions

def zscore_normalization(df, COLUMNS):
    for column in COLUMNS:
        mean = np.mean(df[column])
        std_dev = np.std(df[column])
        z_scores = (df[column] - mean) / std_dev
        df.replace({column: z_scores}, inplace = True)
        data = {
            "column": column,
            "mean": mean,
            "std_dev": std_dev
        }
        zscore_stats = json.dumps(data)
        with open("zscore_stats.json", "w") as f:
            f.write(zscore_stats)
    return df

def apply_zscore_normalization(df, COLUMNS):
    with open('zscore_stats.json') as json_file:
        data = json.load(json_file)
        column = data["column"]
        mean = data["mean"]
        std_dev = data["std_dev"]
    z_scores = (df[column] - mean) / std_dev
    df.replace({column: z_scores}, inplace = True)

def label_window(window, df_label, window_size):
    class_counter = [0] * len(action_map)
    window_start_idx = int(window[0]["sample_idx"])
    window_end_idx = window_start_idx + window_size
    overlap = df_label[
        (df_label["start_idx"] < window_end_idx) & (df_label["end_idx"] > window_start_idx)
    ]
    THRESHOLD = 50
    for index, row in overlap.iterrows():
        lo = max(row["start_idx"], window_start_idx)
        hi = min(row["end_idx"], window_end_idx)
        percentage_overlap = (hi - lo) / window_size * 100

        class_counter[row["action_class"]] += percentage_overlap
    
    most_likely_class = class_counter.index(max(class_counter))
    # print(most_likely_class)
    if class_counter[most_likely_class] >= THRESHOLD:
        return most_likely_class

def build_dataset(sessions, COLUMNS, mode):
    X = []
    y = []
    for session in sessions:
        print("mode: ", mode)
        print("session: ",session)
        # instance_label = session/'sensor_stream_labels.csv'
        # instance_raw = session/'sensor_stream_raw.csv'
        instance_label = 'dummy_dataset/unittest_labels.csv'
        instance_raw = 'dummy_dataset/unittest_raw.csv'
        df = pd.read_csv(instance_raw)
        df_label = pd.read_csv(instance_label)
        df_label["action_class"] = df_label["gesture"].map(action_map).fillna(0).astype(int)

        if mode == "train":
            zscore_normalization(df, COLUMNS) # z-score normalization on raw data
        else:
            apply_zscore_normalization(df, COLUMNS)
            print("applied zscore")

        for window in segment_windows(instance_raw, 4):
            label = label_window(window, df_label, window_size)
            # print(label)
            if label is None:
                continue
            window_values = []
            for row in window:
                row_values =[]
                for c in COLUMNS:
                    row_values.append(float(row[c]))
                window_values.append(row_values)
            X.append(window_values)
            y.append(label)
    X = np.array(X)
    y = np.array(y)
    return X, y

if __name__ == "__main__":
    SESSIONS_DIR = 'dummy_dataset/sessions'
    label_map = 'dummy_dataset/label_map.json'
    window_size = 4
    TRAIN_SESSIONS = 2
    VAL_SESSIONS = 1
    TEST_SESSIONS = 1
    COLUMNS = ["hall_thumb", "hall_index", "hall_middle", "hall_ring", "hall_pinky", "accel_x", "accel_y", "gyro_z"]

    with open(label_map, "r") as file:
        action_map = json.load(file)

    train_sessions, val_sessions, test_sessions = train_val_test_sessions(SESSIONS_DIR, TRAIN_SESSIONS, VAL_SESSIONS, TEST_SESSIONS)
    X_train, y_train = build_dataset(train_sessions, COLUMNS, "train")
    X_val, y_val = build_dataset(val_sessions, COLUMNS, "val")
    X_test, y_test = build_dataset(test_sessions, COLUMNS, "test")

    # save training into file for training.py to load
    np.savez("preprocessed_data/training_data.npz", X_train, y_train)
    np.savez("preprocessed_data/val_data.npz", X_val, y_val)
    np.savez("preprocessed_data/test_data.npz", X_test, y_test)
            

