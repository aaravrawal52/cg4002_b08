# README.md for AI code

Each glove streams 8 channels (accelerometer `ax ay az`, gyroscope `gx gy gz`, finger sensors `hall1 hall2`) tagged `ACTIVE`/`IDLE` by the FireBeetle. One model is trained per glove:

| Glove | Finger sensors | Gestures |
|---|---|---|
| `hall` (left hand) | Hall effect | 11 (see `Data/hall/label_map.json`) |
| `flex` (right hand) | Flex | 12, adds `click` (see `Data/flex/label_map.json`) |

The finger-sensor columns are named `hall1`/`hall2` for both gloves.

## Pipeline

```
FireBeetle stream ─► segment on ACTIVE/IDLE tags (+25-row pre-buffer, max 250 rows)
                  ─► resample to 50 rows ─► z-score ─► INT8 ─► CNN on DPU ─► gesture + confidence
```

- **Model** (`model.py`): 1–3 × [Conv2d (1×k) → ReLU → MaxPool (1×2)] → global average pool → Linear. Input shape `(1, 8, 1, 50)`. Conv2d with 1×k kernels is used instead of Conv1d because the DPU does not support 1D layers.
- **Training**: person-grouped cross-validation (GroupKFold), Optuna TPE tuning with MedianPruner and early stopping, data augmentation (random 3D rotation, IMU scaling, finger-sensor offset, noise), Adam + cross-entropy.
- **Deployment**: Vitis AI quantiser (INT8) and compiler for the DPUCZDX8G B1600 (`arch_ultra96.json`), run with PYNQ + VART.
- **Confidence**: max logit ÷ `logit_ref` (95th percentile of the max logit on correct training predictions), as 0–100%. Below `CONF_THRESHOLD` the gesture is returned as `unknown`.

## Repository layout

| Path | Purpose |
|---|---|
| `config.py` | `GLOVE = "hall"` or `"flex"`: selects which glove every host script works on |
| `sensorcollection.py` | Records serial data from the glove into `Data/<glove>/<person>/<gesture>/<gesture>_data.csv` |
| `data_ingestion.py` | CSV/stream reading and `GestureSegmenter` (ACTIVE/IDLE segmentation) |
| `data_preprocessing.py` | Builds `train_val_data.npz` and `test_data.npz` from the recordings (split by person) |
| `model.py` | CNN definition (`build_model`) |
| `tune.py` | Optuna hyperparameter search → `best_params.json`, `loss_curve.png` |
| `train.py` | Trains the final model (`augment`, `train_model`) → `models/<glove>/<timestamp>/` |
| `test.py` | Float model accuracy and confusion matrix on the held-out person |
| `tests/` | pytest tests for segmentation and packet parsing |
| `vitis_ai/` | Vitis AI Docker workspace: quantise, inspect, compile (see below) |
| `deploy/` | Files copied to the Ultra96: `inference.py`, segmenter, resampler, one folder per glove model |
| `dpu.hwh` | Hardware description of the prebuilt DPU overlay |
| `requirements.txt` | Host PC Python packages |

## Setup (host PC)

```bash
python -m venv .venv
source .venv/bin/activate        # Windows: .venv\Scripts\activate
pip install -r requirements.txt
```

`Data/`, `preprocessed_data/`, `models/` and `optuna.db` are not committed (see `.gitignore`).

## Run order

Run every host command from the repository root. Steps 2–7 use the glove set in `config.py`, so repeat them for each glove.

### 1. Collect data

Set `SERIAL_PORT`, `GLOVE`, `PERSON` and `ACTION` at the top of `sensorcollection.py`, then:

```bash
python sensorcollection.py
```

Each gesture is recorded as 20 repetitions, one gesture per folder: `Data/<glove>/<person>/<gesture>/`. The folder name is the training label, so it must match a key in the glove's `label_map.json` (gesture name → class index). Folders not in the label map are skipped.

### 2. Check the recordings (optional)

```bash
python Data/make_labels.py Data/<glove>
```

