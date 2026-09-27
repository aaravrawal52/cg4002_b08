import json
import shutil
from datetime import datetime
from pathlib import Path

import numpy as np
import torch
from torch import nn, optim
from torch.utils.data import DataLoader

from model import build_model

def define_model(stats):
    channels = stats["channels"]
    kernels = stats["kernels"]
    return build_model(channels, kernels)

def train_model(X, y):
    """
    Uses the optimized hyperparameters to train the model
    """
    with open('best_params.json') as json_file:
        stats = json.load(json_file)

    device = torch.accelerator.current_accelerator().type if torch.accelerator.is_available() else "cpu"
    print(f"Using {device} device")
    model = define_model(stats).to(device)

    dataset = torch.utils.data.TensorDataset(X, y)
    dataloader = DataLoader(dataset, batch_size=5, shuffle=True)

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
    training_data = np.load('preprocessed_data/training_data.npz')
    val_data = np.load('preprocessed_data/val_data.npz')
    test_data = np.load('preprocessed_data/test_data.npz')
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

    best_state, stats = train_model(X,y)
    
    run_dir = Path("models") / datetime.now().strftime("%Y-%m-%d %H%M%S")
    run_dir.mkdir(parents=True, exist_ok=True)
    torch.save(best_state, run_dir / "model.pt")
    with open(run_dir / "best_params.json", "w") as f:
        json.dump(stats, f)
    shutil.copy("zscore_stats.json", run_dir / "zscore_stats.json")
