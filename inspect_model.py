# inspect_model.py
import json
import torch
from pytorch_nndct import Inspector
from model import build_model

with open("run/best_params.json") as f:
    params = json.load(f)

model = build_model(params["channels"], params["kernels"])
model.load_state_dict(torch.load("run/model.pt", map_location="cpu"))
model.eval()

dummy_input = torch.randn(1, 8, 1, 50)  # (windows, channels, additional direction, window_size)

inspector = Inspector("0x101000016010404")  # from arch_ultra96.json
inspector.inspect(model, (dummy_input,), device=torch.device("cpu"),
                   output_dir="inspect", image_format=None)