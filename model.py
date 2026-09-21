import torch
from torch import nn

def build_model(channels, kernels):
    layers = []
    in_channels = 8
    for out_channels, kernel_size in zip(channels, kernels):
        layers.append(nn.Conv1d(in_channels, out_channels, kernel_size, padding="same"))
        layers.append(nn.ReLU())
        layers.append(nn.MaxPool1d(2))
        in_channels = out_channels
    layers.append(nn.AdaptiveAvgPool1d(1))
    layers.append(nn.Flatten())
    layers.append(nn.Linear(in_channels, 8))
    return nn.Sequential(*layers)