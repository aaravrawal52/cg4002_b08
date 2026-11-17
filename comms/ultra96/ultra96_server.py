import socket
import random
import sys
from pathlib import Path

# Absolute path to the ML deployment directory
DEPLOY_DIR = Path(__file__).resolve().parents[2] / "deploy"

# Make inference.py and its dependencies importable
sys.path.insert(0, str(DEPLOY_DIR))

from data_ingestion import (
    parse_sensor_message,
    GestureSegmenter,
)

from pynq_dpu import DpuOverlay

from inference import DPU_BIT, GestureClassifier

HOST = "127.0.0.1"
PORT = 5001

# Version 2: SENSOR and GESTURE lines carry the device ID.
SUPPORTED_PROTOCOL_VERSION = 2

# Which model each glove uses. Each name is a folder in deploy/
# holding that model's compiled_model.xmodel, label_map.json and
# zscore_stats.json.
#   hall = left hand  (Hall-effect sensors)
#   flex = right hand (flex sensors)
# Both gloves send their two finger readings in the hall1/hall2
# fields, so the packet format is the same for both.
MODEL_FOR_DEVICE = {
    "GLOVE_01": "flex",
    "GLOVE_02": "hall",
}


class GloveSession:
    """Stream state kept separately for each glove, so that
    their sequences and gesture segments can't mix."""

    def __init__(self, device_id):
        self.device_id = device_id
        self.model_name = MODEL_FOR_DEVICE[device_id]
        self.classifier = classifiers[self.model_name]
        self.expected_sequence = None
        self.segmenter = GestureSegmenter()
        self.packets_received = 0
        self.packets_missing = 0
        self.packets_out_of_order = 0

        # True once the current gesture has an UNCLASSIFIED row,
        # meaning the glove could not classify it and it needs ML.
        self.saw_unclassified = False


def parse_hello_message(message):
    parts = message.split(",")

    if len(parts) != 3:
        raise ValueError(
            f"Invalid HELLO field count: {len(parts)}"
        )

    if parts[0] != "HELLO":
        raise ValueError(
            f"Expected HELLO, got {parts[0]}"
        )

    device_id = parts[1]
    protocol_version = int(parts[2])

    return device_id, protocol_version


server = socket.socket(
    socket.AF_INET,
    socket.SOCK_STREAM
)

server.setsockopt(
    socket.SOL_SOCKET,
    socket.SO_REUSEADDR,
    1
)

server.bind((HOST, PORT))
server.listen(1)

print(f"[SERVER] Listening on {HOST}:{PORT}")
print("[SERVER] Waiting for client...")


conn, addr = server.accept()

print(f"[SERVER] Client connected: {addr}")


# --------------------------------
# SESSION STATE
# --------------------------------

# device ID -> GloveSession. Each glove joins with its own HELLO.
sessions = {}

# ============================================================
# DPU MODEL INITIALIZATION
# ============================================================

print("[ML] Loading DPU models...")

# One overlay on the DPU, with one runner per model
# (same setup as main() in deploy/inference.py).
overlay = DpuOverlay(DPU_BIT)

classifiers = {
    model_name: GestureClassifier(overlay, DEPLOY_DIR / model_name)
    for model_name in sorted(set(MODEL_FOR_DEVICE.values()))
}

for device, model_name in MODEL_FOR_DEVICE.items():
    print(f"[ML] {device} -> {model_name} model")

print("[ML] DPU models loaded successfully")

invalid_packets = 0
gesture_sequence = 0

buffer = ""


# --------------------------------
# RECEIVE LOOP
# --------------------------------

