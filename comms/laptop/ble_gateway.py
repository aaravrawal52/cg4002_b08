import asyncio
import time

from bleak import BleakClient
from bleak import BleakScanner


DEVICE_NAME = "CG4002_GLOVE"

SENSOR_CHAR_UUID = (
    "12345678-1234-1234-1234-1234567890ac"
)

ULTRA96_HOST = "127.0.0.1"
ULTRA96_PORT = 5001

DEVICE_ID = "GLOVE_01"
PROTOCOL_VERSION = 1


sensor_queue = asyncio.Queue(maxsize=500)

ble_received = 0
ble_missing = 0
queue_dropped = 0

last_ble_sequence = None


def parse_ble_message(data):
    message = data.decode("utf-8")

    parts = message.split(",")

    if len(parts) != 3:
        raise ValueError(
            f"Expected 3 BLE fields, got {len(parts)}"
        )

    if parts[0] != "HELLO":
        raise ValueError(
            f"Unexpected BLE type: {parts[0]}"
        )

    return {
        "sequence": int(parts[1]),
        "timestamp": int(parts[2]),
    }


def notification_handler(characteristic, data):
    global ble_received
    global ble_missing
    global queue_dropped
    global last_ble_sequence

    try:
        sample = parse_ble_message(data)

    except Exception as error:
        print(
            f"[BLE] Invalid notification: {error}"
        )
        return

    sequence = sample["sequence"]

    if last_ble_sequence is not None:
        expected = last_ble_sequence + 1

        if sequence > expected:
            missing = sequence - expected

            ble_missing += missing

            print(
                f"[BLE] Missing {missing} "
                f"notification(s)"
            )

    last_ble_sequence = sequence
    ble_received += 1

    try:
        sensor_queue.put_nowait(sample)

    except asyncio.QueueFull:
        queue_dropped += 1

        print(
            "[QUEUE] FULL - dropping sample "
            f"seq={sequence}"
        )


async def ultra96_sender(
    reader,
    writer
):
    forwarded = 0

    while True:
        sample = await sensor_queue.get()

        sequence = sample["sequence"]
        timestamp = sample["timestamp"]

        # Dummy sensor values for now.
        # The sequence/timestamp are REAL values
        # originating from the physical ESP32.
        ax = 1.1
        ay = 2.2
        az = 9.81

        gx = 0.1
        gy = 0.2
        gz = 0.3

        hall1 = 2000
        hall2 = 2100

        message = (
            f"SENSOR,"
            f"{sequence},"
            f"{timestamp},"
            f"{ax},"
            f"{ay},"
            f"{az},"
            f"{gx},"
            f"{gy},"
            f"{gz},"
            f"{hall1},"
            f"{hall2}\n"
        )

        writer.write(
            message.encode()
        )

        await writer.drain()

        forwarded += 1

        sensor_queue.task_done()

        if forwarded % 100 == 0:
            print(
                f"[FORWARD] Ultra96 <- "
                f"{forwarded} samples "
                f"(seq={sequence})"
            )


async def stats_task():
    previous_received = 0

    while True:
        await asyncio.sleep(5)

        current_received = ble_received

        received_last_5s = (
            current_received
            - previous_received
        )

        rate = received_last_5s / 5

        previous_received = current_received

        print()
        print("========== GATEWAY STATS ==========")
        print(
            f"BLE received:    {ble_received}"
        )
        print(
            f"BLE missing:     {ble_missing}"
        )
        print(
            f"Queue size:      {sensor_queue.qsize()}"
        )
        print(
            f"Queue dropped:   {queue_dropped}"
        )
        print(
            f"BLE rate:        {rate:.1f} Hz"
        )
        print("===================================")
        print()


async def main():

    # --------------------------------
    # ULTRA96 TCP CONNECTION
    # --------------------------------

    print(
        "[GATEWAY] Connecting to Ultra96..."
    )

    reader, writer = await asyncio.open_connection(
        ULTRA96_HOST,
        ULTRA96_PORT
    )

    print(
        "[GATEWAY] TCP connected"
    )


    # --------------------------------
    # HELLO / ACK
    # --------------------------------

    hello = (
        f"HELLO,"
        f"{DEVICE_ID},"
        f"{PROTOCOL_VERSION}\n"
    )

    writer.write(
        hello.encode()
    )

    await writer.drain()

    print(
        "[GATEWAY] HELLO sent"
    )

    response = await reader.readline()

    if not response:
        raise ConnectionError(
            "Ultra96 disconnected during handshake"
        )

    ack = response.decode().strip()

    if ack != f"ACK,{DEVICE_ID}":
        raise RuntimeError(
            f"Unexpected ACK: {ack}"
        )

    print(
        f"[GATEWAY] Handshake successful: {ack}"
    )


    # --------------------------------
    # FIND ESP32
    # --------------------------------

    print(
        f"[BLE] Searching for {DEVICE_NAME}..."
    )

    device = await BleakScanner.find_device_by_name(
        DEVICE_NAME,
        timeout=10
    )

    if device is None:
        raise RuntimeError(
            "CG4002_GLOVE not found"
        )

    print(
        "[BLE] Glove found"
    )


    # --------------------------------
    # CONNECT BLE
    # --------------------------------

    async with BleakClient(device) as client:

        print(
            "[BLE] Connected"
        )

        await client.start_notify(
            SENSOR_CHAR_UUID,
            notification_handler
        )

        print(
            "[BLE] Notifications subscribed"
        )

        sender = asyncio.create_task(
            ultra96_sender(
                reader,
                writer
            )
        )

        stats = asyncio.create_task(
            stats_task()
        )

        print()
        print(
            "[GATEWAY] PIPELINE RUNNING"
        )
        print()

        try:
            while client.is_connected:
                await asyncio.sleep(1)

        finally:
            sender.cancel()
            stats.cancel()

            writer.close()

            await writer.wait_closed()


try:
    asyncio.run(main())

except KeyboardInterrupt:
    print(
        "\n[GATEWAY] Shutting down"
    )

except Exception as error:
    print(
        f"[GATEWAY] ERROR: {error}"
    )