Warns about recordings without 20 repetitions, junk lines or out-of-order sequence numbers, and writes a `label.csv` (one row per ACTIVE/IDLE run) next to each recording for inspection. Training does not read `label.csv`.

### 3. Select the glove

Edit `config.py`:

```python
GLOVE = "hall"   # or "flex"
```

### 4. Preprocess

```bash
python data_preprocessing.py
```

Splits every recording into gesture segments using the ACTIVE/IDLE tags (the same `GestureSegmenter` as on the board), resamples each segment to 50 rows and labels it with its folder's gesture. Writes `preprocessed_data/<glove>/train_val_data.npz` and `test_data.npz`. The people are split by `TRAIN_VAL_SESSIONS` / `TEST_SESSIONS` in the `__main__` block.

### 5. Tune hyperparameters

```bash
python tune.py
```

Writes `preprocessed_data/<glove>/best_params.json` and `loss_curve.png`. Results are kept in `optuna.db` under the study `gesture_<glove>`; rerunning continues the same study. `GroupKFold(n_splits=...)` must not exceed the number of training people.

### 6. Train

```bash
python train.py
```

Creates `models/<glove>/<timestamp>/` with `model.pt`, `best_params.json`, `label_map.json` and `zscore_stats.json` (per-channel mean/std and `logit_ref`).

### 7. Evaluate and choose the confidence threshold

```bash
python test.py "models/<glove>/<timestamp>"
```

### 8. Quantise, inspect and compile (Vitis AI Docker)

Start the Vitis AI 3.5 PyTorch CPU container from the Vitis AI repository:

```bash
./docker_run_fixed.sh xilinx/vitis-ai-pytorch-cpu:latest
conda activate vitis-ai-pytorch
```

The workspace needs the contents of `vitis_ai/`, plus `model.py`, laid out as:

```
quantize_model.py  inspect_model.py  arch_ultra96.json  model.py
run/<glove>/                    ← copy of models/<glove>/<timestamp>/
preprocessed_data/<glove>/      ← copy of preprocessed_data/<glove>/
```

Then, for each glove:

```bash
python inspect_model.py <glove>                                   # optional: check every layer runs on the DPU
python quantize_model.py --glove <glove> --quant_mode float       # float accuracy (train+val)
python quantize_model.py --glove <glove> --quant_mode calib       # calibrate INT8 scales
python quantize_model.py --glove <glove> --quant_mode test        # INT8 accuracy on the held-out person
python quantize_model.py --glove <glove> --quant_mode test --deploy
vai_c_xir -x run/<glove>/quantize_result/Sequential_int.xmodel -a arch_ultra96.json -o compiled/<glove> -n compiled_model
```

Ignore the accuracy printed in `calib` mode; use `test` mode to compare INT8 with float. `compiled/<glove>/meta.json` should show target `DPUCZDX8G_ISA1_B1600_0101000016010404`.

### 9. Deploy to the Ultra96

Copy the three files for each glove into `deploy/<glove>/`, all from the **same** training run:

```
deploy/<glove>/compiled_model.xmodel   ← compiled/<glove>/compiled_model.xmodel
deploy/<glove>/zscore_stats.json       ← models/<glove>/<timestamp>/zscore_stats.json
deploy/<glove>/label_map.json          ← models/<glove>/<timestamp>/label_map.json
```

Copy `deploy/` to the board, then on the board run with the PYNQ Python environment:

```bash
python3 inference.py
```

`main()` replays `swipe_down_data.csv` (hall) and `grab_and_release_data.csv` (flex) through the segmenter and both models, printing the prediction, confidence and DPU time for each gesture. For live use, the comms code creates a `GestureClassifier` per glove and calls `classify(segment)` for each finished segment.

## Notes

- Retrain, re-quantise and recompile both gloves after changing preprocessing, resampling or the model, so the board sees inputs processed exactly as in training.
- `zscore_stats.json` must come from the same run as the compiled model.
- Output channels are tuned in multiples of 10 to match the B1600's 10 input × 10 output channel parallelism.
