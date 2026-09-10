import socket


HOST = "127.0.0.1"
PORT = 5001


def parse_sensor_message(message):
    parts = message.split(",")

    if len(parts) != 14:
        raise ValueError(
            f"Expected 14 fields, got {len(parts)}"
        )

    if parts[0] != "SENSOR":
        raise ValueError(
            f"Unknown packet type: {parts[0]}"
        )

    packet = {
        "type": parts[0],
        "sequence": int(parts[1]),
        "timestamp": int(parts[2]),

        "acc_x": float(parts[3]),
        "acc_y": float(parts[4]),
        "acc_z": float(parts[5]),

        "gyro_x": float(parts[6]),
        "gyro_y": float(parts[7]),
        "gyro_z": float(parts[8]),

        "flex_1": int(parts[9]),
        "flex_2": int(parts[10]),
        "flex_3": int(parts[11]),
        "flex_4": int(parts[12]),
        "flex_5": int(parts[13]),
    }

    return packet


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
expected_sequence = None

packets_received = 0
packets_missing = 0
packets_out_of_order = 0
invalid_packets = 0

buffer = ""

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

        try:
            packet = parse_sensor_message(message)
            packets_received += 1

            sequence = packet["sequence"]

            if expected_sequence is None:
                expected_sequence = sequence + 1

            elif sequence == expected_sequence:
                expected_sequence += 1

            elif sequence > expected_sequence:
                missing = sequence - expected_sequence
                packets_missing += missing

                print(
                    f"[WARNING] Missing {missing} packet(s): "
                    f"expected seq={expected_sequence}, "
                    f"received seq={sequence}"
                )

                expected_sequence = sequence + 1

            else:
                packets_out_of_order += 1

                print(
                    f"[WARNING] Out-of-order packet: "
                    f"expected seq={expected_sequence}, "
                    f"received seq={sequence}"
                )
            print(
                f"[SENSOR] "
                f"seq={packet['sequence']} "
                f"timestamp={packet['timestamp']} "
                f"acc=("
                f"{packet['acc_x']:.2f}, "
                f"{packet['acc_y']:.2f}, "
                f"{packet['acc_z']:.2f}"
                f")"
            )
            if packets_received % 100 == 0:
                print()
                print("========== COMMS STATS ==========")
                print(f"Received:     {packets_received}")
                print(f"Missing:      {packets_missing}")
                print(f"Out of order: {packets_out_of_order}")
                print(f"Invalid:      {invalid_packets}")
                print(f"Last seq:     {sequence}")
                print("=================================")
                print()

        except ValueError as error:
            invalid_packets += 1
            print(f"[INVALID PACKET] {error}")

conn.close()
server.close()
