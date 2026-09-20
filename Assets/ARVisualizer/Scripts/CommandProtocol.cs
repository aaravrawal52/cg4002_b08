using System;
using UnityEngine;

namespace ARVisualizer
{
    [Serializable]
    public sealed class VisualizerCommand
    {
        public string id;
        public string command;
        public string token;
    }

    [Serializable]
    public sealed class CommandReply
    {
        public string id;
        public bool ok;
        public string message;
        public bool pointerEnabled;
        public bool placeModeEnabled;
        public bool goggleModeEnabled;
        public bool hudVisible;
        public string pointedHUDButton;
        public int cubeCount;
        public int screenCount;
        public bool adjustModeEnabled;
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
            command.command = command.command.Trim().ToLowerInvariant();
            switch (command.command)
            {
                case "pointer.on": case "pointer.off": case "cube.place":
                case "goggle.enter": case "goggle.exit": case "ui.click":
                case "place.enter": case "place.exit":
                case "cube.undo": case "cubes.clear": case "status":
                case "screen.place": case "screen.undo": case "screens.clear":
                case "adjust.enter": case "adjust.exit": case "adjust.grow": case "adjust.shrink":
                case "adjust.rotate.cw": case "adjust.rotate.ccw":
                    error = null;
                    return true;
                default:
                    error = "Unknown command: " + command.command;
                    return false;
            }
        }
    }
}
