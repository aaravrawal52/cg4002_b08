using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using NativeWebSocket;

namespace ARVisualizer
{
    [Serializable]
    public class GestureMessage
    {
        public string type;
        public int sequence;
        public string gesture;
        public string hand; // Optional: left/right; defaults to right for existing Mac senders.
        public float confidence;
    }

    public class GestureWebSocketReceiver : MonoBehaviour
    {
        public enum ConnectionState { Disconnected, Connecting, Connected, Error, Disabled, Paused }

        public readonly struct ReceivedGesture
        {
            public readonly string Name, Hand;
            public readonly DateTime ReceivedAt;

            public ReceivedGesture(string name, string hand)
            {
                // Keep each receipt on one line, including unknown gestures from a sender.
                name = name.Trim().Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                Name = name.Substring(0, Math.Min(name.Length, 48)).ToUpperInvariant();
                Hand = string.IsNullOrWhiteSpace(hand) ? "R"
                    : hand.Trim().Equals("left", StringComparison.OrdinalIgnoreCase) ? "L"
                    : hand.Trim().Equals("right", StringComparison.OrdinalIgnoreCase) ? "R" : "?";
                ReceivedAt = DateTime.Now;
            }
        }

        public const int HistoryCapacity = 32;
        readonly List<ReceivedGesture> recentGestures = new List<ReceivedGesture>(HistoryCapacity);
        public IReadOnlyList<ReceivedGesture> RecentGestures => recentGestures;
        public int HistoryRevision { get; private set; }

        [Header("Mac connection")]
        [Tooltip("The Mac's fixed Tailscale IP address, without ws:// or a port.")]
        [SerializeField]
        private string macTailscaleIP = "100.x.x.x";
        [SerializeField, Range(1, 65535)] private int macPort = 8765;

        [SerializeField]
        private CommunicationManager communicationManager;

        private WebSocket websocket;
        private volatile ConnectionState connectionState = ConnectionState.Disconnected;
        private bool paused, quitting;

        public CommunicationManager CommandReceiver => communicationManager;
        public string Endpoint => $"{macTailscaleIP}:{macPort}";
        public string LastError { get; private set; } = "";
        public ConnectionState State => !isActiveAndEnabled || quitting ? ConnectionState.Disabled
            : paused ? ConnectionState.Paused : connectionState;
        public bool IsConnected => State == ConnectionState.Connected;

        void OnEnable()
        {
            if (!paused && !quitting) ConnectToMac();
        }

        async void ConnectToMac()
        {
            if (websocket != null) return;
            connectionState = ConnectionState.Connecting;
            LastError = "";
            WebSocket connection = null;
            try
            {
                connection = new WebSocket($"ws://{Endpoint}");
                websocket = connection;

                connection.OnOpen += () =>
                {
                    if (websocket != connection) return;
                    connectionState = ConnectionState.Connected;
                    Debug.Log("[WS] Connected to Mac");
                };

                connection.OnError += error =>
                {
                    if (websocket == connection) ConnectionFailed(error);
                };

                connection.OnClose += code =>
                {
                    if (websocket != connection) return;
                    if (connectionState != ConnectionState.Error) connectionState = ConnectionState.Disconnected;
                    websocket = null;
                    Debug.Log("[WS] Disconnected: " + code);
                };

                connection.OnMessage += bytes =>
                {
                    if (websocket != connection) return;
                    ReceiveGesturePacket(Encoding.UTF8.GetString(bytes));
                };

                await connection.Connect();
            }
            catch (Exception exception)
            {
                if (connection == null || websocket == connection)
                {
                    websocket = null;
                    ConnectionFailed(exception.Message);
                }
            }
        }

        void ConnectionFailed(string error)
        {
            LastError = error;
            connectionState = ConnectionState.Error;
            Debug.LogWarning("[WS] " + error);
        }

        void Disconnect()
        {
            var previous = websocket;
            // Late callbacks from this connection must not change a newer connection's status.
            websocket = null;
            connectionState = ConnectionState.Disconnected;
            previous?.CancelConnection();
        }

        void OnDisable() => Disconnect();

        void OnApplicationPause(bool value)
        {
            paused = value;
            if (paused) Disconnect();
            else if (isActiveAndEnabled && !quitting) ConnectToMac();
        }

        /// <summary>Packet entry point shared by the WebSocket and Editor integration checks.</summary>
        public void ReceiveGesturePacket(string json)
        {
            GestureMessage message;
            try { message = JsonUtility.FromJson<GestureMessage>(json); }
            catch (ArgumentException) { Debug.LogWarning("[GESTURE] Invalid JSON packet"); return; }
            if (message == null || message.type != "GESTURE") return;
            // This records receipt, even if the command cannot run in the current app mode.
            // It lives on the receiver so hiding the HUD never loses incoming gestures.
            if (!string.IsNullOrWhiteSpace(message.gesture))
            {
                if (recentGestures.Count == HistoryCapacity) recentGestures.RemoveAt(HistoryCapacity - 1);
                recentGestures.Insert(0, new ReceivedGesture(message.gesture, message.hand));
                ++HistoryRevision;
            }
            if (!CommandProtocol.TryParseGesture(message.gesture, message.hand, out var command, out var error))
            {
                Debug.LogWarning("[GESTURE] " + error);
                return;
            }
            if (communicationManager == null)
            {
                Debug.LogWarning("[GESTURE] Assign a Communication Manager in the Inspector");
                return;
            }
            communicationManager.ExecuteExternalCommand(command);
            Debug.Log($"[GESTURE] {message.gesture} -> {command.hand}.{command.command}");
        }

        void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            websocket?.DispatchMessageQueue();
#endif
        }

        void OnApplicationQuit()
        {
            quitting = true;
            Disconnect();
        }
    }
}
