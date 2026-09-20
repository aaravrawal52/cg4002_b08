#!/usr/bin/env python3
"""Send one command to the Unity AR visualiser over unicast UDP (Python 3, no dependencies)."""
import argparse
import json
import socket
import sys
import time
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("host", help="iPhone IPv4 address shown in the HUD, or 127.0.0.1 for Editor")
    parser.add_argument("command", choices=[
        "pointer.on", "pointer.off", "place.enter", "place.exit",
        "goggle.enter", "goggle.exit", "ui.click",
        "screen.place", "screen.undo", "screens.clear",
        "adjust.enter", "adjust.exit", "adjust.grow", "adjust.shrink",
        "adjust.rotate.cw", "adjust.rotate.ccw", "status",
        "cube.place", "cube.undo", "cubes.clear",  # Compatibility aliases.
    ])
    parser.add_argument("--port", type=int, default=7777)
    parser.add_argument("--token", default="", help="Match CommunicationManager.sharedToken if configured")
    parser.add_argument("--timeout", type=float, default=3.0)
    parser.add_argument("--attempts", type=int, default=3)
    args = parser.parse_args()
    if not 1 <= args.port <= 65535 or args.timeout <= 0 or args.attempts < 1:
        parser.error("port must be 1..65535, timeout > 0, and attempts >= 1")
    command_id = uuid.uuid4().hex
    payload = json.dumps({"id": command_id, "command": args.command, "token": args.token}).encode("utf-8")
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as client:
            client.connect((args.host, args.port))
            # Keep the same socket and id for retries so placements are deduplicated.
            for _ in range(args.attempts):
                client.send(payload)
                deadline = time.monotonic() + args.timeout
                while time.monotonic() < deadline:
                    client.settimeout(max(0.01, deadline - time.monotonic()))
                    try:
                        reply = json.loads(client.recv(4096))
                    except socket.timeout:
                        break
                    except (ValueError, UnicodeDecodeError):
                        continue
                    if not isinstance(reply, dict) or reply.get("id") != command_id:
                        continue
                    print(json.dumps(reply, indent=2))
                    return 0 if reply.get("ok") else 1
    except OSError as error:
        print(f"Network error: {error}", file=sys.stderr)
        return 2
    print("No reply. Check Wi-Fi/IP, foreground app, firewall and iOS Local Network permission.\n"
          "The command may have executed; check the HUD before issuing a new placement.", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
