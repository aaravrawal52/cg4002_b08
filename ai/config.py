from pathlib import Path

GLOVE = "flex"                      # change to "flex" for the other hand

DATA_DIR         = Path("Data") / GLOVE
LABEL_MAP_PATH   = DATA_DIR / "label_map.json"
PREPROCESSED_DIR = Path("preprocessed_data") / GLOVE
BEST_PARAMS_PATH = PREPROCESSED_DIR / "best_params.json"
MODELS_DIR       = Path("models") / GLOVE