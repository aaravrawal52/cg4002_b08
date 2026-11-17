# GENETARED BY NNDCT, DO NOT EDIT!

import torch
from torch import tensor
import pytorch_nndct as py_nndct

class Sequential(py_nndct.nn.NndctQuantModel):
    def __init__(self):
        super(Sequential, self).__init__()
        self.module_0 = py_nndct.nn.Input() #Sequential::input_0(Sequential::nndct_input_0)
        self.module_1 = py_nndct.nn.Conv2d(in_channels=8, out_channels=61, kernel_size=[1, 5], stride=[1, 1], padding=[0, 2], dilation=[1, 1], groups=1, bias=True) #Sequential::Sequential/Conv2d[0]/ret.3(Sequential::nndct_conv2d_1)
        self.module_2 = py_nndct.nn.ReLU(inplace=False) #Sequential::Sequential/ReLU[1]/ret.5(Sequential::nndct_relu_2)
        self.module_3 = py_nndct.nn.MaxPool2d(kernel_size=[1, 2], stride=[1, 2], padding=[0, 0], dilation=[1, 1], ceil_mode=False) #Sequential::Sequential/MaxPool2d[2]/246(Sequential::nndct_maxpool_3)
        self.module_4 = py_nndct.nn.AdaptiveAvgPool2d(output_size=[1, 1]) #Sequential::Sequential/AdaptiveAvgPool2d[3]/264(Sequential::nndct_adaptive_avg_pool2d_4)
        self.module_5 = py_nndct.nn.Module('nndct_flatten') #Sequential::Sequential/Flatten[4]/ret.7(Sequential::nndct_flatten_5)
        self.module_6 = py_nndct.nn.Linear(in_features=61, out_features=12, bias=True) #Sequential::Sequential/Linear[5]/ret(Sequential::nndct_dense_6)

    @py_nndct.nn.forward_processor
    def forward(self, *args):
        output_module_0 = self.module_0(input=args[0])
        output_module_0 = self.module_1(output_module_0)
        output_module_0 = self.module_2(output_module_0)
        output_module_0 = self.module_3(output_module_0)
        output_module_0 = self.module_4(output_module_0)
        output_module_0 = self.module_5(input=output_module_0, start_dim=1, end_dim=-1)
        output_module_0 = self.module_6(output_module_0)
        return output_module_0
