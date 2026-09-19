import socket


HOST = "127.0.0.1"
PORT = 5001

SUPPORTED_PROTOCOL_VERSION = 1


def parse_sensor_message(message):
    parts = message.split(",")

    if len(parts) != 11:
        raise ValueError(
            f"Expected 11 fields, got {len(parts)}"
        )

    if parts[0] != "SENSOR":
        raise ValueError(
            f"Unknown packet type: {parts[0]}"
        )

    return {
        "type": parts[0],

        "sequence": int(parts[1]),
        "timestamp": int(parts[2]),

        "ax": float(parts[3]),
        "ay": float(parts[4]),
        "az": float(parts[5]),

        "gx": float(parts[6]),
        "gy": float(parts[7]),
        "gz": float(parts[8]),

        "hall1": int(parts[9]),
        "hall2": int(parts[10]),
    }


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

packets_received = 0
packets_missing = 0
packets_out_of_order = 0
invalid_packets = 0

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

            if not session_ready:
                print(
                    "[SERVER] SENSOR rejected: "
                    "no active session"
                )

                conn.sendall(
                    b"ERROR,NO_SESSION\n"
                )

                continue


            try:
                packet = parse_sensor_message(message)

                packets_received += 1

                sequence = packet["sequence"]


                if expected_sequence is None:
                    expected_sequence = sequence + 1

                elif sequence == expected_sequence:
                    expected_sequence += 1

                elif sequence > expected_sequence:

                    missing = (
                        sequence - expected_sequence
                    )

                    packets_missing += missing

                    print(
                        f"[WARNING] Missing "
                        f"{missing} packet(s): "
                        f"expected seq="
                        f"{expected_sequence}, "
                        f"received seq="
                        f"{sequence}"
                    )

                    expected_sequence = sequence + 1

                else:

                    packets_out_of_order += 1

                    print(
                        f"[WARNING] Out-of-order "
                        f"packet: expected seq="
                        f"{expected_sequence}, "
                        f"received seq="
                        f"{sequence}"
                    )


                if packets_received % 100 == 0:

                    print()
                    print(
                        "========== COMMS STATS =========="
                    )

                    print(
                        f"Device:       {device_id}"
                    )

                    print(
                        f"Received:     "
                        f"{packets_received}"
                    )

                    print(
                        f"Missing:      "
                        f"{packets_missing}"
                    )

                    print(
                        f"Out of order: "
                        f"{packets_out_of_order}"
                    )

                    print(
                        f"Invalid:      "
                        f"{invalid_packets}"
                    )

                    print(
                        f"Last seq:     {sequence}"
                    )

                    print(
                        "================================="
                    )

                    print()


            except ValueError as error:

                invalid_packets += 1

                print(
                    f"[INVALID SENSOR] {error}"
                )

            continue


        # ----------------------------
        # UNKNOWN
        # ----------------------------

        print(
            f"[SERVER] Unknown message: {message}"
        )


conn.close()
server.close()