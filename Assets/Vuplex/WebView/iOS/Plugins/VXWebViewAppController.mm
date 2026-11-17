// Copyright (c) 2026 Vuplex Inc. All rights reserved.
//
// Licensed under the Vuplex Commercial Software Library License, you may
// not use this file except in compliance with the License. You may obtain
// a copy of the License at
//
//     https://vuplex.com/commercial-library-license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#import "VXWebViewAppController.h"
#import "IUnityInterface.h"

static void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API VuplexUnityPluginLoad(IUnityInterfaces *unityInterfaces) {

    Class textureManager = NSClassFromString(@"VXMetalTextureManager");
    [textureManager performSelector:@selector(setUnityInterfacesValue:) withObject:[NSValue valueWithPointer:unityInterfaces]];
}

static void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API VuplexUnityPluginUnload() {}

@implementation VXWebViewAppController

// Overrides [UnityAppController shouldAttachRenderDelegate] to hook into the Unity trampoline.
- (void)shouldAttachRenderDelegate {

    // These function names are prefixed with "Vuplex" to prevent them
    // from clashing with symbols from other 3rd party plugins.
    UnityRegisterRenderingPluginV5(&VuplexUnityPluginLoad, &VuplexUnityPluginUnload);
    // Call the base class's `shouldAttachRenderDelegate` implementation in case
    // VXWebViewAppController is modified to subclass another plugin's app controller.
    [super shouldAttachRenderDelegate];
}

@end

IMPL_APP_CONTROLLER_SUBCLASS(VXWebViewAppController);
