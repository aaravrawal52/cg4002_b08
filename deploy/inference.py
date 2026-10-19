import json
import numpy as np
from pynq_dpu import DpuOverlay
from data_ingestion import segment_windows
import time

COLUMNS = ["ax", "ay", "az", "gx", "gy", "gz", "hall1", "hall2"]
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

def softmax(values):
    values_diff = values - np.max(values)
    exp_values = np.exp(values_diff)
    exp_values_sum = np.sum(exp_values)
    return exp_values/exp_values_sum

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
    output_scale = 2 ** output_tensors[0].get_attr("fix_point")

    zscore_stats = load_zscore_stats("zscore_stats.json")
    idx_to_gesture = load_idx_to_gesture("label_map.json")

    input_data = [np.empty(input_shape, dtype=np.int8)]
    output_data = [np.empty(output_shape, dtype=np.int8)]

    for window in segment_windows("sensor_stream_raw.csv", WINDOW_SIZE):
        norm = normalize_window(window, zscore_stats, COLUMNS)
        quantized = np.clip(np.round(norm * input_scale), -128, 127).astype(np.int8)
        input_data[0][0] = quantized.reshape(input_shape[1:])

        t0 = time.perf_counter()
        job_id = dpu.execute_async(input_data, output_data)
        dpu.wait(job_id)
        t1 = time.perf_counter()
        print(f"inference time: {(t1 - t0)*1000:.2f} ms")

        pred_class = int(np.argmax(output_data[0][0]))
        
        output_data_float = output_data[0][0].astype(np.float32)
        logits = output_data_float / output_scale
        probabilities = softmax(logits)
        print("Predicted:", idx_to_gesture.get(pred_class, "unknown"), "(", probabilities[pred_class], ")")


if __name__ == "__main__":
    main()