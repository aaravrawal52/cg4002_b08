import asyncio
import time

from bleak import BleakClient
from bleak import BleakScanner


DEVICE_NAME = "CG4002_GLOVE"

SENSOR_CHAR_UUID = (
    "12345678-1234-1234-1234-1234567890ac"
)


received_count = 0
start_time = None
last_sequence = None
missing_count = 0


def notification_handler(
    characteristic,
    data
):
    global received_count
    global start_time
    global last_sequence
    global missing_count

    try:
        message = data.decode("utf-8")

        parts = message.split(",")

        if (
            len(parts) != 3
            or parts[0] != "HELLO"
        ):
            print(
                f"[BLE] Invalid message: "
                f"{message}"
            )

            return

        sequence = int(parts[1])
        esp32_timestamp = int(parts[2])

        if start_time is None:
            start_time = time.monotonic()

        if last_sequence is not None:
            expected = last_sequence + 1

            if sequence > expected:
                missing = sequence - expected
                missing_count += missing

                print(
                    f"[BLE] Missing "
                    f"{missing} notification(s)"
                )

            elif sequence < expected:
                print(
                    f"[BLE] Out-of-order: "
                    f"expected={expected}, "
                    f"received={sequence}"
                )

        last_sequence = sequence

        received_count += 1

        if received_count % 10 == 0:
            elapsed = (
                time.monotonic()
                - start_time
            )

            rate = (
                received_count / elapsed
                if elapsed > 0
                else 0
            )

            print(
                f"[BLE] received="
                f"{received_count} "
                f"seq={sequence} "
                f"esp_time="
                f"{esp32_timestamp} "
                f"missing="
                f"{missing_count} "
                f"rate={rate:.1f} Hz"
            )

    except Exception as error:
        print(
            f"[BLE] Parse error: {error}"
        )


async def main():
    print(
        f"[BLE] Searching for "
        f"{DEVICE_NAME}..."
    )

    device = await BleakScanner.find_device_by_name(
        DEVICE_NAME,
        timeout=10.0
    )

    if device is None:
        print(
            "[BLE] Glove not found"
        )

        return

    print(
        f"[BLE] Found {DEVICE_NAME}"
    )

    print(
        "[BLE] Connecting..."
    )

    async with BleakClient(device) as client:
        print(
            "[BLE] Connected"
        )

        await client.start_notify(
            SENSOR_CHAR_UUID,
            notification_handler
        )

        print(
            "[BLE] Subscribed to "
            "sensor notifications"
        )

        print(
            "[BLE] Press Ctrl+C to stop"
        )

        while client.is_connected:
            await asyncio.sleep(1)


try:
    asyncio.run(main())

except KeyboardInterrupt:
    print(
        "\n[BLE] Stopped"
    )