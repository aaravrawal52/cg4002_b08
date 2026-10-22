import csv
from pathlib import Path
import time
from enum import Enum

CSV_PATH = Path(__file__).parent / "dummy_dataset_tagged" / "unittest_raw.csv"

# Fields of a comms SENSOR packet after the "SENSOR" prefix, plus the FireBeetle tag:
#   SENSOR,sequence,timestamp,ax,ay,az,gx,gy,gz,hall1,hall2,tag
# The dummy CSVs use the same names in the same order.
SENSOR_FIELDS = ["sequence", "timestamp", "ax", "ay", "az", "gx", "gy", "gz", "hall1", "hall2", "tag"]
TAGS = {"IDLE", "ACTIVE"}

class States(Enum):
    WAITING = "waiting"
    COLLECTING = "collecting"
    DISCARDING = "discarding"

class CircularBuffer:
    def __init__(self, size):
        self.size = size # fixed capacity
        self.buffer = [None] * size
        self.start = 0 # index of oldest element currently stored
        self.count = 0 # how many slots actually filled

    def append(self, item):
        self.buffer[(self.start + self.count) % self.size] = item
        if self.count == self.size:
            self.start = (self.start + 1) % self.size  # Overwrite oldest
        else:
            self.count += 1

    def get(self):
        return [self.buffer[(self.start + i) % self.size] for i in range(self.count)]
    
    def clear(self):
        self.buffer = [None] * self.size
        self.start = 0
        self.count = 0

def parse_sensor_message(message):
    """Turn one comms line 'SENSOR,sequence,...,tag' into a row dict, keyed like the CSV rows."""
    parts = message.strip().split(",")
    if parts[0] != "SENSOR":
        raise ValueError(f"Unknown packet type: {parts[0]}")
    if len(parts) != len(SENSOR_FIELDS) + 1:
        raise ValueError(f"Expected {len(SENSOR_FIELDS) + 1} fields, got {len(parts)}")
    row = dict(zip(SENSOR_FIELDS, parts[1:]))
    # if row["tag"] not in TAGS:
    #     raise ValueError(f"Unknown tag: {row['tag']}")
    return row

def format_sensor_message(row):
    """Inverse of parse_sensor_message, e.g. to replay a CSV as comms packets."""
    return "SENSOR," + ",".join(str(row[f]) for f in SENSOR_FIELDS) + "\n"

def stream_rows(csv_path=CSV_PATH, realtime=True):
    """Yield rows from the raw sensor CSV for
    simulating the live inference path.

    args:
    - realtime=True: paced to match the timestamp (ms) intervals recorded in the file
    """

    with open(csv_path, newline="") as file_obj:
        reader = csv.DictReader(file_obj)

        if not realtime:
            yield from reader
            return

        start_wall = None
        start_t_ms = None
        for row in reader:
            t_ms = float(row["timestamp"])

            if start_wall is None:
                start_wall = time.monotonic()
                start_t_ms = t_ms
            else:
                target = start_wall + (t_ms - start_t_ms) / 1000.0
                delay = target - time.monotonic()
                if delay > 0:
                    time.sleep(delay)

            yield row

def segment_windows(data_path, window_size, realtime=True):
    """This function segments the stream with 50% overlap between each window.

    args:
    - window_size: size of each window
    - realtime: pace the read to match stream_rows' recorded timing

    returns:
    cb.buffer: segmented window of data from firebeetle
    """
    cb = CircularBuffer(window_size)
    since_last_window = 0
    stride = window_size // 2
    for row in stream_rows(data_path, realtime=realtime):
        cb.append(row)
        since_last_window += 1
        if cb.count == window_size and since_last_window >= stride:
            since_last_window = 0
            yield cb.get()

class GestureSegmenter:
    '''Acts as state machine. Push-style: feed one row at a time with add_row().'''
    MAX_SEGMENT = 250
    PRE_BUFFER = 25

    def __init__(self):
        self.cb = CircularBuffer(self.PRE_BUFFER)
        self.active_buffer = []
        self.state = States.WAITING
        self.prev_tag = "IDLE"

    def add_row(self, row):
        """Process one row."""
        if row["tag"] in TAGS:
            self.prev_tag = row["tag"]
        else:
            row["tag"] = self.prev_tag
        if row["tag"] == "IDLE":
            segment = None
            if self.state == States.COLLECTING:
                self.cb.clear()
                segment = self.active_buffer
                self.active_buffer = []
            self.cb.append(row)
            self.state = States.WAITING
            return segment
        
        else:
            if self.state == States.DISCARDING:
                return None
            if self.state == States.WAITING:
                self.active_buffer = self.cb.get()
                self.cb.clear()
            self.state = States.COLLECTING
            self.active_buffer.append(row)
            if len(self.active_buffer) >= self.MAX_SEGMENT:
                # discard
                self.state = States.DISCARDING
                self.active_buffer = []
        return None

def segment_gestures(rows):
    '''Pull-style wrapper around GestureSegmenter: yields every finished segment from an iterable of rows.'''
    segmenter = GestureSegmenter()
    for row in rows:
        segment = segmenter.add_row(row)
        if segment is not None:
            yield segment



if __name__ == "__main__":
    data_path = 'dummy_dataset_tagged/unittest_raw.csv'
    window_size = 50
    for window in segment_windows(data_path, window_size):
        print(window)