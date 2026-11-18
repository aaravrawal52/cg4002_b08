import asyncio
import random
import time

from bleak import BleakClient
from bleak import BleakScanner


DEVICE_NAME = "CG4002_GLOVE"

SENSOR_CHAR_UUID = (
    "12345678-1234-1234-1234-1234567890ac"
)

COMMAND_CHAR_UUID = (
    "12345678-1234-1234-1234-1234567890ad"
)


sequence = 0


# ============================================================
# REALISTIC DUMMY SENSOR PACKETS
# ============================================================

DUMMY_SAMPLES = [
    {
        "ax": 1.20,
        "ay": -0.40,
        "az": 9.71,
        "gx": 0.10,
        "gy": 0.05,
        "gz": -0.02,
        "hall1": 2050,
        "hall2": 2110,
    },

    {
        "ax": -2.40,
        "ay": 3.10,
        "az": 8.90,
        "gx": -0.30,
        "gy": 0.80,
        "gz": 0.15,
        "hall1": 2400,
        "hall2": 2180,
    },

    {
        "ax": 4.50,
        "ay": 0.20,
        "az": 7.80,
        "gx": 1.20,
        "gy": -0.40,
        "gz": 0.60,
        "hall1": 2700,
        "hall2": 2600,
    },
]


def notification_handler(characteristic, data):

    message = data.decode(
        "utf-8",
        errors="replace"
    )

    # Ignore periodic DATA packets for now.
    if message.startswith("DATA,"):
        return

    print()
    print(
        f"[FIREBEETLE -> MAC] {message}"
    )


def make_dummy_packet():

    global sequence

    sample = random.choice(
        DUMMY_SAMPLES
    )

    timestamp = int(
        time.time() * 1000
    )

    packet = (
        f"DATA,"
        f"GLOVE_01,"
        f"{sequence},"
        f"{timestamp},"
        f"{sample['ax']},"
        f"{sample['ay']},"
        f"{sample['az']},"
        f"{sample['gx']},"
        f"{sample['gy']},"
        f"{sample['gz']},"
        f"{sample['hall1']},"
        f"{sample['hall2']}"
    )

    sequence += 1

    return packet


async def main():

    print(
        "[MAC] Searching for FireBeetle..."
    )

    device = await BleakScanner.find_device_by_name(
        DEVICE_NAME,
        timeout=10
    )

    if device is None:
        print(
            "[MAC] FireBeetle not found"
        )
        return

    print(
        "[MAC] Found FireBeetle"
    )

    async with BleakClient(device) as client:

        print(
            "[MAC] Connected"
        )

        await client.start_notify(
            SENSOR_CHAR_UUID,
            notification_handler
        )

        print(
            "[MAC] Notifications enabled"
        )

        print()
        print(
            "Press ENTER to send a random dummy packet."
        )

        print(
            "Type q + ENTER to quit."
        )

        print()

        while True:

            user_input = await asyncio.to_thread(
                input,
                "SEND> "
            )

            if user_input.lower() == "q":
                break

            packet = make_dummy_packet()

            print()
            print(
                f"[MAC -> FIREBEETLE]"
            )

            print(packet)

            await client.write_gatt_char(
                COMMAND_CHAR_UUID,
                packet.encode(),
                response=True
            )


asyncio.run(main())