from torch import nn

def build_model(channels, kernels, num_classes):
    layers = []
    in_channels = 8
    for out_channels, kernel_size in zip(channels, kernels):
        padding = kernel_size // 2   # "same" padding: output keeps the input's time length
        conv = nn.Conv2d(in_channels, out_channels, kernel_size=(1, kernel_size), padding=(0, padding))
        layers.append(conv)
        layers.append(nn.ReLU())
        layers.append(nn.MaxPool2d((1, 2)))
        in_channels = out_channels
    layers.append(nn.AdaptiveAvgPool2d((1,1)))
    layers.append(nn.Flatten())
    layers.append(nn.Linear(in_channels, num_classes))
    return nn.Sequential(*layers)