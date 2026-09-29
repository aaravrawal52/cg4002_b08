import sys
import argparse
from pytorch_nndct.apis import torch_quantizer
import torch
from pathlib import Path
from sklearn.metrics import classification_report, confusion_matrix
from model import build_model
import numpy as np
import json

device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

parser = argparse.ArgumentParser()

parser.add_argument(
    '--data_dir',
    default="preprocessed_data/",
    help='Data set directory, when quant_mode=calib, it is for calibration, while quant_mode=test it is for evaluation')
parser.add_argument(
    '--model_dir',
    default="run/",
    help='Trained model file path. Download pretrained model from the following url and put it in model_dir specified path: https://download.pytorch.org/models/resnet18-5c106cde.pth'
)
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


def load_data(data_dir):
    training_data = np.load(data_dir/'training_data.npz')
    val_data = np.load(data_dir/'val_data.npz')
    test_data = np.load(data_dir/'test_data.npz')
    X_train, y_train = training_data['X'], training_data['y']
    X_train = np.transpose(X_train, axes = (0,2,1))
    X_train = torch.from_numpy(X_train).float().unsqueeze(2)
    y_train = torch.from_numpy(y_train).long()

    X_val, y_val = val_data['X'], val_data['y']
    X_val = np.transpose(X_val, axes = (0,2,1))
    X_val = torch.from_numpy(X_val).float().unsqueeze(2)
    y_val = torch.from_numpy(y_val).long()

    X = torch.cat((X_train, X_val), 0)
    y = torch.cat((y_train, y_val), 0)

    X_test, y_test = test_data['X'], test_data['y']
    X_test = np.transpose(X_test, axes = (0,2,1))
    X_test = torch.from_numpy(X_test).float().unsqueeze(2)
    y_test = torch.from_numpy(y_test).long()
    return X, y, X_test, y_test

def quantization():
    args, _ = parser.parse_known_args()
    data_dir = Path(args.data_dir)
    model_dir = Path(args.model_dir)
    quant_mode = args.quant_mode
    deploy = args.deploy
    inspect = args.inspect

    X_calib, y_calib, X_test, y_test = load_data(data_dir)

    with open(model_dir/'best_params.json', "r") as file:
        best_params = json.load(file)
    
    model = build_model(best_params["channels"], best_params["kernels"])
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
            # inspector = Inspector("0x603000b16013831") # by fingerprint
            inspector = Inspector("0x101000016010404")  # by name
            # start to inspect
            inspector.inspect(quant_model, (dummy_input,), device=device)
            sys.exit()
    else:
        ## new api
        ####################################################################################
        quantizer = torch_quantizer(
            quant_mode, model, (dummy_input), device=device, quant_config_file=None)

        quant_model = quantizer.quant_model
        #####################################################################################

    # to get loss value after evaluation
    loss_fn = torch.nn.CrossEntropyLoss().to(device)

    if deploy:
        X, y = X_test[:1], y_test[:1]
    elif quant_mode == 'test':
        X, y = X_test, y_test
    else:
        X, y = X_calib, y_calib

    evaluate(X,y, quant_model)

    # handle quantization result
    if quant_mode == 'calib':
        quantizer.export_quant_config()
    if deploy:
        quantizer.export_xmodel(deploy_check=False)
        quantizer.export_onnx_model()     

def evaluate(X, y, quant_model):
    with torch.no_grad():
        logits = quant_model(X)
        pred = logits.argmax(1)

    print(classification_report(y, pred, zero_division=0))
    print("confusion matrix (rows=true, cols=pred):")
    print(confusion_matrix(y, pred))


if __name__ == '__main__':
    
    print("-------- Start test ")

    quantization()

    print("-------- End of test ")