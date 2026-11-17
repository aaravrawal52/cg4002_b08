using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARVisualizer
{
    [Serializable]
    public sealed class VisualizerCommand
    {
        public string id;
        public string command;
        public string hand;
        public string token;
        [NonSerialized] public string sourceGesture;
    }

    [Serializable]
    public sealed class CommandReply
    {
        public string id;
        public bool ok;
        public string hand;
        public HandCommandState leftHand, rightHand;
        public string message;
        public bool pointerEnabled;
        public bool placeModeEnabled;
        public bool goggleModeEnabled;
        public bool hudVisible;
        public string pointedHUDButton;
        public string pointedAppButton;
        public int appMenuScreenId;
        public bool appChooserOpen;
        public bool photoLibraryOpen;
        public int cubeCount;
        public int screenCount;
        public bool adjustModeEnabled;
        public string editMode;
        public bool moveModeEnabled;
        public int movingScreenId;
        public int pointedScreenId;
        public ScreenInteractionState interaction;
        public ScreenState adjustedScreen;
    }

    [Serializable]
    public sealed class ScreenInteractionState
    {
        public int screenId;
        public string kind;
        public float u, v, xMetres, yMetres;
        public static ScreenInteractionState From(ScreenSurface screen) => screen == null
            ? new ScreenInteractionState { kind = "none" }
            : new ScreenInteractionState { screenId = screen.Id, kind = screen.InputKind.ToString().ToLowerInvariant(),
                u = screen.InputUV.x, v = screen.InputUV.y, xMetres = screen.InputMetres.x, yMetres = screen.InputMetres.y };
    }

    [Serializable]
    public sealed class ScreenState
    {
        public int id;
        public float widthMetres, heightMetres, thicknessMetres;
        public bool canRotate;
        public static ScreenState From(ScreenSurface screen) => screen == null ? null : new ScreenState
        { id = screen.Id, widthMetres = screen.Width, heightMetres = screen.Height, thicknessMetres = ScreenSurface.Thickness, canRotate = screen.CanRotate };
    }

    [Serializable]
    public sealed class HandCommandState
    {
        public bool tracked, pointerEnabled, hasTipDepth, placeModeEnabled, moveModeEnabled;
        public string reportedHand;
        public string editMode;
        public bool identityStabilized;
        public float rootConfidence, sampleAgeSeconds;
        public int pointedScreenId, adjustedScreenId, movingScreenId;
        public ScreenInteractionState interaction;
    }

    public static class CommandProtocol
    {
        public const int MaxPacketBytes = 4096;

        public static bool TryParse(string text, out VisualizerCommand command, out string error)
        {
            command = null;
            error = "Expected a command or JSON object with a command field.";
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            try
            {
                command = text.StartsWith("{", StringComparison.Ordinal)
                    ? JsonUtility.FromJson<VisualizerCommand>(text)
                    : new VisualizerCommand { command = text };
            }
            catch (ArgumentException) { return false; }
            if (command == null || string.IsNullOrWhiteSpace(command.command)) return false;
            if ((command.id?.Length ?? 0) > 128) { error = "id is limited to 128 characters."; return false; }
            return TryNormalize(command, out error);
        }

        [Serializable] public sealed class Catalogue { public Entry[] commands; public GestureBinding[] gestures; public GestureBinding[] legacyGestures; public AppGestureBinding[] appGestures; }
        [Serializable] public sealed class Entry { public string command; public string[] aliases; }
        [Serializable] public sealed class GestureBinding { public string gesture, command, guide; public string[] aliases; }
        [Serializable] public sealed class AppGestureBinding { public string app, gesture, command; }
        static Dictionary<string, string> gestureCommands;
        static Dictionary<string, string> gestureNames;
        static AppGestureBinding[] appGestures;

        public static string CommandForAppGesture(string app, string gesture)
        {
            if (string.IsNullOrEmpty(gesture)) return null;
            if (appGestures == null)
                appGestures = JsonUtility.FromJson<Catalogue>(Resources.Load<TextAsset>("commands").text).appGestures
                    ?? Array.Empty<AppGestureBinding>();
            foreach (var binding in appGestures)
                if (binding.app == app && string.Equals(binding.gesture, gesture, StringComparison.OrdinalIgnoreCase)) return binding.command;
            return null;
        }

        public static bool TryParseGesture(string gesture, string hand, out VisualizerCommand command, out string error)
        {
            command = null;
            error = "Unknown or missing gesture";
            if (string.IsNullOrWhiteSpace(gesture)) return false;
            if (gestureCommands == null)
            {
                var catalogue = JsonUtility.FromJson<Catalogue>(Resources.Load<TextAsset>("commands").text);
                gestureCommands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                gestureNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                void Add(GestureBinding binding)
                {
                    gestureCommands.Add(binding.gesture, binding.command);
                    gestureNames.Add(binding.gesture, binding.gesture);
                    foreach (var alias in binding.aliases ?? Array.Empty<string>())
                    {
                        gestureCommands.Add(alias, binding.command);
                        gestureNames.Add(alias, binding.gesture);
                    }
                }
                foreach (var binding in catalogue.gestures) Add(binding);
                foreach (var binding in catalogue.legacyGestures ?? Array.Empty<GestureBinding>()) Add(binding);
            }
            if (!gestureCommands.TryGetValue(gesture.Trim(), out var name)) return false;
            command = new VisualizerCommand { id = Guid.NewGuid().ToString("N"), command = name, hand = hand,
                sourceGesture = gestureNames[gesture.Trim()].ToUpperInvariant() };
            return TryNormalize(command, out error);
        }
        static HashSet<string> knownCommands;
        static HashSet<string> KnownCommands
        {
            get
            {
                if (knownCommands != null) return knownCommands;
                var asset = Resources.Load<TextAsset>("commands");
                if (asset == null) throw new InvalidOperationException("Missing Resources/commands.json");
                var catalogue = JsonUtility.FromJson<Catalogue>(asset.text);
                knownCommands = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in catalogue.commands)
                {
                    knownCommands.Add(entry.command);
                    if (entry.aliases != null) foreach (var alias in entry.aliases) knownCommands.Add(alias);
                }
                return knownCommands;
            }
        }

        public static bool TryNormalize(VisualizerCommand command, out string error)
        {
            error = "Expected a command.";
            if (command == null || string.IsNullOrWhiteSpace(command.command)) return false;
            string name = command.command.Trim().ToLowerInvariant();
            string hand = command.hand?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(hand) && hand != "left" && hand != "right")
            { error = "hand must be left or right."; return false; }
            string prefix = name.StartsWith("left.", StringComparison.Ordinal) ? "left"
                : name.StartsWith("right.", StringComparison.Ordinal) ? "right" : null;
            if (prefix != null)
            {
                if (!string.IsNullOrEmpty(hand) && hand != prefix)
                { error = "Conflicting hand selectors."; return false; }
                hand = prefix;
                name = name.Substring(prefix.Length + 1);
            }
            if (!KnownCommands.Contains(name)) { error = "Unknown command: " + name; return false; }
            command.command = name;
            command.hand = string.IsNullOrEmpty(hand) ? "right" : hand;
            error = null;
            return true;
        }
    }
}
