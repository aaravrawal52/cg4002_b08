import socket
import time
import random

HOST = "127.0.0.1"
PORT = 5001

sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)

print("[CLIENT] Connecting...")

sock.connect((HOST, PORT))

print("[CLIENT] Connected")

sequence = 0

try:

    while True:

        timestamp = int(time.monotonic() * 1000)

        acc_x = random.uniform(-2.0, 2.0)
        acc_y = random.uniform(-2.0, 2.0)
        acc_z = random.uniform(8.0, 10.0)

        gyro_x = random.uniform(-1.0, 1.0)
        gyro_y = random.uniform(-1.0, 1.0)
        gyro_z = random.uniform(-1.0, 1.0)

        flex_1 = random.randint(0, 4095)
        flex_2 = random.randint(0, 4095)
        flex_3 = random.randint(0, 4095)
        flex_4 = random.randint(0, 4095)
        flex_5 = random.randint(0, 4095)

        message = (
            f"SENSOR,"
            f"{sequence},"
            f"{timestamp},"
            f"{acc_x:.3f},"
            f"{acc_y:.3f},"
            f"{acc_z:.3f},"
            f"{gyro_x:.3f},"
            f"{gyro_y:.3f},"
            f"{gyro_z:.3f},"
            f"{flex_1},"
            f"{flex_2},"
            f"{flex_3},"
            f"{flex_4},"
            f"{flex_5}\n"
        )

        sock.sendall(message.encode())
        print("[SENT]", message.strip())

        sequence += 1

        time.sleep(0.01)

except KeyboardInterrupt:

    print("\n[CLIENT] Stopping")

finally:

    sock.close()

