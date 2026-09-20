#!/usr/bin/env python3
"""Send Unity AR visualiser commands from the PC keyboard (Windows, no dependencies)."""
import argparse
import json
import msvcrt
import socket
import sys
import time
import uuid


def send_command(client, host, port, token, command, timeout):
    command_id = uuid.uuid4().hex
    payload = json.dumps({
        "id": command_id,
        "command": command,
        "token": token,
    }).encode("utf-8")
    client.sendto(payload, (host, port))
    client.settimeout(timeout)
    try:
        while True:
            reply, _ = client.recvfrom(4096)
            try:
                reply = json.loads(reply)
            except (ValueError, UnicodeDecodeError):
                continue
            if isinstance(reply, dict) and reply.get("id") == command_id:
                return reply
    except socket.timeout:
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("host", help="iPhone IPv4 address shown in the HUD, or 127.0.0.1 for Editor")
    parser.add_argument("--port", type=int, default=7777)
    parser.add_argument("--token", default="", help="Match CommunicationManager.sharedToken if configured")
    parser.add_argument("--timeout", type=float, default=2.0)
    args = parser.parse_args()
    if not 1 <= args.port <= 65535 or args.timeout <= 0:
        parser.error("port must be 1..65535 and timeout > 0")

    key_commands = {
        "p": "screen.place",
        "z": "screen.undo",
        "c": "screens.clear",
        "up": "adjust.grow",
        "down": "adjust.shrink",
        "left": "adjust.rotate.ccw",
        "right": "adjust.rotate.cw",
        "u": "ui.click",
    }
    toggle_commands = {
        "i": ("pointer.on", "pointer.off"),
        "=": ("place.enter", "place.exit"),
        "a": ("adjust.enter", "adjust.exit"),
        "g": ("goggle.enter", "goggle.exit"),
    }
    toggle_state = {key: False for key in toggle_commands}

    print("Keyboard command listener active. Press Esc to quit.")
    print("I pointer | = place mode | P place | Z undo | C clear | A adjust mode")
    print("Arrow keys grow/shrink/rotate")

    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as client:
            while True:
                key = msvcrt.getwch()
                if key == "\x1b":
                    break
                if key in ("\x00", "\xe0"):
                    arrow = msvcrt.getwch()
                    key = {"H": "up", "P": "down", "K": "left", "M": "right"}.get(arrow)
                else:
                    key = key.lower()

                if key in toggle_commands:
                    toggle_state[key] = not toggle_state[key]
                    command = toggle_commands[key][toggle_state[key]]
                else:
                    command = key_commands.get(key)
                if command is None:
                    continue

                try:
                    reply = send_command(client, args.host, args.port, args.token, command, args.timeout)
                except OSError as error:
                    print(f"Network error: {error}", file=sys.stderr)
                    continue
                if reply is None:
                    print(f"{command}: no reply")
                else:
                    status = "ok" if reply.get("ok") else "rejected"
                    print(f"{command}: {status}")
    except KeyboardInterrupt:
        pass
    print("Keyboard command listener stopped.")


if __name__ == "__main__":
    main()
