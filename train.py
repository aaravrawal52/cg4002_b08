import numpy as np
import torch
from torch.utils.data import DataLoader
from torch import nn
from torch import optim

class NeuralNetwork(nn.Module):
    def __init__(self):
        super().__init__()
        self.flatten = nn.Flatten()
        self.linear_relu_stack = nn.Sequential(
            nn.Conv1d(8, 16, kernel_size = 5, stride = 1, padding = 'same'), # change kernel size when use windowsize 50
            nn.ReLU(),
            nn.MaxPool1d(2),
            nn.Conv1d(16, 32, kernel_size = 5, stride = 1, padding = 'same'),
            nn.ReLU(),
            nn.MaxPool1d(2),
            nn.AdaptiveAvgPool1d(1),
            nn.Flatten(),
            nn.Linear(32, 8) # window size change from 4 in test to 50, so shape of x_train change from 8x4 to 8x50, param change from 8*4 to 8*50
        )

    def forward(self, x):
        logits = self.linear_relu_stack(x)
        return logits

if __name__ == "__main__":
    training_data = np.load('preprocessed_data/training_data.npz')
    val_data = np.load('preprocessed_data/val_data.npz')
    test_data = np.load('preprocessed_data/test_data.npz')
    X_train, y_train = training_data['X'], training_data['y']
    X_train = np.transpose(X_train, axes = (0,2,1))
    X_train = torch.from_numpy(X_train).float()
    y_train = torch.from_numpy(y_train).long()

    device = torch.accelerator.current_accelerator().type if torch.accelerator.is_available() else "cpu"
    print(f"Using {device} device")
    model = NeuralNetwork().to(device)

    train_dataset = torch.utils.data.TensorDataset(X_train, y_train)
    train_dataloader = DataLoader(train_dataset, batch_size=5, shuffle=True)

    # val_dataset = torch.utils.data.TensorDataset(X_val, y_val)
    # val_dataloader = DataLoader(val_dataset, batch_size=5, shuffle=True)

    num_epochs=10
    optimizer = optim.Adam(model.parameters(), lr=0.001)
    loss_criterion = nn.CrossEntropyLoss()

    for epoch in range(num_epochs):
        running_loss = 0
        print(f"Epoch [{epoch + 1}/{num_epochs}]")
        
        
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
        avg_training_loss = running_loss / len(train_dataloader)
        print ("Epoch ", epoch, "loss: ", avg_training_loss)
