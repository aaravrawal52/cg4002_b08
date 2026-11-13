import json

import numpy as np
import optuna
import torch
from optuna.trial import TrialState
from torch import nn, optim
from torch.utils.data import DataLoader
from sklearn.model_selection import GroupKFold

from model import build_model
from train import augment
import matplotlib.pyplot as plt
from config import GLOVE, LABEL_MAP_PATH, PREPROCESSED_DIR, BEST_PARAMS_PATH

def to_tensor(X):
    return torch.from_numpy(np.transpose(X, (0, 2, 1))).float().unsqueeze(2)

def define_model(trial):
    n_conv_layers = trial.suggest_int("n_conv_layers", 1, 3)
    channels = [trial.suggest_int("out_channels_l{}".format(i), 8, 64) for i in range(n_conv_layers)]
    kernels = [trial.suggest_categorical(f"kernel_size_l{i}", [3, 5, 7]) for i in range(n_conv_layers)]
    with open(LABEL_MAP_PATH) as f:
        num_classes = len(json.load(f))
    return build_model(channels, kernels, num_classes)

def objective(trial, X, y, group):
    """
    Run Optuna through the different combinations of hyperparameters and compare train and val loss

    returns:
    - best_vloss: for Optuna to compare across diff trials and rank the trials
    """
    device = torch.accelerator.current_accelerator().type if torch.accelerator.is_available() else "cpu"
    print(f"Using {device} device")
    batch_size = trial.suggest_categorical("batch_size", [16, 32, 64])

    num_epochs=200
    lr = trial.suggest_float("lr", 1e-4, 1e-2, log=True)
    loss_criterion = nn.CrossEntropyLoss()
    PATIENCE = 10

    fold_best_vlosses = []
    fold_best_epoch = []
    fold_train_curves = []
    fold_val_curves = []
    gkf = GroupKFold(n_splits=2)
    COLUMNS = ["ax", "ay", "az", "gx", "gy", "gz", "hall1", "hall2"]

    for fold, (train_index, val_index) in enumerate(gkf.split(X, y, group)):
        print(fold, np.unique(group[val_index]))
        X_train, X_val = X[train_index], X[val_index]
        # zscore_normalization
        mean = X_train.mean(axis=(0, 1))          # one mean per channel, from training people only
        std  = X_train.std(axis=(0, 1))
        # X_train stays in raw units: augmented then normalised per batch below
        X_val = (X_val - mean) / std
        mean_t = torch.tensor(mean, dtype=torch.float32, device=device).view(1, 8, 1, 1)
        std_t  = torch.tensor(std,  dtype=torch.float32, device=device).view(1, 8, 1, 1)

        train_dataset = torch.utils.data.TensorDataset(to_tensor(X_train), torch.from_numpy(y[train_index]).long())
        train_dataloader = DataLoader(train_dataset, batch_size=batch_size, shuffle=True)
        val_dataset = torch.utils.data.TensorDataset(to_tensor(X_val), torch.from_numpy(y[val_index]).long())
        val_dataloader = DataLoader(val_dataset, batch_size=batch_size, shuffle=True)

        model = define_model(trial).to(device)
        optimizer = optim.Adam(model.parameters(), lr= lr)
        best_vloss = 1000
        best_epoch = 1
        epochs_no_improve = 0
        train_curve, val_curve = [], []

        for epoch in range(num_epochs):
            running_loss = 0
            running_vloss = 0
            print(f"Epoch [{epoch + 1}/{num_epochs}]")

            model.train()
            for batch_input, batch_label in train_dataloader:
                batch_input = batch_input.to(device)
                batch_input = augment(batch_input)               # raw units
                batch_input = (batch_input - mean_t) / std_t     # then z-score
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
            # print ("training loss: ", avg_training_loss)
            # print ("validation loss: ", avg_vloss)
            train_curve.append(avg_training_loss)
            val_curve.append(avg_vloss)

            if avg_vloss <= best_vloss:
                best_vloss = avg_vloss
                best_epoch = epoch + 1
                epochs_no_improve = 0
            else:
                epochs_no_improve += 1
                if epochs_no_improve >= PATIENCE:
                    print("early stopping")
                    break
        fold_train_curves.append(train_curve)
        fold_val_curves.append(val_curve)
        fold_best_vlosses.append(best_vloss) # append avg train loss for the fold
        fold_best_epoch.append(best_epoch)
        mean_so_far = np.mean(fold_best_vlosses)
        trial.report(mean_so_far, fold)
        if trial.should_prune():
            raise optuna.exceptions.TrialPruned()

    trial.set_user_attr("train_curves", fold_train_curves)
    trial.set_user_attr("val_curves", fold_val_curves)
    trial.set_user_attr("val_losses", np.mean(fold_best_vlosses))
    trial.set_user_attr("best_epoch", int(round(np.median(fold_best_epoch))))
    return np.mean(fold_best_vlosses)

if __name__ == "__main__":
    training_val_data = np.load(PREPROCESSED_DIR / 'train_val_data.npz')
    X, y, group = training_val_data['X'], training_val_data['y'], training_val_data['group']

    study = optuna.create_study(
        direction="minimize",
        pruner = optuna.pruners.MedianPruner(n_startup_trials=5, n_warmup_steps=1),
        storage="sqlite:///optuna.db",
        study_name=f"gesture_{GLOVE}",
        load_if_exists=True,
    )
    study.optimize(lambda trial: objective(trial, X, y, group), n_trials=100)

    pruned_trials = study.get_trials(deepcopy=False, states=[TrialState.PRUNED])
    complete_trials = study.get_trials(deepcopy=False, states=[TrialState.COMPLETE])

    print("Study statistics: ")
    print("  Number of finished trials: ", len(study.trials))
    print("  Number of pruned trials: ", len(pruned_trials))
    print("  Number of complete trials: ", len(complete_trials))

    print("Best trial:")
    trial = study.best_trial

    train_curves = trial.user_attrs["train_curves"]
    val_curves = trial.user_attrs["val_curves"]
    best_ep = trial.user_attrs["best_epoch"]

    # plot train and validation loss curve: one colour per fold (solid = val, dashed = train)
    plt.figure(figsize=(9, 5))
    for fold, (tc, vc) in enumerate(zip(train_curves, val_curves)):
        color = f"C{fold}"
        epochs = range(1, len(tc) + 1)
        plt.plot(epochs, tc, color=color, linestyle="--", alpha=0.6)
        plt.plot(epochs, vc, color=color, label=f"fold {fold} val")
    plt.plot([], [], color="gray", linestyle="--", label="train (dashed)")
    plt.axvline(best_ep, color="black", linestyle=":", label=f"median best epoch ({best_ep})")
    plt.xlabel("epoch")
    plt.ylabel("cross-entropy loss")
    plt.title(f"Best trial #{trial.number}")
    plt.legend(fontsize=8)
    plt.grid(alpha=0.3)
    plt.tight_layout()
    plt.savefig(PREPROCESSED_DIR / "loss_curve.png", dpi=150)

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
        "batch_size": p["batch_size"]
    }
    with open(BEST_PARAMS_PATH, "w") as f:
        json.dump(config, f)
