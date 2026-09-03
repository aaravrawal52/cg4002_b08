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

        except ValueError as error:
            print(f"[INVALID PACKET] {error}")

conn.close()
server.close()
