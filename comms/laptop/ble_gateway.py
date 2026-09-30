import asyncio
import random
import time
import zlib
from bleak import BleakClient
from bleak import BleakScanner
import json
import websockets


DEVICE_NAME = "CG4002_GLOVE"
COMMAND_CHAR_UUID = (
    "12345678-1234-1234-1234-1234567890ad"
)

SENSOR_CHAR_UUID = (
    "12345678-1234-1234-1234-1234567890ac"
)

ULTRA96_HOST = "127.0.0.1"
ULTRA96_PORT = 5001
PHONE_WS_HOST = "0.0.0.0"
PHONE_WS_PORT = 8765

DEVICE_ID = "GLOVE_01"
PROTOCOL_VERSION = 1

DUMMY_SAMPLES = [
    {
        "ax": 1.20, "ay": -0.40, "az": 9.71,
        "gx": 0.10, "gy": 0.05, "gz": -0.02,
        "hall1": 2050, "hall2": 2110,
    },
    {
        "ax": -2.40, "ay": 3.10, "az": 8.90,
        "gx": -0.30, "gy": 0.80, "gz": 0.15,
        "hall1": 2400, "hall2": 2180,
    },
    {
        "ax": 4.50, "ay": 0.20, "az": 7.80,
        "gx": 1.20, "gy": -0.40, "gz": 0.60,
        "hall1": 2700, "hall2": 2600,
    },
]

command_sequence = 0

sensor_queue = asyncio.Queue(maxsize=500)
gesture_queue = asyncio.Queue(maxsize=50)
phone_clients = set() #for multiple phones

ble_received = 0
ble_missing = 0
queue_dropped = 0
ultra96_forwarded = 0
ble_bytes_received = 0

last_ble_sequence = None

def handle_stream_packet(message, byte_count):
    global ble_received
    global ble_missing
    global ble_bytes_received
    global last_ble_sequence

    try:
        parts = message.split(",")

        # DATA
        # GLOVE_01
        # sequence
        # timestamp
        # ax ay az
        # gx gy gz
        # hall1 hall2
        #
        # Total = 12 fields

        if len(parts) != 12:
            print(
                f"[STREAM] Invalid packet "
                f"fields={len(parts)}"
            )
            return

        if parts[0] != "DATA":
            return

        device_id = parts[1]
        sequence = int(parts[2])
        timestamp = int(parts[3])

        ax = float(parts[4])
        ay = float(parts[5])
        az = float(parts[6])

        gx = float(parts[7])
        gy = float(parts[8])
        gz = float(parts[9])

        hall1 = int(parts[10])
        hall2 = int(parts[11])

        if last_ble_sequence is not None:
            expected = last_ble_sequence + 1

            if sequence > expected:
                missing = sequence - expected

                ble_missing += missing

                print(
                    f"[STREAM] Missing "
                    f"{missing} packet(s)"
                )

        last_ble_sequence = sequence

        ble_received += 1
        ble_bytes_received += byte_count

    except ValueError as error:
        print(
            f"[STREAM] Parse error: {error}"
        )
    
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
    global queue_dropped

    try:
        message = data.decode(
            "utf-8",
            errors="replace"
        )

        # Ignore the periodic FireBeetle DATA stream
        # for this keyboard pipeline test.
        if message.startswith("DATA,"):
            handle_stream_packet(
                message,
                len(data)
            )

            return

        if not message.startswith("RESPONSE,DATA,"):
            print(
                f"[BLE] Unknown packet: {message}"
            )
            return

        parts = message.split(",")

        # RESPONSE,
        # DATA,
        # GLOVE_01,
        # sequence,
        # timestamp,
        # ax ay az,
        # gx gy gz,
        # hall1 hall2,
        # MODIFIED_BY_GLOVE

        if len(parts) != 15:
            print(
                f"[BLE] Invalid response "
                f"fields={len(parts)}"
            )
            return
        last_comma = message.rfind(",")

        if last_comma == -1:
            print(
                "[CRC] Invalid response format"
            )
            return


        message_without_crc = (
            message[:last_comma]
        )

        received_crc_text = (
            message[last_comma + 1:]
        )


        try:
            received_crc = int(
                received_crc_text,
                16
            )

        except ValueError:
            print(
                "[CRC] Invalid CRC field"
            )
            return


        calculated_crc = calculate_crc32(
            message_without_crc
        )


        if received_crc != calculated_crc:

            print(
                f"[CRC] FAILED "
                f"received={received_crc:08X} "
                f"calculated={calculated_crc:08X}"
            )

            print(
                "[PACKET] Corrupted response dropped"
            )

            return


        print(
            f"[CRC] OK seq={parts[3]}"
        )

        sample = {
            "device_id": parts[2],
            "sequence": int(parts[3]),
            "timestamp": int(parts[4]),

            "ax": float(parts[5]),
            "ay": float(parts[6]),
            "az": float(parts[7]),

            "gx": float(parts[8]),
            "gy": float(parts[9]),
            "gz": float(parts[10]),

            "hall1": int(parts[11]),
            "hall2": int(parts[12]),
        }

        ble_received += 1

        print(
            f"[GLOVE -> MAC] "
            f"device={sample['device_id']} "
            f"seq={sample['sequence']} "
            f"modified=YES"
        )

        try:
            sensor_queue.put_nowait(sample)

        except asyncio.QueueFull:
            queue_dropped += 1

            print(
                f"[QUEUE] FULL - dropping "
                f"seq={sample['sequence']}"
            )

    except Exception as error:
        print(
            f"[BLE] Parse error: {error}"
        )


