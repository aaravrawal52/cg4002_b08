import json
from pathlib import Path
import numpy as np
from pynq_dpu import DpuOverlay
from data_ingestion import segment_gestures, stream_rows
from resample import resample_segment
import time

COLUMNS = ["ax", "ay", "az", "gx", "gy", "gz", "hall1", "hall2"]
TARGET_LEN = 50
DEPLOY_DIR = Path(__file__).resolve().parent
DPU_BIT = "/usr/local/share/pynq-venv/lib/python3.10/site-packages/pynq_dpu/dpu.bit"

def load_zscore_stats(path):
    with open(path) as f:
        return json.load(f)


def load_idx_to_gesture(path):
    with open(path) as f:
        action_map = json.load(f)
    return {v: k for k, v in action_map.items()}

def normalize_window(window, stats, columns):
    """window: list of dict-like rows from a segment. Returns (n, 8) float32."""
    arr = np.array([[float(row[c]) for c in columns] for row in window], dtype=np.float32)
    for i, c in enumerate(columns):
        arr[:, i] = (arr[:, i] - stats[c]["mean"]) / stats[c]["std_dev"]
    return arr

def softmax(values):
    values_diff = values - np.max(values)
    exp_values = np.exp(values_diff)
    exp_values_sum = np.sum(exp_values)
    return exp_values/exp_values_sum

class GestureClassifier:
    """Loads the DPU and model once; classify() is then called for each finished segment."""

    def __init__(self, deploy_dir=DEPLOY_DIR):
        deploy_dir = Path(deploy_dir)
        overlay = DpuOverlay(DPU_BIT)
        overlay.load_model(str(deploy_dir / "compiled_model.xmodel"))
        self.dpu = overlay.runner

        input_tensors = self.dpu.get_input_tensors()
        output_tensors = self.dpu.get_output_tensors()
        self.input_shape = tuple(input_tensors[0].dims)
        output_shape = tuple(output_tensors[0].dims)
        print("input shape:", self.input_shape, "output shape:", output_shape)

        self.input_scale = 2 ** input_tensors[0].get_attr("fix_point")
        self.output_scale = 2 ** output_tensors[0].get_attr("fix_point")

        self.zscore_stats = load_zscore_stats(deploy_dir / "zscore_stats.json")
        self.idx_to_gesture = load_idx_to_gesture(deploy_dir / "label_map.json")

        self.input_data = [np.empty(self.input_shape, dtype=np.int8)]
        self.output_data = [np.empty(output_shape, dtype=np.int8)]

    def classify(self, segment):
        """segment: list of rows from GestureSegmenter / segment_gestures.
        Returns (gesture name, confidence, DPU inference time in ms)."""
        norm = normalize_window(segment, self.zscore_stats, COLUMNS)
        norm_resampled = resample_segment(norm, TARGET_LEN)
        quantized = np.clip(np.round(norm_resampled * self.input_scale), -128, 127).astype(np.int8)
        self.input_data[0][0] = quantized.reshape(self.input_shape[1:])

        t0 = time.perf_counter()
        job_id = self.dpu.execute_async(self.input_data, self.output_data)
        self.dpu.wait(job_id)
        t1 = time.perf_counter()

        pred_class = int(np.argmax(self.output_data[0][0]))

        output_data_float = self.output_data[0][0].astype(np.float32)
        logits = output_data_float / self.output_scale
        probabilities = softmax(logits)
        gesture = self.idx_to_gesture.get(pred_class, "unknown")
        return gesture, float(probabilities[pred_class]), (t1 - t0) * 1000


def main():
    """Offline test: replay a tagged CSV through the same segmenter and classifier the server uses."""
    classifier = GestureClassifier()
    for segment in segment_gestures(stream_rows(DEPLOY_DIR / "sensor_stream_raw.csv", realtime=False)):
        gesture, confidence, inference_ms = classifier.classify(segment)
        print(f"inference time: {inference_ms:.2f} ms")
        print("Predicted:", gesture, "(", confidence, ")")


if __name__ == "__main__":
    main()