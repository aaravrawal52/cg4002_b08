import socket
import random

import sys
from pathlib import Path

DEPLOY_DIR = Path(__file__).resolve().parents[2] / "deploy"
sys.path.insert(0, str(DEPLOY_DIR))

from data_ingestion import (
    parse_sensor_message,
    GestureSegmenter,
)

from inference import GestureClassifier

HOST = "127.0.0.1"
PORT = 5001

SUPPORTED_PROTOCOL_VERSION = 1



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

session_ready = False
device_id = None

expected_sequence = None
segmenter = GestureSegmenter()
# ============================================================
# DPU MODEL INITIALIZATION
# ============================================================

print("[ML] Loading DPU model...")

classifier = GestureClassifier(
    deploy_dir=DEPLOY_DIR
)

print("[ML] DPU model loaded successfully")

packets_received = 0
packets_missing = 0
packets_out_of_order = 0
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
                new_device_id, protocol_version = (
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


                # New communication session

                device_id = new_device_id
                session_ready = True

                expected_sequence = None
                segmenter = GestureSegmenter()

                packets_received = 0
                packets_missing = 0
                packets_out_of_order = 0
                invalid_packets = 0


                print()
                print("========== NEW SESSION ==========")
                print(f"Device:   {device_id}")
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

            if not session_ready:

                print(
                    "[SERVER] SENSOR rejected: no active session"
                )

                conn.sendall(
                    b"ERROR,NO_SESSION\n"
                )

                continue

            try:

                # ----------------------------------------
                # PARSE USING TEAMMATE'S CODE
                # ----------------------------------------

                row = parse_sensor_message(message)

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

                packets_received += 1

                # ----------------------------------------
                # SEQUENCE TRACKING
                # ----------------------------------------

                if expected_sequence is None:

                    expected_sequence = sequence + 1

                elif sequence == expected_sequence:

                    expected_sequence += 1

                elif sequence > expected_sequence:

                    missing = sequence - expected_sequence

                    packets_missing += missing

                    expected_sequence = sequence + 1

                    print(
                        f"[WARNING] Missing "
                        f"{missing} packet(s)"
                    )

                else:

                    packets_out_of_order += 1

                    print(
                        f"[WARNING] Out-of-order "
                        f"seq={sequence}"
                    )

                # ----------------------------------------
                # ML INGESTION
                # ----------------------------------------

                segment = segmenter.add_row(row)

                if tag == "ACTIVE" and sequence % 50 == 0:

                    print(
                        f"[SEGMENT] ACTIVE seq={sequence}"
                    )

                if tag == "UNCLASSIFIED":

                    print(
                        f"[SEGMENT] UNCLASSIFIED "
                        f"seq={sequence}"
                    )

                # ----------------------------------------
                # COMPLETED GESTURE SEGMENT
                # ----------------------------------------

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
                            classifier.classify(segment)
                        )

                        print()
                        print("========== ML PREDICTION ==========")
                        print(f"Gesture:       {gesture}")
                        print(f"Confidence:    {confidence:.4f}")
                        print(f"DPU time:      {inference_ms:.2f} ms")
                        print("===================================")

                        # Send the prediction back to the relay Mac.
                        response = (
                            f"GESTURE,"
                            f"{gesture_sequence},"
                            f"{gesture},"
                            f"{confidence}\n"
                        )

                        conn.sendall(response.encode("utf-8"))

                        print(
                            f"[ML -> MAC] Sent gesture={gesture} "
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

                # ----------------------------------------
                # STATISTICS
                # ----------------------------------------

                if packets_received % 100 == 0:

                    print()
                    print(
                        "========== ULTRA96 STATS =========="
                    )

                    print(
                        f"Received:     {packets_received}"
                    )

                    print(
                        f"Missing:      {packets_missing}"
                    )

                    print(
                        f"Out of order: {packets_out_of_order}"
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

            if not session_ready:

                conn.sendall(
                    b"ERROR,NO_SESSION\n"
                )

                continue

            try:

                parts = message.split(",")

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