async def ultra96_sender(writer):
    global ultra96_forwarded

    while True:
        sample = await sensor_queue.get()

        sequence = sample["sequence"]
        timestamp = sample["timestamp"]


        message = (

            f"SENSOR,"

            f"{sample['sequence']},"

            f"{sample['timestamp']},"

            f"{sample['ax']},"

            f"{sample['ay']},"

            f"{sample['az']},"

            f"{sample['gx']},"

            f"{sample['gy']},"

            f"{sample['gz']},"

            f"{sample['hall1']},"

            f"{sample['hall2']}\n"

        )

        if ultra96_forwarded % 100 == 0:
            '''print(
                f"[DEBUG] About to write seq={sequence}"
            )'''

        writer.write(
            message.encode()
        )

        if ultra96_forwarded % 100 == 0:
            '''print(
                f"[DEBUG] Write done seq={sequence}, "
                f"waiting for drain"
            )'''

        await writer.drain()

        if ultra96_forwarded % 100 == 0:
            '''print(
                f"[DEBUG] Drain done seq={sequence}"
            )'''

        ultra96_forwarded += 1

        sensor_queue.task_done()


async def stats_task():
    previous_bytes = 0
    previous_received = 0
    previous_forwarded = 0

    while True:
        await asyncio.sleep(5)

        current_received = ble_received
        current_bytes = ble_bytes_received

        bytes_last_5s = (
            current_bytes - previous_bytes
        )

        kbps = (
            bytes_last_5s
            * 8
            / 5
            / 1000
        )

        previous_bytes = current_bytes

        received_last_5s = (
            current_received
            - previous_received
        )

        rate = received_last_5s / 5

        previous_received = current_received
        current_forwarded = ultra96_forwarded

        forwarded_last_5s = (
            current_forwarded
            - previous_forwarded
        )

        forward_rate = (
            forwarded_last_5s / 5
        )

        previous_forwarded = current_forwarded

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
        print(
            f"Forward rate:    {forward_rate:.1f} Hz"
        )
        print(
            f"BLE throughput:  {kbps:.2f} kbps"
        )
        print("===================================")
        print()

async def ultra96_receiver(reader):
    print(
        "[RECEIVER] Waiting for Ultra96 messages..."
    )

    while True:
        data = await reader.readline()

        if not data:
            raise ConnectionError(
                "Ultra96 disconnected"
            )

        message = data.decode().strip()

        parts = message.split(",")

        if (
            len(parts) == 4
            and parts[0] == "GESTURE"
        ):
            try:
                gesture = {
                    "sequence": int(parts[1]),
                    "gesture": parts[2],
                    "confidence": float(parts[3]),
                }

            except ValueError:
                print(
                    f"[RECEIVER] Invalid gesture: "
                    f"{message}"
                )
                continue

            print(
                f"[RECEIVER] Gesture received: "
                f"seq={gesture['sequence']} "
                f"gesture={gesture['gesture']} "
                f"confidence={gesture['confidence']}"
            )

            try:
                gesture_queue.put_nowait(
                    gesture
                )

            except asyncio.QueueFull:
                print(
                    "[GESTURE QUEUE] FULL"
                )

        else:
            print(
                f"[RECEIVER] Unknown message: "
                f"{message}"
            )
async def phone_handler(websocket):
    phone_clients.add(websocket)

    print(
        f"[PHONE] Connected "
        f"(clients={len(phone_clients)})"
    )

    try:
        await websocket.wait_closed()

    finally:
        phone_clients.discard(websocket)

        print(
            f"[PHONE] Disconnected "
            f"(clients={len(phone_clients)})"
        )
async def phone_sender():
    print(
        "[PHONE] Gesture sender ready"
    )

    while True:
        gesture = await gesture_queue.get()

        message = {
            "type": "GESTURE",
            "sequence": gesture["sequence"],
            "gesture": gesture["gesture"],
            "confidence": gesture["confidence"],
        }

        encoded = json.dumps(message)

        if not phone_clients:
            print(
                f"[PHONE] No client connected - "
                f"gesture seq={gesture['sequence']} "
                f"not delivered"
            )

            gesture_queue.task_done()
            continue

        disconnected = []

        for websocket in phone_clients.copy():
            try:
                await websocket.send(
                    encoded
                )

            except websockets.ConnectionClosed:
                disconnected.append(
                    websocket
                )

        for websocket in disconnected:
            phone_clients.discard(
                websocket
            )

        print(
            f"[PHONE] Sent "
            f"seq={gesture['sequence']} "
            f"gesture={gesture['gesture']} "
            f"confidence={gesture['confidence']}"
        )

        gesture_queue.task_done()


