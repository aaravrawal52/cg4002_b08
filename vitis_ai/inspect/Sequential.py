# GENETARED BY NNDCT, DO NOT EDIT!

import torch
import pytorch_nndct as py_nndct
class Sequential(torch.nn.Module):
    def __init__(self):
        super(Sequential, self).__init__()
        self.module_0 = py_nndct.nn.Input() #Sequential::input_0
        self.module_1 = py_nndct.nn.Conv2d(in_channels=8, out_channels=22, kernel_size=[1, 7], stride=[1, 1], padding=[0, 3], dilation=[1, 1], groups=1, bias=True) #Sequential::Sequential/Conv2d[0]/input.3
        self.module_2 = py_nndct.nn.ReLU(inplace=False) #Sequential::Sequential/ReLU[1]/303
        self.module_3 = py_nndct.nn.MaxPool2d(kernel_size=[1, 2], stride=[1, 2], padding=[0, 0], dilation=[1, 1], ceil_mode=False) #Sequential::Sequential/MaxPool2d[2]/input.5
        self.module_4 = py_nndct.nn.Conv2d(in_channels=22, out_channels=50, kernel_size=[1, 3], stride=[1, 1], padding=[0, 1], dilation=[1, 1], groups=1, bias=True) #Sequential::Sequential/Conv2d[3]/input.7
        self.module_5 = py_nndct.nn.ReLU(inplace=False) #Sequential::Sequential/ReLU[4]/337
        self.module_6 = py_nndct.nn.MaxPool2d(kernel_size=[1, 2], stride=[1, 2], padding=[0, 0], dilation=[1, 1], ceil_mode=False) #Sequential::Sequential/MaxPool2d[5]/input.9
        self.module_7 = py_nndct.nn.Conv2d(in_channels=50, out_channels=43, kernel_size=[1, 7], stride=[1, 1], padding=[0, 3], dilation=[1, 1], groups=1, bias=True) #Sequential::Sequential/Conv2d[6]/input.11
        self.module_8 = py_nndct.nn.ReLU(inplace=False) #Sequential::Sequential/ReLU[7]/371
        self.module_9 = py_nndct.nn.MaxPool2d(kernel_size=[1, 2], stride=[1, 2], padding=[0, 0], dilation=[1, 1], ceil_mode=False) #Sequential::Sequential/MaxPool2d[8]/input.13
        self.module_10 = py_nndct.nn.AdaptiveAvgPool2d(output_size=1) #Sequential::Sequential/AdaptiveAvgPool2d[9]/401
        self.module_11 = py_nndct.nn.Module('flatten') #Sequential::Sequential/Flatten[10]/input
        self.module_12 = py_nndct.nn.Linear(in_features=43, out_features=8, bias=True) #Sequential::Sequential/Linear[11]/405

    def forward(self, *args):
        output_module_0 = self.module_0(input=args[0])
        output_module_0 = self.module_1(output_module_0)
        output_module_0 = self.module_2(output_module_0)
        output_module_0 = self.module_3(output_module_0)
        output_module_0 = self.module_4(output_module_0)
        output_module_0 = self.module_5(output_module_0)
        output_module_0 = self.module_6(output_module_0)
        output_module_0 = self.module_7(output_module_0)
        output_module_0 = self.module_8(output_module_0)
        output_module_0 = self.module_9(output_module_0)
        output_module_0 = self.module_10(output_module_0)
        output_module_0 = self.module_11(input=output_module_0, start_dim=1, end_dim=3)
        output_module_0 = self.module_12(output_module_0)
        return output_module_0