while True:

    data = conn.recv(4096)

    if not data:
        print("[SERVER] Client disconnected")
        break

    buffer += data.decode()

    while "\n" in buffer:

        message, buffer = buffer.split("\n", 1)

        if not message:
            continue


        # ----------------------------
        # HELLO
        # ----------------------------

        if message.startswith("HELLO,"):

            try:
                device_id, protocol_version = (
                    parse_hello_message(message)
                )

                if protocol_version != SUPPORTED_PROTOCOL_VERSION:
                    print(
                        f"[SERVER] Unsupported protocol "
                        f"version={protocol_version}"
                    )

                    conn.sendall(
                        b"ERROR,UNSUPPORTED_VERSION\n"
                    )

                    continue

                if device_id not in MODEL_FOR_DEVICE:
                    print(
                        f"[SERVER] Unknown device={device_id}: "
                        "no model assigned in MODEL_FOR_DEVICE"
                    )

                    conn.sendall(
                        b"ERROR,UNKNOWN_DEVICE\n"
                    )

                    continue


                # New session for this glove only.

                sessions[device_id] = GloveSession(device_id)


                print()
                print("========== NEW SESSION ==========")
                print(f"Device:   {device_id}")
                print(f"Model:    {sessions[device_id].model_name}")
                print(f"Protocol: {protocol_version}")
                print("=================================")
                print()


                ack = f"ACK,{device_id}\n"

                conn.sendall(
                    ack.encode()
                )

                print(
                    f"[SERVER] ACK sent to {device_id}"
                )


            except ValueError as error:
                invalid_packets += 1

                print(
                    f"[INVALID HELLO] {error}"
                )

            continue


        # ----------------------------
        # SENSOR
        # ----------------------------

        if message.startswith("SENSOR,"):

            # ----------------------------------------
            # SESSION VALIDATION
            # ----------------------------------------

            # SENSOR,device,sequence,timestamp,...,tag

            packet_fields = message.split(",", 2)

            if len(packet_fields) != 3:

                invalid_packets += 1

                print(
                    f"[INVALID SENSOR] {message}"
                )

                continue

            device_id = packet_fields[1]

            session = sessions.get(device_id)

            if session is None:

                print(
                    f"[SERVER] SENSOR rejected: "
                    f"no active session for {device_id}"
                )

                conn.sendall(
                    b"ERROR,NO_SESSION\n"
                )

                continue

            try:

                # ----------------------------------------
                # PARSE USING TEAMMATE'S CODE
                # ----------------------------------------

                # The ML parser expects no device field.

                row = parse_sensor_message(
                    f"SENSOR,{packet_fields[2]}"
                )

                sequence = int(row["sequence"])

                tag = row["tag"]

                if tag not in {
                    "IDLE",
                    "ACTIVE",
                    "UNCLASSIFIED"
                }:

                    raise ValueError(
                        f"Invalid tag: {tag}"
                    )

                session.packets_received += 1

                # ----------------------------------------
                # SEQUENCE TRACKING
                # ----------------------------------------

                if session.expected_sequence is None:

                    session.expected_sequence = sequence + 1

                elif sequence == session.expected_sequence:

                    session.expected_sequence += 1

                elif sequence > session.expected_sequence:

                    missing = (
                        sequence - session.expected_sequence
                    )

                    session.packets_missing += missing

                    session.expected_sequence = sequence + 1

                    print(
                        f"[WARNING] {device_id} Missing "
                        f"{missing} packet(s)"
                    )

                else:
                    # FireBeetle restarted its stream sequence.
                    if sequence == 0:
                        print(
                            f"[STREAM] {device_id} "
                            "Sequence counter reset"
                        )
                        session.expected_sequence = 1
                        session.segmenter = GestureSegmenter()
                        session.saw_unclassified = False
                    else:
                        session.packets_out_of_order += 1
                        print(
                            f"[WARNING] {device_id} "
                            f"Out-of-order seq={sequence}"
                        )

                # ----------------------------------------
                # ML INGESTION
                # ----------------------------------------

                segment = session.segmenter.add_row(row)

                if tag == "ACTIVE" and sequence % 50 == 0:

                    print(
                        f"[SEGMENT] {device_id} "
                        f"ACTIVE seq={sequence}"
                    )

                if tag == "UNCLASSIFIED":

                    session.saw_unclassified = True

                    print(
                        f"[SEGMENT] {device_id} UNCLASSIFIED "
                        f"seq={sequence}"
                    )

                # ----------------------------------------
                # COMPLETED GESTURE SEGMENT
                # ----------------------------------------

                # The segmenter returns every finished gesture. Only
                # gestures the glove tagged UNCLASSIFIED need the
                # model. Gestures the glove recognised itself end
                # with IDLE and arrive separately as LOCAL_GESTURE,
                # so classifying them too would report them twice.

                if segment is not None and not session.saw_unclassified:

                    print(
                        f"[SEGMENT] {device_id} recognised on glove - "
                        f"ML skipped ({len(segment)} samples)"
                    )

                    segment = None

                if segment is not None:

                    print()
                    print(
                        "========== ML INPUT READY =========="
                    )

                    print(
                        f"Source:       {device_id}"
                    )

                    print(
                        f"Samples:      {len(segment)}"
                    )

                    print(
                        f"First seq:    "
                        f"{segment[0]['sequence']}"
                    )

                    print(
                        f"Last seq:     "
                        f"{segment[-1]['sequence']}"
                    )

                    print(
                        "Status:       READY FOR ML"
                    )

                    print(
                        "===================================="
                    )
                    # ============================================================
