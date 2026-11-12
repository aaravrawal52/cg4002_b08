using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ARVisualizer.Tests
{
    public sealed class GestureConnectionHUDTests
    {
        // A local WebSocket peer: no Mac, Tailscale route or gesture commands are used.
        sealed class TestMac : IDisposable
        {
            readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            readonly TaskCompletionSource<bool> handshake = new TaskCompletionSource<bool>();
            readonly TaskCompletionSource<bool> close = new TaskCompletionSource<bool>();
            TcpClient client;
            public int Port { get; }
            public Task Session { get; }

            public TestMac()
            {
                listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Session = Serve();
            }

            public void Open() => handshake.TrySetResult(true);
            public void Close() => close.TrySetResult(true);

            async Task Serve()
            {
                client = await listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var buffer = new byte[1024];
                string request = "";
                while (!request.Contains("\r\n\r\n"))
                {
                    int count = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (count == 0) return;
                    request += Encoding.ASCII.GetString(buffer, 0, count);
                }
                string key = null;
                foreach (string line in request.Split('\n'))
                    if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                        key = line.Substring(line.IndexOf(':') + 1).Trim();
                Assert.IsNotNull(key, "The client must request a WebSocket upgrade");
                await handshake.Task;
                using var sha = SHA1.Create();
                string accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(
                    key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\n"
                    + "Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                await stream.WriteAsync(response, 0, response.Length);
                await close.Task;
                byte[] frame = { 0x88, 0x02, 0x03, 0xE8 }; // Normal WebSocket close (1000).
                await stream.WriteAsync(frame, 0, frame.Length);
            }

            public void Dispose()
            {
                handshake.TrySetCanceled();
                close.TrySetCanceled();
                client?.Dispose();
                listener.Stop();
            }
        }

        static void Configure(GestureWebSocketReceiver receiver, int port)
        {
            var settings = new SerializedObject(receiver);
            settings.FindProperty("macTailscaleIP").stringValue = "127.0.0.1";
            settings.FindProperty("macPort").intValue = port;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static IEnumerator WaitFor(GestureWebSocketReceiver receiver, GestureWebSocketReceiver.ConnectionState state)
        {
            float deadline = Time.realtimeSinceStartup + 8;
            while (receiver.State != state && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(state, receiver.State, receiver.LastError);
        }

        [UnityTest]
        public IEnumerator HudFollowsRealWebSocketStateWithoutGestureTraffic()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var receiverObject = new GameObject("Test GestureReceiver");
            receiverObject.SetActive(false);
            var receiver = receiverObject.AddComponent<GestureWebSocketReceiver>();
            var hudObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ARVisualizer/Prefabs/LandscapeHUD.prefab"));
            var hud = hudObject.GetComponent<VisualizerHUD>();
            var hudSettings = new SerializedObject(hud);
            hudSettings.FindProperty("gestureReceiver").objectReferenceValue = receiver;
            hudSettings.ApplyModifiedPropertiesWithoutUndo();
            var label = hud.transform.Find("Safe Area/Top Bar/Status/Wi-Fi Status").GetComponent<Text>();
            var rect = label.rectTransform;
            var size = rect.sizeDelta;
            var position = rect.anchoredPosition;

            try
            {
                using (var mac = new TestMac())
                {
                    Configure(receiver, mac.Port);
                    receiverObject.SetActive(true);
                    yield return WaitFor(receiver, GestureWebSocketReceiver.ConnectionState.Connecting);
                    hud.RefreshStatus();
                    StringAssert.Contains("MAC  Connecting", label.text);
                    Assert.IsFalse(receiver.IsConnected);

                    mac.Open();
                    yield return WaitFor(receiver, GestureWebSocketReceiver.ConnectionState.Connected);
                    hud.RefreshStatus();
                    StringAssert.Contains("MAC  Connected", label.text);
                    StringAssert.Contains("127.0.0.1:" + mac.Port, label.text);
                    Assert.IsTrue(receiver.IsConnected);
                    Assert.Greater(label.color.g, label.color.r);

                    // The floating HUD uses the same Text and must retain the status and layout.
                    hud.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                    yield return new WaitForSecondsRealtime(0.2f);
                    hud.RefreshStatus();
                    StringAssert.Contains("MAC  Connected", label.text);
                    Assert.AreEqual(size, rect.sizeDelta);
                    Assert.AreEqual(position, rect.anchoredPosition);

                    mac.Close();
                    yield return WaitFor(receiver, GestureWebSocketReceiver.ConnectionState.Disconnected);
                    hud.RefreshStatus();
                    StringAssert.Contains("MAC  Disconnected", label.text);
                    Assert.Greater(label.color.r, label.color.g);
                    Assert.IsFalse(receiver.IsConnected);
                    Assert.IsTrue(mac.Session.IsCompletedSuccessfully);
                }

                receiverObject.SetActive(false);
                hud.RefreshStatus();
                StringAssert.Contains("MAC  Disabled", label.text);

                using (var mac = new TestMac())
                {
                    Configure(receiver, mac.Port);
                    mac.Open();
                    receiverObject.SetActive(true);
                    yield return WaitFor(receiver, GestureWebSocketReceiver.ConnectionState.Connected);
                    receiver.SendMessage("OnApplicationPause", true);
                    hud.RefreshStatus();
                    StringAssert.Contains("MAC  Paused", label.text);
                    Assert.IsFalse(receiver.IsConnected);
                }

                // A refused connection must never look connected, even after a previous success.
                int closedPort;
                using (var mac = new TestMac()) closedPort = mac.Port;
                Configure(receiver, closedPort);
                receiver.SendMessage("OnApplicationPause", false);
                yield return WaitFor(receiver, GestureWebSocketReceiver.ConnectionState.Error);
                hud.RefreshStatus();
                StringAssert.Contains("MAC  Connection error", label.text);
                Assert.IsNotEmpty(receiver.LastError);
                Assert.IsFalse(receiver.IsConnected);

                hudSettings.Update();
                hudSettings.FindProperty("tintMacConnectionStatus").boolValue = false;
                hudSettings.ApplyModifiedPropertiesWithoutUndo();
                label.color = Color.cyan;
                hud.RefreshStatus();
                Assert.AreEqual(Color.cyan, label.color, "Authored colors are retained when tinting is disabled");
            }
            finally
            {
                receiverObject.SetActive(false);
                Object.Destroy(receiverObject);
                Object.Destroy(hudObject);
            }
            yield return new ExitPlayMode();
        }
    }
}
