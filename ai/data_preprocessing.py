import json
from pathlib import Path

import numpy as np

from data_ingestion import segment_gestures, stream_rows
from deploy.resample import resample_segment
from config import DATA_DIR, LABEL_MAP_PATH, PREPROCESSED_DIR

def list_sessions(person_dir):
    person_dir = Path(person_dir)
    return sorted(person_dir.iterdir())

def train_val_test_sessions(DATA_DIR, TRAIN_VAL_SESSIONS, TEST_SESSIONS):
    def recordings(people):
        sessions = []
        for person in people:
            sessions.extend(list_sessions(Path(DATA_DIR) / person))
        return sessions

    people = []
    for p in Path(DATA_DIR).iterdir():
        if p.is_dir():
            people.append(p.name)
    people.sort()

    train_val_sessions = people[:TRAIN_VAL_SESSIONS]
    test_sessions = people[TRAIN_VAL_SESSIONS:]
    return recordings(train_val_sessions), recordings(test_sessions)

def raw_csv(recording):
    csv_files = list(recording.glob("*_data.csv"))
    return csv_files[0]

def build_dataset(sessions, COLUMNS, mode):
    """
    Preprocess dataset:
    - split each recording into gesture segments (ACTIVE/IDLE tags) and resample them to TARGET_LEN rows
    - label every segment with the recording's gesture: each folder Data/<glove>/<person>/<gesture>/

    returns:
    - X: list of resampled segments
    - y: list of action class for each segment
    """
    TARGET_LEN = 50
    X = []
    y = []
    group = []
    for session in sessions:
        print("mode: ", mode)
        print("session: ", session)
        gesture = Path(session).name
        if gesture not in action_map:
            print("  skipped: gesture not in label_map.json")
            continue
        label = action_map[gesture]
        instance_raw = raw_csv(session)

        for segment in segment_gestures(stream_rows(instance_raw, realtime=False)):
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
    TRAIN_VAL_SESSIONS = 2
    TEST_SESSIONS = 1
    COLUMNS = ["ax", "ay", "az", "gx", "gy", "gz", "hall1", "hall2"]

    with open(label_map, "r") as file:
        action_map = json.load(file)

    train_val_sessions, test_sessions = train_val_test_sessions(DATA_DIR, TRAIN_VAL_SESSIONS, TEST_SESSIONS)
    
    X_train_val, y_train_val, group_train_val = build_dataset(train_val_sessions, COLUMNS, "train_val")
    X_test, y_test, group_test = build_dataset(test_sessions, COLUMNS, "test")

    # save training into file for training.py to load
    PREPROCESSED_DIR.mkdir(parents=True, exist_ok=True)
    np.savez(PREPROCESSED_DIR / "train_val_data.npz", X = X_train_val, y = y_train_val, group = group_train_val)
    np.savez(PREPROCESSED_DIR / "test_data.npz", X = X_test, y = y_test, group = group_test)
            

