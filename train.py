import numpy as np
import torch
from torch.utils.data import DataLoader
from torch import nn
from torch import optim
from datetime import datetime
import optuna
from optuna.trial import TrialState

# use optimized params from optuna after hyperparam tuning for the final model
# class NeuralNetwork(nn.Module):
#     def __init__(self):
#         super().__init__()
#         self.flatten = nn.Flatten()
#         self.linear_relu_stack = nn.Sequential(
#             nn.Conv1d(8, 16, kernel_size = 5, stride = 1, padding = 'same'),
#             nn.ReLU(),
#             nn.MaxPool1d(2),
#             nn.Conv1d(16, 32, kernel_size = 5, stride = 1, padding = 'same'),
#             nn.ReLU(),
#             nn.MaxPool1d(2),
#             nn.AdaptiveAvgPool1d(1),
#             nn.Flatten(),
#             nn.Linear(32, 8)
#         )

#     def forward(self, x):
#         logits = self.linear_relu_stack(x)
#         return logits

def define_model(trial):
    n_conv_layers = trial.suggest_int("n_conv_layers", 1, 3)
    layers = []
    in_channels = 8

    for i in range(n_conv_layers):
        out_channels = trial.suggest_int("out_channels_l{}".format(i), 8, 64)
        kernel_size = trial.suggest_categorical(f"kernel_size_l{i}", [3, 5, 7])
        layers.append(nn.Conv1d(in_channels, out_channels, kernel_size, padding="same"))
        layers.append(nn.ReLU())
        layers.append(nn.MaxPool1d(2))
        in_channels = out_channels
    
    layers.append(nn.AdaptiveAvgPool1d(1))
    layers.append(nn.Flatten())
    layers.append(nn.Linear(in_channels, 8))

    return nn.Sequential(*layers)

def objective(trial, X_train, y_train, X_val, y_val):
    device = torch.accelerator.current_accelerator().type if torch.accelerator.is_available() else "cpu"
    print(f"Using {device} device")
    model = define_model(trial).to(device)

    train_dataset = torch.utils.data.TensorDataset(X_train, y_train)
    train_dataloader = DataLoader(train_dataset, batch_size=5, shuffle=True)

    val_dataset = torch.utils.data.TensorDataset(X_val, y_val)
    val_dataloader = DataLoader(val_dataset, batch_size=5, shuffle=True)

    num_epochs=10
    lr = trial.suggest_float("lr", 1e-5, 1e-1, log=True)
    optimizer = optim.Adam(model.parameters(), lr= lr)
    loss_criterion = nn.CrossEntropyLoss()
    best_vloss = 1000
    epoch_number = 1
    best_epoch = 1
    timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
    best_timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')

    for epoch in range(num_epochs):
        running_loss = 0
        running_vloss = 0
        print(f"Epoch [{epoch + 1}/{num_epochs}]")
        
        model.train()
        for batch_input, batch_label in train_dataloader:
            
            logits = model(batch_input)
            pred_probab = nn.Softmax(dim=1)(logits)
            y_pred = pred_probab.argmax(1)
            # print(f"Predicted class: {y_pred}")
            loss = loss_criterion(logits, batch_label)
            running_loss += loss.item()
            # print("loss: ", loss)
            optimizer.zero_grad()
            loss.backward()
            optimizer.step()
        
        model.eval()
        with torch.no_grad():
            for batch_vinput, batch_vlabel in val_dataloader:
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

        # Track best performance, and save the model's state
        if avg_vloss < best_vloss:
            best_vloss = avg_vloss
            best_epoch = epoch_number
            best_timestamp_ = timestamp
        epoch_number += 1
    return avg_vloss

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

    study = optuna.create_study(direction="minimize")
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

    # model_path = f'models/model_{best_timestamp}_{best_epoch}'
    # torch.save(model.state_dict(), model_path)