def calculate_crc32(text):
    return (
        zlib.crc32(
            text.encode("utf-8")
        )
        & 0xFFFFFFFF
    )

async def keyboard_sender(client):
    global command_sequence

    print()
    print("========== COMMANDS ==========")
    print("ENTER : send pipeline packet")
    print("s     : start 100 Hz stream")
    print("m     : maximum-speed stream")
    print("x     : stop stream")
    print("q     : stop keyboard input")
    print("==============================")
    print()

    while True:
        text = await asyncio.to_thread(
            input,
            "SEND> "
        )
        if text.lower() == "s":
            await client.write_gatt_char(
                COMMAND_CHAR_UUID,
                b"CONTROL,START_STREAM",
                response=True
            )

            print(
                "[STREAM] Started 100 Hz stream"
            )
            continue

        if text.lower() == "x":
            await client.write_gatt_char(
                COMMAND_CHAR_UUID,
                b"CONTROL,STOP_STREAM",
                response=True
            )

            print(
                "[STREAM] Stopped"
            )
            continue

        if text.lower() == "q":
            print("[KEYBOARD] Input stopped")
            return
        if text.lower() == "m":
            await client.write_gatt_char(
                COMMAND_CHAR_UUID,
                b"CONTROL,MAX_STREAM",
                response=True
            )

            print(
                "[STREAM] Maximum-speed stream started"
            )
            continue

        sample = random.choice(DUMMY_SAMPLES)

        timestamp = int(time.time() * 1000)

        packet = (
            f"DATA,"
            f"GLOVE_01,"
            f"{command_sequence},"
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
        crc = calculate_crc32(
            packet
        )

        # Reliability demo:
        # 'c' deliberately sends an incorrect CRC.
        if text.lower() == "c":

            bad_crc = crc ^ 0x00000001

            packet = (
                f"{packet},"
                f"{bad_crc:08X}"
            )

            print(
                f"[TEST] Sending intentionally "
                f"corrupted packet seq={command_sequence}"
            )

        else:

            packet = (
                f"{packet},"
                f"{crc:08X}"
            )

        await client.write_gatt_char(
            COMMAND_CHAR_UUID,
            packet.encode(),
            response=True
        )

        print(
            f"[MAC -> GLOVE] "
            f"seq={command_sequence}"
        )

        command_sequence += 1
async def ble_connection_manager():
    while True:

        print(
            f"[BLE] Searching for {DEVICE_NAME}..."
        )

        device = await BleakScanner.find_device_by_name(
            DEVICE_NAME,
            timeout=10
        )

        if device is None:
            print(
                "[BLE] Glove not found. "
                "Retrying in 2 seconds..."
            )

            await asyncio.sleep(2)
            continue

        print("[BLE] Glove found")

        try:

            async with BleakClient(device) as client:

                print("[BLE] Connected")

                await client.start_notify(
                    SENSOR_CHAR_UUID,
                    notification_handler
                )

                print(
                    "[BLE] Notifications subscribed"
                )

                keyboard = asyncio.create_task(
                    keyboard_sender(client)
                )

                print(
                    "[BLE] FireBeetle ready"
                )

                while client.is_connected:
                    await asyncio.sleep(1)

                print(
                    "[BLE] FireBeetle disconnected"
                )

                keyboard.cancel()

        except Exception as error:

            print(
                f"[BLE] Connection lost: {error}"
            )

        print(
            "[BLE] Reconnecting in 2 seconds..."
        )

        await asyncio.sleep(2)

async def main():

    # --------------------------------
    # ULTRA96 TCP CONNECTION
    # --------------------------------
    phone_server = await websockets.serve(
    phone_handler,
    PHONE_WS_HOST,
    PHONE_WS_PORT
    )

    print(
        f"[PHONE] WebSocket server listening "
        f"on {PHONE_WS_HOST}:{PHONE_WS_PORT}"
    )

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


    sender = asyncio.create_task(
        ultra96_sender(writer)
    )

    receiver = asyncio.create_task(
        ultra96_receiver(reader)
    )

    phone = asyncio.create_task(
        phone_sender()
    )

    stats = asyncio.create_task(
        stats_task()
    )

    ble_manager = asyncio.create_task(
        ble_connection_manager()
    )

    print()
    print(
        "[GATEWAY] PIPELINE RUNNING"
    )
    print()

    try:
        await asyncio.gather(
            sender,
            receiver,
            phone,
            stats,
            ble_manager,
        )

    finally:

        sender.cancel()
        receiver.cancel()
        phone.cancel()
        stats.cancel()
        ble_manager.cancel()

        phone_server.close()

        await phone_server.wait_closed()

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