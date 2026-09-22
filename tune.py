import json

import numpy as np
import optuna
import torch
from optuna.trial import TrialState
from torch import nn, optim
from torch.utils.data import DataLoader

from model import build_model

def define_model(trial):
    n_conv_layers = trial.suggest_int("n_conv_layers", 1, 3)
    channels = [trial.suggest_int("out_channels_l{}".format(i), 8, 64) for i in range(n_conv_layers)]
    kernels = [trial.suggest_categorical(f"kernel_size_l{i}", [3, 5, 7]) for i in range(n_conv_layers)]
    return build_model(channels, kernels)

def objective(trial, X_train, y_train, X_val, y_val):
    """
    Run Optuna through the different combinations of hyperparameters and compare train and val loss

    returns:
    - best_vloss: for Optuna to compare across diff trials and rank the trials
    """
    device = torch.accelerator.current_accelerator().type if torch.accelerator.is_available() else "cpu"
    print(f"Using {device} device")
    model = define_model(trial).to(device)

    train_dataset = torch.utils.data.TensorDataset(X_train, y_train)
    train_dataloader = DataLoader(train_dataset, batch_size=5, shuffle=True)

    val_dataset = torch.utils.data.TensorDataset(X_val, y_val)
    val_dataloader = DataLoader(val_dataset, batch_size=5, shuffle=True)

    num_epochs=50
    lr = trial.suggest_float("lr", 1e-5, 1e-1, log=True)
    optimizer = optim.Adam(model.parameters(), lr= lr)
    loss_criterion = nn.CrossEntropyLoss()
    best_vloss = 1000
    best_epoch = 1
    epochs_no_improve = 0
    PATIENCE = 10

    for epoch in range(num_epochs):
        running_loss = 0
        running_vloss = 0
        print(f"Epoch [{epoch + 1}/{num_epochs}]")
        
        model.train()
        for batch_input, batch_label in train_dataloader:
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
        
        model.eval()
        with torch.no_grad():
            for batch_vinput, batch_vlabel in val_dataloader:
                batch_vinput = batch_vinput.to(device)
                batch_vlabel = batch_vlabel.to(device)
                voutputs = model(batch_vinput)
                vloss = loss_criterion(voutputs, batch_vlabel)
                running_vloss += vloss.item()
        avg_training_loss = running_loss / len(train_dataloader)
        avg_vloss = running_vloss / len(val_dataloader)
        print ("training loss: ", avg_training_loss)
        print ("validation loss: ", avg_vloss)

        trial.report(avg_vloss, epoch)
        if trial.should_prune():
            raise optuna.exceptions.TrialPruned()

        if avg_vloss <= best_vloss:
            best_vloss = avg_vloss
            best_epoch = epoch + 1
            epochs_no_improve = 0
        else:
            epochs_no_improve += 1
            if epochs_no_improve >= PATIENCE:
                print("early stopping")
                break
    
    trial.set_user_attr("best_epoch", best_epoch)
    return best_vloss

if __name__ == "__main__":
    training_data = np.load('preprocessed_data/training_data.npz')
    val_data = np.load('preprocessed_data/val_data.npz')
    test_data = np.load('preprocessed_data/test_data.npz')
    X_train, y_train = training_data['X'], training_data['y']
    X_train = np.transpose(X_train, axes = (0,2,1))
    X_train = torch.from_numpy(X_train).float()
    y_train = torch.from_numpy(y_train).long()

    X_val, y_val = val_data['X'], val_data['y']
    X_val = np.transpose(X_val, axes = (0,2,1))
    X_val = torch.from_numpy(X_val).float()
    y_val = torch.from_numpy(y_val).long()

    study = optuna.create_study(
        direction="minimize", 
        pruner = optuna.pruners.MedianPruner(n_startup_trials=5, n_warmup_steps=5),
        storage="sqlite:///optuna.db",
        study_name="gesture",
        load_if_exists=True,
    )
    study.optimize(lambda trial: objective(trial, X_train, y_train, X_val, y_val), n_trials=100, timeout=600)

    pruned_trials = study.get_trials(deepcopy=False, states=[TrialState.PRUNED])
    complete_trials = study.get_trials(deepcopy=False, states=[TrialState.COMPLETE])
    
    print("Study statistics: ")
    print("  Number of finished trials: ", len(study.trials))
    print("  Number of pruned trials: ", len(pruned_trials))
    print("  Number of complete trials: ", len(complete_trials))

    print("Best trial:")
    trial = study.best_trial

    print("  Value: ", trial.value)

    print("  Params: ")
    for key, value in trial.params.items():
        print("    {}: {}".format(key, value))
    
    p = trial.params
    n = p["n_conv_layers"]
    config = {
        "channels": [p[f"out_channels_l{i}"] for i in range(n)],
        "kernels":  [p[f"kernel_size_l{i}"] for i in range(n)],
        "lr": p["lr"],
        "best_epoch": trial.user_attrs["best_epoch"],
    }
    with open("best_params.json", "w") as f:
        json.dump(config, f)
