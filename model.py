from torch import nn

def build_model(channels, kernels):
    layers = []
    in_channels = 8
    for out_channels, kernel_size in zip(channels, kernels):
        layers.append(nn.Conv2d(in_channels, out_channels, kernel_size=(1, kernel_size), padding=(0, kernel_size // 2)))
        layers.append(nn.ReLU())
        layers.append(nn.MaxPool2d((1, 2)))
        in_channels = out_channels
    layers.append(nn.AdaptiveAvgPool2d((1,1)))
    layers.append(nn.Flatten())
    layers.append(nn.Linear(in_channels, 8))
    return nn.Sequential(*layers)