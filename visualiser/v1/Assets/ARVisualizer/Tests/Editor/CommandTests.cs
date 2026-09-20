using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ARVisualizer.Tests
{
    public sealed class CommandTests
    {
        [TestCase("pointer.on")]
        [TestCase("pointer.off")]
        [TestCase("goggle.enter")]
        [TestCase("goggle.exit")]
        [TestCase("ui.click")]
        [TestCase("place.enter")]
        [TestCase("place.exit")]
        [TestCase("cube.place")]
        [TestCase("cube.undo")]
        [TestCase("cubes.clear")]
        [TestCase("status")]
        [TestCase("screen.place")]
        [TestCase("screen.undo")]
        [TestCase("screens.clear")]
        [TestCase("adjust.enter")]
        [TestCase("adjust.exit")]
        [TestCase("adjust.grow")]
        [TestCase("adjust.shrink")]
        [TestCase("adjust.rotate.cw")]
        [TestCase("adjust.rotate.ccw")]
        public void AcceptsDocumentedCommands(string value)
        {
            Assert.IsTrue(CommandProtocol.TryParse("  " + value.ToUpperInvariant() + "\n", out var command, out _));
            Assert.AreEqual(value, command.command);
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("{}")]
        [TestCase("{broken")]
        [TestCase("{\"command\":\"\"}")]
        [TestCase("cube.delete.everything")]
        [TestCase("pointer.on\ncube.place")]
        public void RejectsInvalidPackets(string value) => Assert.IsFalse(CommandProtocol.TryParse(value, out _, out _));

        [Test]
        public void PreservesCorrelationAndToken()
        {
            Assert.IsTrue(CommandProtocol.TryParse("{\"id\":\"abc\",\"command\":\"cube.place\",\"token\":\"secret\"}", out var value, out _));
            Assert.AreEqual("abc", value.id);
            Assert.AreEqual("secret", value.token);
        }

        [UnityTest]
        public IEnumerator UdpDispatchesOnMainThreadDeduplicatesAndReopens()
        {
            int mainThread = Environment.CurrentManagedThreadId;
            int calls = 0;
            int freePort;
            using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                freePort = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
            var obj = new GameObject("UDP test");
            obj.SetActive(false);
            var manager = obj.AddComponent<CommunicationManager>();
            var update = typeof(CommunicationManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("port").intValue = freePort;
            serialized.FindProperty("sharedToken").stringValue = "test-token";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.CommandReceived += (command, done) =>
            {
                Assert.AreEqual(mainThread, Environment.CurrentManagedThreadId);
                ++calls;
                done(new CommandReply { ok = true, cubeCount = calls, message = command.command });
            };
            using var client = new UdpClient();
            client.Connect(IPAddress.Loopback, freePort);
            try
            {
                obj.SetActive(true);
                manager.StartListening();
                Assert.AreEqual("Listening", manager.Status);
                byte[] packet = Encoding.UTF8.GetBytes("{\"id\":\"one\",\"command\":\"cube.place\",\"token\":\"test-token\"}");
                for (int attempt = 0; attempt < 2; ++attempt)
                {
                    client.Send(packet, packet.Length);
                    float deadline = Time.realtimeSinceStartup + 3;
                    while (client.Available == 0 && Time.realtimeSinceStartup < deadline)
                    { update.Invoke(manager, null); yield return null; }
                    Assert.Greater(client.Available, 0, "No UDP reply received");
                    IPEndPoint sender = null;
                    var reply = JsonUtility.FromJson<CommandReply>(Encoding.UTF8.GetString(client.Receive(ref sender)));
                    Assert.IsTrue(reply.ok);
                    Assert.AreEqual("one", reply.id);
                    Assert.AreEqual(1, reply.cubeCount);
                }
                Assert.AreEqual(1, calls, "Duplicate id must not repeat placement");
                byte[] denied = Encoding.UTF8.GetBytes("{\"command\":\"cubes.clear\",\"token\":\"wrong\"}");
                client.Send(denied, denied.Length);
                float until = Time.realtimeSinceStartup + 3;
                while (client.Available == 0 && Time.realtimeSinceStartup < until)
                { update.Invoke(manager, null); yield return null; }
                Assert.Greater(client.Available, 0);
                IPEndPoint source = null;
                Assert.IsFalse(JsonUtility.FromJson<CommandReply>(Encoding.UTF8.GetString(client.Receive(ref source))).ok);
                Assert.AreEqual(1, calls);
                manager.StopListening();
                Assert.AreEqual("Offline", manager.Status);
                manager.StartListening();
                Assert.AreEqual("Listening", manager.Status);
            }
            finally { manager.StopListening(); UnityEngine.Object.DestroyImmediate(obj); }
        }
    }
}
