import json
import shutil
from datetime import datetime
from pathlib import Path

import numpy as np
import torch
from torch import nn, optim
from torch.utils.data import DataLoader

from model import build_model
from config import LABEL_MAP_PATH, PREPROCESSED_DIR, BEST_PARAMS_PATH, MODELS_DIR

def define_model(stats):
    channels = stats["channels"]
    kernels = stats["kernels"]
    with open(LABEL_MAP_PATH) as f:
        num_classes = len(json.load(f))
    return build_model(channels, kernels, num_classes)

def train_model(X, y):
    """
    Uses the optimized hyperparameters to train the model
    """
    with open(BEST_PARAMS_PATH) as json_file:
        stats = json.load(json_file)

    device = torch.accelerator.current_accelerator().type if torch.accelerator.is_available() else "cpu"
    print(f"Using {device} device")
    model = define_model(stats).to(device)

    dataset = torch.utils.data.TensorDataset(X, y)
    dataloader = DataLoader(dataset, batch_size=stats["batch_size"], shuffle=True)

    num_epochs= stats["best_epoch"]
    lr = stats["lr"]
    optimizer = optim.Adam(model.parameters(), lr= lr)
    loss_criterion = nn.CrossEntropyLoss()
    best_loss = 1000
    best_epoch = 1

    for epoch in range(num_epochs):
        running_loss = 0
        print(f"Epoch [{epoch + 1}/{num_epochs}]")
        
        model.train()
        for batch_input, batch_label in dataloader:
            batch_input = batch_input.to(device)
            batch_label = batch_label.to(device)
            logits = model(batch_input)
            pred_probab = nn.Softmax(dim=1)(logits)
            y_pred = pred_probab.argmax(1)
            loss = loss_criterion(logits, batch_label)
            running_loss += loss.item()
            optimizer.zero_grad()
            loss.backward()
            optimizer.step()

        avg_loss = running_loss / len(dataloader)
    
    print(avg_loss)
    return model.state_dict(), stats

if __name__ == "__main__":
    train_val_data = np.load(PREPROCESSED_DIR / 'train_val_data.npz')
    X, y = train_val_data['X'], train_val_data['y']
    mean = X.mean(axis=(0, 1))          # one mean per channel, from training people only
    std  = X.std(axis=(0, 1))
    X = (X - mean) / std
    COLUMNS = ["ax", "ay", "az", "gx", "gy", "gz", "hall1", "hall2"]
    stats = {c: {"mean": float(m), "std_dev": float(s)}
            for c, m, s in zip(COLUMNS, mean, std)}
    run_dir = MODELS_DIR / datetime.now().strftime("%Y-%m-%d %H%M%S")
    run_dir.mkdir(parents=True, exist_ok=True)
    with open(run_dir / "zscore_stats.json", "w") as f:
        json.dump(stats, f)
    X = np.transpose(X, axes = (0,2,1))
    X = torch.from_numpy(X).float().unsqueeze(2)
    y = torch.from_numpy(y).long()

    

    best_state, stats = train_model(X,y)
    
    torch.save(best_state, run_dir / "model.pt")
    with open(run_dir / "best_params.json", "w") as f:
        json.dump(stats, f)
    # test/quantize/inspect read the class count from here, so it always matches these weights
    shutil.copy(LABEL_MAP_PATH, run_dir / "label_map.json")
