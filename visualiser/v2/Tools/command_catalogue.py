"""The Unity resource is the shared command catalogue for the app and PC tools."""
import json
from pathlib import Path

CATALOGUE_PATH = Path(__file__).resolve().parent.parent / "Assets/ARVisualizer/Resources/commands.json"
CATALOGUE = json.loads(CATALOGUE_PATH.read_text(encoding="utf-8"))
BASE_COMMANDS = tuple(
    name
    for entry in CATALOGUE["commands"]
    for name in (entry["command"], *entry.get("aliases", []))
)
COMMAND_CHOICES = tuple(
    prefix + name for name in BASE_COMMANDS for prefix in ("", "left.", "right.")
)


def qualify_command(command, hand=None):
    """Return an explicit hand command, rejecting conflicting selectors."""
    command = command.strip().lower()
    prefix, _, rest = command.partition(".")
    if hand not in (None, "left", "right"):
        raise ValueError("hand must be left or right")
    if prefix in ("left", "right"):
        if hand is not None and prefix != hand:
            raise ValueError("command prefix conflicts with --hand")
        hand, command = prefix, rest
    if command not in BASE_COMMANDS:
        raise ValueError(f"Unknown command: {command}")
    return f"{hand or 'right'}.{command}"
