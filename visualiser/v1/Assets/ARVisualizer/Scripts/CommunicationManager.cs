using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ARVisualizer
{
    /// <summary>Local Wi-Fi UDP template. Replace the transport while keeping CommandReceived.</summary>
    public sealed class CommunicationManager : MonoBehaviour
    {
        [Header("Wi-Fi command receiver")]
        [Tooltip("UDP port to listen on. The controller must send to this port.")]
        [SerializeField, Range(1024, 65535)] int port = 7777;
        [Tooltip("Optional shared token required in incoming commands. Leave empty for local prototyping.")]
        [SerializeField] string sharedToken = "";
        public event Action<VisualizerCommand, Action<CommandReply>> CommandReceived;
        public string Status => status;
        public string Endpoint { get; private set; } = "";
        public int Port => port;

        volatile string status = "Starting";
        UdpClient socket;
        Thread receiver;
        int generation;
        readonly ConcurrentQueue<Packet> incoming = new ConcurrentQueue<Packet>();
        readonly Dictionary<string, string> replies = new Dictionary<string, string>();
        readonly HashSet<string> pending = new HashSet<string>();
        readonly Queue<string> replyOrder = new Queue<string>();

        struct Packet { public string text; public IPEndPoint sender; public int generation; }

        void OnEnable() => StartListening();
        void OnDisable() => StopListening();
        void OnApplicationPause(bool paused)
        {
            if (paused) StopListening();
            else if (isActiveAndEnabled) StartListening();
        }

        public void StartListening()
        {
            if (socket != null) return;
            try
            {
                socket = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                socket.Client.ReceiveTimeout = 250;
                var localSocket = socket;
                int version = ++generation;
                Endpoint = LocalAddress() + ":" + port;
                status = "Listening";
                receiver = new Thread(() => ReceiveLoop(localSocket, version))
                    { IsBackground = true, Name = "AR Wi-Fi commands" };
                receiver.Start();
            }
            catch (SocketException e)
            {
                socket?.Close();
                socket = null;
                status = "Network unavailable: " + e.SocketErrorCode;
            }
        }

        public void StopListening()
        {
            ++generation;
            var old = socket;
            socket = null;
            old?.Close();
            receiver?.Join(500);
            receiver = null;
            while (incoming.TryDequeue(out _)) { }
            status = "Offline";
        }

        void ReceiveLoop(UdpClient localSocket, int version)
        {
            while (version == Volatile.Read(ref generation))
            {
                try
                {
                    var sender = new IPEndPoint(IPAddress.Any, 0);
                    byte[] bytes = localSocket.Receive(ref sender);
                    if (bytes.Length > CommandProtocol.MaxPacketBytes || incoming.Count >= 64) continue;
                    incoming.Enqueue(new Packet { text = Encoding.UTF8.GetString(bytes), sender = sender, generation = version });
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.TimedOut) continue;
                    if (version == Volatile.Read(ref generation)) status = "Network error: " + e.SocketErrorCode;
                    break;
                }
                catch (ObjectDisposedException) { break; }
            }
        }

        void Update()
        {
            // All parsing, Unity access, and callbacks happen on Unity's main thread.
            for (int i = 0; i < 8 && incoming.TryDequeue(out var packet); ++i)
            {
                if (packet.generation != generation) continue;
                if (!CommandProtocol.TryParse(packet.text, out var command, out var error))
                {
                    Send(packet, JsonUtility.ToJson(new CommandReply { id = command?.id, message = error }));
                    continue;
                }
                if (!string.IsNullOrEmpty(sharedToken) && !string.Equals(sharedToken, command.token, StringComparison.Ordinal))
                {
                    Send(packet, JsonUtility.ToJson(new CommandReply { id = command.id, message = "Invalid token." }));
                    continue;
                }
                string key = string.IsNullOrEmpty(command.id) ? null : packet.sender + "/" + command.id;
                if (key != null && replies.TryGetValue(key, out var previous)) { Send(packet, previous); continue; }
                if (key != null && !pending.Add(key)) continue;
                void Complete(CommandReply reply)
                {
                    reply.id = command.id;
                    string json = JsonUtility.ToJson(reply);
                    if (key != null)
                    {
                        pending.Remove(key);
                        replies[key] = json;
                        replyOrder.Enqueue(key);
                        while (replyOrder.Count > 128) replies.Remove(replyOrder.Dequeue());
                    }
                    Send(packet, json);
                }
                try
                {
                    if (CommandReceived == null) Complete(new CommandReply { message = "Command handler unavailable." });
                    else CommandReceived(command, Complete);
                }
                catch (Exception e) { Complete(new CommandReply { message = e.Message }); }
            }
        }

        void Send(Packet packet, string json)
        {
            if (socket == null || packet.generation != generation) return;
            try { byte[] bytes = Encoding.UTF8.GetBytes(json); socket.Send(bytes, bytes.Length, packet.sender); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        static string LocalAddress()
        {
            try
            {
                string fallback = "See iPhone Wi-Fi settings";
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var address in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (adapter.Name == "en0" || adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return address.Address.ToString();
                        fallback = address.Address.ToString();
                    }
                }
                return fallback;
            }
            catch (Exception) { return "See device Wi-Fi settings"; }
        }
    }
}
