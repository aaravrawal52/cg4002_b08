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
#if UNITY_IOS && !UNITY_EDITOR
using System;

namespace Vuplex.WebView {

    /// <summary>
    /// Event args for <see cref="iOSWebView.AuthChallengeReceived">iOSWebView.AuthChallengeReceived</see>.
    /// </summary>
    public class iOSAuthChallengeReceivedEventArgs : EventArgs {

        /// <summary>
        /// Objective-C pointer to the native `(NSURLAuthenticationChallenge *) challenge` parameter passed to
        /// <see href="https://developer.apple.com/documentation/webkit/wknavigationdelegate/webview(_:didreceive:completionhandler:)?language=objc">webView:didReceiveAuthenticationChallenge:completionHandler:</see>.
        /// </summary>
        /// <seealso href="https://developer.apple.com/documentation/foundation/urlauthenticationchallenge?language=objc">NSURLAuthenticationChallenge</seealso>
        public IntPtr AuthChallenge;

        /// <summary>
        /// Objective-C pointer to the native `(void (^)(enum NSURLSessionAuthChallengeDisposition, NSURLCredential *)) completionHandler`
        /// parameter passed to <see href="https://developer.apple.com/documentation/webkit/wknavigationdelegate/webview(_:didreceive:completionhandler:)?language=objc">webView:didReceiveAuthenticationChallenge:completionHandler:</see>.
        /// </summary>
        /// <seealso href="https://developer.apple.com/documentation/foundation/nsurlsessionauthchallengedisposition?language=objc">NSURLSessionAuthChallengeDisposition</seealso>
        /// <seealso href="https://developer.apple.com/documentation/foundation/nsurlcredential?language=objc">NSURLCredential</seealso>
        public IntPtr CompletionHandler;
    }
}
#endif
