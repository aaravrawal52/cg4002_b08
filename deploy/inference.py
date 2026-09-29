import json
import numpy as np
from pynq_dpu import DpuOverlay
from data_ingestion import segment_windows

COLUMNS = ["hall_thumb", "hall_index", "hall_middle", "hall_ring",
           "hall_pinky", "accel_x", "accel_y", "gyro_z"]
WINDOW_SIZE = 50

def load_zscore_stats(path):
    with open(path) as f:
        return json.load(f)


def load_idx_to_gesture(path):
    with open(path) as f:
        action_map = json.load(f)
    return {v: k for k, v in action_map.items()}

def normalize_window(window, stats, columns):
    """window: list of dict-like rows from segment_windows. Returns (50, 8) float32."""
    arr = np.array([[float(row[c]) for c in columns] for row in window], dtype=np.float32)
    for i, c in enumerate(columns):
        arr[:, i] = (arr[:, i] - stats[c]["mean"]) / stats[c]["std_dev"]
    return arr

def main():
    overlay = DpuOverlay("/usr/local/share/pynq-venv/lib/python3.10/site-packages/pynq_dpu/dpu.bit")
    overlay.load_model("compiled_model.xmodel")
    dpu = overlay.runner

    input_tensors = dpu.get_input_tensors()
    output_tensors = dpu.get_output_tensors()
    input_shape = tuple(input_tensors[0].dims)
    output_shape = tuple(output_tensors[0].dims)
    print("input shape:", input_shape, "output shape:", output_shape)

    input_scale = 2 ** input_tensors[0].get_attr("fix_point")

    zscore_stats = load_zscore_stats("zscore_stats.json")
    idx_to_gesture = load_idx_to_gesture("label_map.json")

    input_data = [np.empty(input_shape, dtype=np.int8)]
    output_data = [np.empty(output_shape, dtype=np.int8)]

    for window in segment_windows("sensor_stream_raw.csv", WINDOW_SIZE):
        norm = normalize_window(window, zscore_stats, COLUMNS)
        quantized = np.clip(np.round(norm * input_scale), -128, 127).astype(np.int8)
        input_data[0][0] = quantized.reshape(input_shape[1:])

        job_id = dpu.execute_async(input_data, output_data)
        dpu.wait(job_id)

        pred_class = int(np.argmax(output_data[0][0]))
        print("Predicted:", idx_to_gesture.get(pred_class, "unknown"))


if __name__ == "__main__":
    main()