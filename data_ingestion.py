import csv
import time
from pathlib import Path

CSV_PATH = Path(__file__).parent / "dummy_dataset" / "sensor_stream_raw.csv"


def stream_rows(csv_path=CSV_PATH, realtime=True):
    """Yield rows from the raw sensor CSV. With realtime=True (default),
    paced to match the t_ms intervals recorded in the file (i.e. replayed
    as if arriving live at 50 Hz from the FireBeetle), use this for
    simulating the live inference path. Pass realtime=False for offline
    batch processing (e.g. building a training set), where you want the
    whole file read as fast as possible, not replayed in wall-clock time."""
    with open(csv_path, newline="") as file_obj:
        reader = csv.DictReader(file_obj)

        if not realtime:
            yield from reader
            return

        start_wall = None
        start_t_ms = None
        for row in reader:
            t_ms = float(row["t_ms"])

            if start_wall is None:
                start_wall = time.monotonic()
                start_t_ms = t_ms
            else:
                target = start_wall + (t_ms - start_t_ms) / 1000.0
                delay = target - time.monotonic()
                if delay > 0:
                    time.sleep(delay)

            yield row

class CircularBuffer:
    def __init__(self, size):
        self.size = size # fixed capacity, 50
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

def segment_windows(data_path, window_size, realtime=True):
    """This function segments the stream with 50% overlap between each window.

    args:
    window_size: size of each window
    realtime: pace the read to match stream_rows' recorded timing (live
        simulation) vs read the file as fast as possible (offline batch
        processing).

    returns:
    cb.buffer: segmented window of data from firebeetle
    """
    cb = CircularBuffer(window_size)
    since_last_window = 0
    stride = window_size // 2
    # test_stream = [0,1,2,3,4,5]
    # windows = []
    for row in stream_rows(data_path, realtime=realtime):
        cb.append(row)
        since_last_window += 1
        if cb.count == window_size and since_last_window >= stride:
            since_last_window = 0
            yield cb.get()

if __name__ == "__main__":
    data_path = 'dummy_dataset/unittest_raw.csv'
    window_size = 4
    for window in segment_windows(data_path, window_size):
        print(window)
    # for row in stream_rows('dummy_dataset/unittest_raw.csv'):
    #     print(row)