# ACTUAL DPU INFERENCE
# ============================================================

                    try:
                        gesture, confidence, inference_ms = (
                            session.classifier.classify(segment)
                        )

                        print()
                        print("========== ML PREDICTION ==========")
                        print(f"Device:        {device_id}")
                        print(f"Model:         {session.model_name}")
                        print(f"Gesture:       {gesture}")
                        print(f"Confidence:    {confidence:.4f}")
                        print(f"DPU time:      {inference_ms:.2f} ms")
                        print("===================================")

                        # Send the prediction back to the relay Mac.
                        response = (
                            f"GESTURE,"
                            f"{device_id},"
                            f"{gesture_sequence},"
                            f"{gesture},"
                            f"{confidence}\n"
                        )

                        conn.sendall(response.encode("utf-8"))

                        print(
                            f"[ML -> MAC] Sent device={device_id} "
                            f"gesture={gesture} "
                            f"seq={gesture_sequence}"
                        )

                        gesture_sequence += 1

                    except Exception as error:
                        print(f"[ML] Inference failed: {error}")

                    # Your teammate's actual DPU inference
                    # will be called here later:
                    #
                    # gesture, confidence, inference_ms = (
                    #     classifier.classify(segment)
                    # )

                # A gesture always ends on an IDLE row, so start the
                # next gesture with a clean flag. This also clears it
                # when the segmenter discarded an over-long gesture.
                if tag == "IDLE":
                    session.saw_unclassified = False

                # ----------------------------------------
                # STATISTICS
                # ----------------------------------------

                if session.packets_received % 100 == 0:

                    print()
                    print(
                        "========== ULTRA96 STATS =========="
                    )

                    print(
                        f"Device:       {device_id}"
                    )

                    print(
                        f"Received:     {session.packets_received}"
                    )

                    print(
                        f"Missing:      {session.packets_missing}"
                    )

                    print(
                        f"Out of order: {session.packets_out_of_order}"
                    )

                    print(
                        f"Invalid:      {invalid_packets}"
                    )

                    print(
                        "==================================="
                    )

            except (ValueError, KeyError) as error:

                invalid_packets += 1

                print(
                    f"[INVALID SENSOR] {error}"
                )

            continue


        # ----------------------------
        # UNKNOWN
        # ----------------------------
        # ============================================================
        # LOCALLY CLASSIFIED GESTURE
        # ============================================================

        if message.startswith("LOCAL_GESTURE,"):

            parts = message.split(",")

            if len(parts) < 2 or parts[1] not in sessions:

                conn.sendall(
                    b"ERROR,NO_SESSION\n"
                )

                continue

            try:

                if len(parts) != 5:

                    raise ValueError(
                        "Invalid LOCAL_GESTURE format"
                    )

                event_device = parts[1]

                event_sequence = int(parts[2])

                sensor_sequence = int(parts[3])

                gesture = parts[4]

                print()
                print(
                    "========== LOCAL GESTURE =========="
                )

                print(
                    f"Device:       {event_device}"
                )

                print(
                    f"Event seq:    {event_sequence}"
                )

                print(
                    f"Sensor seq:   {sensor_sequence}"
                )

                print(
                    f"Gesture:      {gesture}"
                )

                print(
                    "==================================="
                )

                # Locally classified gestures bypass ML.

                response = (
                    f"GESTURE,"
                    f"{event_device},"
                    f"{gesture_sequence},"
                    f"{gesture},"
                    f"1.0\n"
                )

                conn.sendall(
                    response.encode()
                )

                gesture_sequence += 1

            except ValueError as error:

                invalid_packets += 1

                print(
                    f"[INVALID LOCAL_GESTURE] {error}"
                )

            continue
        print(
            f"[SERVER] Unknown message: {message}"
        )


conn.close()
server.close()