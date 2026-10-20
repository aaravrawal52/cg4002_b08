import asyncio
from email.mime import message
from bleak import BleakScanner, BleakClient
import zlib

SENSOR_CHAR_UUID = (
    "12345678-1234-1234-1234-1234567890ac"
)

DEVICE_NAME = "CG4002_GLOVE_01"

def validate_gesture_event(message):

    last_comma = message.rfind(",")

    if last_comma == -1:
        return None

    payload = message[:last_comma]
    crc_text = message[last_comma + 1:]

    try:
        received_crc = int(crc_text, 16)
    except ValueError:
        return None

    calculated_crc = (
        zlib.crc32(payload.encode("utf-8"))
        & 0xFFFFFFFF
    )

    if received_crc != calculated_crc:

        print(
            f"[CRC] FAILED "
            f"received={received_crc:08X} "
            f"calculated={calculated_crc:08X}"
        )

        return None

    parts = payload.split(",")

    if parts[0] == "LOCAL_GESTURE":

        if len(parts) != 5:
            return None

        try:
            return {
                "type": "LOCAL_GESTURE",
                "device_id": parts[1],
                "event_sequence": int(parts[2]),
                "sensor_sequence": int(parts[3]),
                "gesture": parts[4],
            }
        except ValueError:
            return None

    if parts[0] == "ML_REQUEST":

        if len(parts) != 4:
            return None

        try:
            return {
                "type": "ML_REQUEST",
                "device_id": parts[1],
                "event_sequence": int(parts[2]),
                "sensor_sequence": int(parts[3]),
            }
        except ValueError:
            return None

    return None
async def main():

    print("[BLE] Searching for FireBeetle...")

    devices = await BleakScanner.discover(
        timeout=10,
        return_adv=True
    )

    device = None

    for _, (candidate, advertisement) in devices.items():

        name = (
            advertisement.local_name
            or candidate.name
        )

        if name == DEVICE_NAME:
            device = candidate
            break

    if device is None:
        print("[BLE] FireBeetle not found")
        return

    async with BleakClient(device) as client:

        print("[BLE] Connected")

        received = 0

        def handler(characteristic, data):
            nonlocal received

            message = data.decode(
                "utf-8",
                errors="replace"
            )

            if message.startswith(
                ("LOCAL_GESTURE,", "ML_REQUEST,")
            ):

                event = validate_gesture_event(message)

                if event is None:

                    print(
                        "[GESTURE] Invalid event or CRC mismatch"
                    )

                    return

                print()
                print("========== VERIFIED GESTURE EVENT ==========")

                print(f"Type:           {event['type']}")
                print(f"Device:         {event['device_id']}")
                print(f"Event sequence: {event['event_sequence']}")
                print(f"Sensor seq:     {event['sensor_sequence']}")

                if event["type"] == "LOCAL_GESTURE":

                    print(
                        f"Gesture:        {event['gesture']}"
                    )

                print("CRC32:          VALID")
                print("============================================")

                return

            if not message.startswith("DATA,"):

                return

            parts = message.split(",")

            if len(parts) != 12:
                print("[ERROR] Invalid packet")
                return

            received += 1

            if received % 50 == 0:

                print()
                print(
                    f"[SENSOR] device={parts[1]} "
                    f"seq={parts[2]} "
                    f"timestamp={parts[3]}"
                )

                print(
                    f"  accel=({parts[4]}, "
                    f"{parts[5]}, {parts[6]})"
                )

                print(
                    f"  gyro=({parts[7]}, "
                    f"{parts[8]}, {parts[9]})"
                )

                print(
                    f"  hall=({parts[10]}, "
                    f"{parts[11]})"
                )

        await client.start_notify(
            SENSOR_CHAR_UUID,
            handler
        )

        await client.write_gatt_char(
            "12345678-1234-1234-1234-1234567890ad",
            b"CONTROL,START_STREAM",
            response=True
        )

        print("[BLE] Receiving real sensor data...")

        await asyncio.sleep(20)

        await client.write_gatt_char(
            "12345678-1234-1234-1234-1234567890ad",
            b"CONTROL,STOP_STREAM",
            response=True
        )

        print("[BLE] Test complete")


asyncio.run(main())