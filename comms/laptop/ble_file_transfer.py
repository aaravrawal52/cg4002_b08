import asyncio
import hashlib
import time

from pathlib import Path

from bleak import BleakClient
from bleak import BleakScanner


DEVICE_NAME = "CG4002_GLOVE"

SENSOR_CHAR_UUID = (
    "12345678-1234-1234-1234-1234567890ac"
)

COMMAND_CHAR_UUID = (
    "12345678-1234-1234-1234-1234567890ad"
)
FILE_PATH = Path(
    "comms/laptop/test_data.txt"
)

file_result = None


def notification_handler(characteristic, data):
    global file_result

    message = data.decode(
        "utf-8",
        errors="replace"
    )

    if not message.startswith(
        "FILE_RESULT,"
    ):
        return

    parts = message.split(",")

    if len(parts) != 3:
        return

    file_result = {
        "bytes": int(parts[1]),
        "md5": parts[2].lower(),
    }


async def main():

    global file_result

    # --------------------------------
    # TEST FILE
    # --------------------------------

    # ASCII only for this first test because our current
    # FILE_DATA framing uses text.
    if not FILE_PATH.exists():

        print(
            f"[FILE] ERROR: "
            f"{FILE_PATH} does not exist"
        )

        return


# Read actual file bytes from disk.
    file_data = FILE_PATH.read_bytes()

    expected_size = len(file_data)

    expected_md5 = hashlib.md5(
        file_data
    ).hexdigest()


    print(
        f"[FILE] File: {FILE_PATH}"
    )

    print(
        f"[FILE] Size: "
        f"{expected_size} bytes"
    )

    print(
        f"[FILE] Original MD5: "
        f"{expected_md5}"
    )

    # --------------------------------
    # FIND FIREBEETLE
    # --------------------------------

    print(
        "[BLE] Searching for FireBeetle..."
    )

    device = await BleakScanner.find_device_by_name(
        DEVICE_NAME,
        timeout=10
    )

    if device is None:
        print(
            "[BLE] FireBeetle not found"
        )
        return


    # --------------------------------
    # CONNECT
    # --------------------------------

    async with BleakClient(device) as client:

        print(
            "[BLE] Connected"
        )

        await client.start_notify(
            SENSOR_CHAR_UUID,
            notification_handler
        )


        # --------------------------------
        # FILE START
        # --------------------------------

        start_message = (
            f"FILE_START,{expected_size}"
        )

        await client.write_gatt_char(
            COMMAND_CHAR_UUID,
            start_message.encode(),
            response=True
        )

        print(
            "[FILE] FILE_START sent"
        )


        # --------------------------------
        # FILE DATA
        # --------------------------------

        # Keep chunks deliberately small for the first test.
        CHUNK_SIZE = 100

        start_time = time.monotonic()

        sent = 0

        for offset in range(
            0,
            expected_size,
            CHUNK_SIZE
        ):

            chunk = file_data[
                offset:
                offset + CHUNK_SIZE
            ]

            message = (
                b"FILE_DATA,"
                + chunk
            )

            await client.write_gatt_char(
                COMMAND_CHAR_UUID,
                message,
                response=True
            )

            sent += len(chunk)


        # --------------------------------
        # FILE END
        # --------------------------------

        await client.write_gatt_char(
            COMMAND_CHAR_UUID,
            b"FILE_END",
            response=True
        )

        elapsed = (
            time.monotonic()
            - start_time
        )

        throughput_kbps = (
            sent
            * 8
            / elapsed
            / 1000
        )

        print(
            f"[FILE] Sent {sent} bytes"
        )

        print(
            f"[FILE] Time: {elapsed:.3f} s"
        )

        print(
            f"[FILE] Throughput: "
            f"{throughput_kbps:.2f} kbps"
        )


        # --------------------------------
        # WAIT FOR RESULT
        # --------------------------------

        for _ in range(50):

            if file_result is not None:
                break

            await asyncio.sleep(0.1)


        if file_result is None:

            print(
                "[FILE] ERROR: No verification "
                "response received"
            )

            return


        # --------------------------------
        # VERIFY
        # --------------------------------

        print()
        print(
            "========== FILE VERIFICATION =========="
        )

        print(
            f"File: {FILE_PATH.name}"
        )

        print(
            f"Expected bytes: "
            f"{expected_size}"
        )

        print(
            f"Received bytes: "
            f"{file_result['bytes']}"
        )

        print(
            f"Original MD5: "
            f"{expected_md5}"
        )

        print(
            f"Received MD5: "
            f"{file_result['md5']}"
        )


        if (
            file_result["bytes"]
            == expected_size
            and
            file_result["md5"]
            == expected_md5
        ):

            print(
                "Result: VERIFIED"
            )

        else:

            print(
                "Result: FAILED"
            )


        print(
            "======================================="
        )


asyncio.run(main())