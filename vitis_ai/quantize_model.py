import sys
import argparse
from pytorch_nndct.apis import torch_quantizer
import torch
from pathlib import Path
from sklearn.metrics import classification_report, confusion_matrix
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # repo root, for model.py
from model import build_model
import numpy as np
import json

device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

parser = argparse.ArgumentParser()

parser.add_argument('--glove', choices=['hall', 'flex'], required=True,
    help='reads preprocessed_data/<glove>/ and run/<glove>/')
parser.add_argument('--quant_mode', 
    default='calib', 
    choices=['float', 'calib', 'test'], 
    help='quantization mode. 0: no quantization, evaluate float model, calib: quantize, test: evaluate quantized model')
parser.add_argument('--deploy', 
    dest='deploy',
    action='store_true',
    help='export xmodel for deployment')
parser.add_argument('--inspect', 
    dest='inspect',
    action='store_true',
    help='inspect model')

def load_data(data_dir, model_dir):
    train_val_data = np.load(data_dir/'train_val_data.npz')
    test_data = np.load(data_dir/'test_data.npz')
    X, y = train_val_data["X"], train_val_data["y"]
    X_test, y_test = test_data["X"], test_data["y"]
    COLUMNS = ["ax", "ay", "az", "gx", "gy", "gz", "hall1", "hall2"]
    with open(model_dir / 'zscore_stats.json') as json_file:
        stats = json.load(json_file)
    mean = np.array([stats[c]["mean"] for c in COLUMNS])
    std  = np.array([stats[c]["std_dev"] for c in COLUMNS])
    X = (X - mean) / std
    X = np.transpose(X, axes=(0, 2, 1))
    X = torch.from_numpy(X).float().unsqueeze(2)
    y = torch.from_numpy(y).long()

    X_test, y_test = test_data['X'], test_data['y']
    
    X_test = (X_test - mean) / std
    X_test = np.transpose(X_test, axes = (0,2,1))
    X_test = torch.from_numpy(X_test).float().unsqueeze(2)
    y_test = torch.from_numpy(y_test).long()
    return X, y, X_test, y_test

def quantization():
    args, _ = parser.parse_known_args()
    data_dir = Path("preprocessed_data") / args.glove
    model_dir = Path("run") / args.glove
    quant_mode = args.quant_mode
    deploy = args.deploy
    inspect = args.inspect

    X_calib, y_calib, X_test, y_test = load_data(data_dir, model_dir)

    with open(model_dir/'best_params.json', "r") as file:
        best_params = json.load(file)
    
    with open(model_dir / 'label_map.json') as f:
        label_map = json.load(f)
    class_names = [n for n, i in sorted(label_map.items(), key=lambda kv: kv[1])]
    num_classes = len(class_names)

    model = build_model(best_params["channels"], best_params["kernels"], num_classes)
    model.load_state_dict(torch.load(model_dir/'model.pt'))

    batch_size = 5
    if deploy:
        if batch_size != 1:
            print("Warning: exporting xmodel needs batch_size=1, forcing it.")
        batch_size = 1
    dummy_input = torch.randn(batch_size, 8, 1, 50)

    if quant_mode == 'float':
        quant_model = model
        
        if inspect:
            import sys
            from pytorch_nndct.apis import Inspector

            # create inspector
            inspector = Inspector("0x101000016010404")
            # start to inspect
            inspector.inspect(quant_model, (dummy_input,), device=device)
            sys.exit()
    else:
        ## new api
        ####################################################################################
        out_dir = str(model_dir / "quantize_result")

        quantizer = torch_quantizer(
            quant_mode, model, (dummy_input), device=device, quant_config_file=None, output_dir=out_dir)

        quant_model = quantizer.quant_model
        #####################################################################################

    if deploy:
        X, y = X_test[:1], y_test[:1]
    elif quant_mode == 'test':
        X, y = X_test, y_test
    else:
        X, y = X_calib, y_calib

    evaluate(X,y, quant_model, class_names)

    # handle quantization result
    if quant_mode == 'calib':
        quantizer.export_quant_config()
    if deploy:
        quantizer.export_xmodel(output_dir=out_dir, deploy_check=False)
        quantizer.export_onnx_model(output_dir=out_dir)     

def evaluate(X, y, quant_model, class_names):
    with torch.no_grad():
        logits = quant_model(X)
        pred = logits.argmax(1)
    
    idx = list(range(len(class_names)))
    print("idx: ", idx)

    print(classification_report(y, pred, zero_division=0))
    print("confusion matrix (rows=true, cols=pred):")
    cm = confusion_matrix(y, pred, labels=idx)
    width = max(len(n) for n in class_names)
    print(" " * width + " " + "".join(f"{i:>4}" for i in idx))      # column header: class numbers
    for i, row in enumerate(cm):
        print(f"{class_names[i]:>{width}} " + "".join(f"{v:>4}" for v in row) + f"   ({i})")


if __name__ == '__main__':
    
    print("-------- Start test ")

    quantization()

    print("-------- End of test ")