# inspect_model.py
import json
import sys
from pathlib import Path
import torch
from pytorch_nndct import Inspector
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # repo root, for model.py
from model import build_model

# usage: python inspect_model.py hall|flex
GLOVE = sys.argv[1]
RUN_DIR = Path("run") / GLOVE

with open(RUN_DIR / "best_params.json") as f:
    params = json.load(f)

with open(RUN_DIR / "label_map.json") as f:
    num_classes = len(json.load(f))

model = build_model(params["channels"], params["kernels"], num_classes)
model.load_state_dict(torch.load(RUN_DIR / "model.pt", map_location="cpu"))
model.eval()

dummy_input = torch.randn(1, 8, 1, 50)  # (windows, channels, additional direction, window_size)

inspector = Inspector("0x101000016010404")  # from arch_ultra96.json
inspector.inspect(model, (dummy_input,), device=torch.device("cpu"),
                   output_dir="inspect", image_format=None)