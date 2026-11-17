import json
from pathlib import Path
import numpy as np
from pynq_dpu import DpuOverlay
from data_ingestion import segment_gestures, stream_rows
from resample import resample_segment
import time

COLUMNS = ["ax", "ay", "az", "gx", "gy", "gz", "hall1", "hall2"]
TARGET_LEN = 50
CONF_THRESHOLD = 50   # %, below this the gesture is "unknown"
DEPLOY_DIR = Path(__file__).resolve().parent
DPU_BIT = "/usr/local/share/pynq-venv/lib/python3.10/site-packages/pynq_dpu/dpu.bit"

def load_zscore_stats(path):
    with open(path) as f:
        return json.load(f)


def load_idx_to_gesture(path):
    with open(path) as f:
        action_map = json.load(f)
    idx_to_gesture = {}
    for gesture, index in action_map.items():
        idx_to_gesture[index] = gesture
    return idx_to_gesture

def normalize_window(window, stats, columns):
    """
    args: 
    - window: list of dict-like rows from a segment. 
    - stats: mean and std_dev of training dataset
    - columns: columns in the dataset

    returns: 
    - (n, 8) float32
    """
    values = []
    for row in window:
        row_values = []
        for c in columns:
            row_values.append(float(row[c]))
        values.append(row_values)
    arr = np.array(values, dtype=np.float32)
    for i, c in enumerate(columns):
        arr[:, i] = (arr[:, i] - stats[c]["mean"]) / stats[c]["std_dev"]
    return arr

class GestureClassifier:
    """Loads the DPU and model once; classify() is then called for each finished segment."""
    def __init__(self, overlay, model_dir):
        model_dir = Path(model_dir)
        self.overlay = overlay
        self.overlay.load_model(str(model_dir / "compiled_model.xmodel"))
        self.runner = self.overlay.runner
        self.graph = self.overlay.graph

        input_tensors = self.runner.get_input_tensors()
        output_tensors = self.runner.get_output_tensors()
        self.input_shape = tuple(input_tensors[0].dims)
        output_shape = tuple(output_tensors[0].dims)
        print("input shape:", self.input_shape, "output shape:", output_shape)

        self.input_scale = 2 ** input_tensors[0].get_attr("fix_point")
        self.output_scale = 2 ** output_tensors[0].get_attr("fix_point")

        self.zscore_stats = load_zscore_stats(model_dir / "zscore_stats.json")
        self.idx_to_gesture = load_idx_to_gesture(model_dir / "label_map.json")

        self.input_data = [np.empty(self.input_shape, dtype=np.int8)]
        self.output_data = [np.empty(output_shape, dtype=np.int8)]

    def classify(self, segment):
        """
        args:
        - segment: list of rows from GestureSegmenter

        returns: 
        - (gesture name, confidence, DPU inference time in ms).
        """
        norm = normalize_window(segment, self.zscore_stats, COLUMNS)
        norm_resampled = resample_segment(norm, TARGET_LEN)
        quantized = np.clip(np.round(norm_resampled * self.input_scale), -128, 127).astype(np.int8)
        self.input_data[0][0] = quantized.reshape(self.input_shape[1:])

        t0 = time.perf_counter()
        job_id = self.runner.execute_async(self.input_data, self.output_data)
        self.runner.wait(job_id)
        t1 = time.perf_counter()

        pred_class = int(np.argmax(self.output_data[0][0]))

        output_data_float = self.output_data[0][0].astype(np.float32)
        logits = output_data_float / self.output_scale
        max_logit = logits[pred_class]
        confidence = float(np.clip(max_logit / self.zscore_stats["logit_ref"], 0, 1) * 100)
        gesture = self.idx_to_gesture.get(pred_class, "unknown")
        if confidence < CONF_THRESHOLD:
            gesture = "unknown"
        return gesture, confidence, (t1 - t0) * 1000


def main():
    """Offline test: replay a tagged CSV through the same segmenter and classifier the server uses."""
    overlay = DpuOverlay(DPU_BIT)
    hall = GestureClassifier(overlay, DEPLOY_DIR/"hall")
    flex = GestureClassifier(overlay, DEPLOY_DIR/ "flex")
    
    hall_segs = segment_gestures(stream_rows(DEPLOY_DIR / "swipe_down_data.csv", realtime=False))
    flex_segs = segment_gestures(stream_rows(DEPLOY_DIR / "grab_and_release_data.csv", realtime=False))

    for hall_seg, flex_seg in zip(hall_segs, flex_segs):
        for name, classifier, segment, expected in (("hall", hall, hall_seg, "swipe_down"),
                                         ("flex", flex, flex_seg, "grab_and_release")):
            gesture, confidence, inference_ms = classifier.classify(segment)
            print(f"inference time: {inference_ms:.2f} ms")
            if gesture == expected:
                result = "OK"
            else:
                result = "WRONG"
            print(f"Predicted: {gesture} ({confidence}) {result}")



if __name__ == "__main__":
    main()