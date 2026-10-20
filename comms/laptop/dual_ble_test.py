import asyncio
import time
from dataclasses import dataclass

from bleak import BleakClient, BleakScanner


# ============================================================
# CONFIGURATION
# ============================================================

SENSOR_CHAR_UUID = (
    "12345678-1234-1234-1234-1234567890ac"
)

COMMAND_CHAR_UUID = (
    "12345678-1234-1234-1234-1234567890ad"
)

TARGETS = {
    "GLOVE_01": "CG4002_GLOVE_01",
    "GLOVE_02": "CG4002_GLOVE_02",
}

TEST_DURATION = 20
STATS_INTERVAL = 5

# Print one representative packet per 100 received packets.
PACKET_PRINT_INTERVAL = 100


# ============================================================
# PER-DEVICE STATISTICS
# ============================================================

@dataclass
class GloveStats:
    device_id: str

    received: int = 0
    missing: int = 0
    out_of_order: int = 0
    bytes_received: int = 0

    last_sequence: int | None = None

    connected: bool = False

    # Measurement baseline
    previous_received: int = 0
    previous_bytes: int = 0

    def reset(self):
        self.received = 0
        self.missing = 0
        self.out_of_order = 0
        self.bytes_received = 0
        self.last_sequence = None
        self.previous_received = 0
        self.previous_bytes = 0


stats = {
    device_id: GloveStats(device_id)
    for device_id in TARGETS
}


# ============================================================
# BLE DISCOVERY
# ============================================================

async def discover_gloves():

    print("[BLE] Scanning for both FireBeetles...")

    discovered = await BleakScanner.discover(
        timeout=15,
        return_adv=True
    )

    found = {}

    for _, (device, advertisement) in discovered.items():

        # Use actual advertised name because macOS may
        # cache the old peripheral name.
        name = advertisement.local_name or device.name

        for device_id, target_name in TARGETS.items():

            if name == target_name:

                found[device_id] = device

                print(
                    f"[FOUND] {device_id} "
                    f"name={name}"
                )

    return found


# ============================================================
# NOTIFICATION HANDLER
# ============================================================

def make_notification_handler(device_id):

    def handler(characteristic, data):

        message = data.decode(
            "utf-8",
            errors="replace"
        )

        if not message.startswith("DATA,"):
            return

        parts = message.split(",")

        # DATA,device,seq,timestamp,
        # ax,ay,az,gx,gy,gz,hall1,hall2
        if len(parts) != 12:
            return

        try:
            packet_device = parts[1]
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

        except ValueError:
            return

        if packet_device != device_id:
            print(
                f"[WARNING] Device mismatch: "
                f"expected={device_id} "
                f"received={packet_device}"
            )
            return

        s = stats[device_id]

        # Sequence tracking
        if s.last_sequence is not None:

            expected = s.last_sequence + 1

            if sequence > expected:
                s.missing += sequence - expected

            elif sequence < expected:
                s.out_of_order += 1

        s.last_sequence = sequence

        s.received += 1
        s.bytes_received += len(data)

        # Human-readable representative packet
        if s.received % PACKET_PRINT_INTERVAL == 0:

            print(
                f"\n[{device_id}] DATA "
                f"seq={sequence} "
                f"timestamp={timestamp}"
            )

            print(
                f"  accel=({ax:.2f}, {ay:.2f}, {az:.2f})"
            )

            print(
                f"  gyro=({gx:.2f}, {gy:.2f}, {gz:.2f})"
            )

            print(
                f"  hall=({hall1}, {hall2})"
            )

    return handler


# ============================================================
# PER-DEVICE BLE CONNECTION
# ============================================================

async def connect_glove(device_id, device, ready_event, stop_event):

    print(f"[{device_id}] Connecting...")

    async with BleakClient(device) as client:

        stats[device_id].connected = True

        print(f"[{device_id}] Connected")

        try:
            await client.start_notify(
                SENSOR_CHAR_UUID,
                make_notification_handler(device_id)
            )

            print(
                f"[{device_id}] Notifications subscribed"
            )

            # Stop any streaming left active from an
            # earlier BLE connection.
            await client.write_gatt_char(
                COMMAND_CHAR_UUID,
                b"CONTROL,STOP_STREAM",
                response=True
            )

            ready_event.set()

            # Keep this connection alive while the
            # benchmark controller runs.
            await stop_event.wait()

        finally:

            if client.is_connected:
                try:
                    await client.write_gatt_char(
                        COMMAND_CHAR_UUID,
                        b"CONTROL,STOP_STREAM",
                        response=True
                    )

                except Exception as error:
                    print(
                        f"[{device_id}] Stop failed: {error}"
                    )

            stats[device_id].connected = False

            print(f"[{device_id}] Disconnected")


# ============================================================
# STATISTICS
# ============================================================

def print_statistics(elapsed):

    print()
    print("=" * 52)
    print(f" DUAL BLE STATISTICS | elapsed={elapsed:.1f}s")
    print("=" * 52)

    combined_rate = 0
    combined_kbps = 0
    combined_received = 0
    combined_missing = 0

    for device_id, s in stats.items():

        delta_received = (
            s.received - s.previous_received
        )

        delta_bytes = (
            s.bytes_received - s.previous_bytes
        )

        rate = delta_received / STATS_INTERVAL

        kbps = (
            delta_bytes * 8
            / STATS_INTERVAL
            / 1000
        )

        s.previous_received = s.received
        s.previous_bytes = s.bytes_received

        combined_rate += rate
        combined_kbps += kbps
        combined_received += s.received
        combined_missing += s.missing

        print()
        print(f"{device_id}")
        print(f"  Connected:    {s.connected}")
        print(f"  Received:     {s.received}")
        print(f"  Missing:      {s.missing}")
        print(f"  Out of order: {s.out_of_order}")
        print(f"  Rate:         {rate:.1f} Hz")
        print(f"  Throughput:   {kbps:.2f} kbps")

    print()
    print("COMBINED")
    print(f"  Received:     {combined_received}")
    print(f"  Missing:      {combined_missing}")
    print(f"  Rate:         {combined_rate:.1f} Hz")
    print(f"  Throughput:   {combined_kbps:.2f} kbps")
    print("=" * 52)
    print()


# ============================================================
# BENCHMARK CONTROLLER
# ============================================================

async def run_benchmark(mode, clients):

    print()
    print("=" * 52)
    print(f" STARTING {mode.upper()} BENCHMARK")
    print("=" * 52)

    # Ensure both streams are stopped before starting.
    await asyncio.gather(
        *[
            client.write_gatt_char(
                COMMAND_CHAR_UUID,
                b"CONTROL,STOP_STREAM",
                response=True
            )
            for client in clients.values()
        ]
    )

    await asyncio.sleep(1)

    # Reset all measurement counters.
    for s in stats.values():
        s.reset()

    command = (
        b"CONTROL,START_STREAM"
        if mode == "normal"
        else b"CONTROL,MAX_STREAM"
    )

    # Start both devices concurrently.
    await asyncio.gather(
        *[
            client.write_gatt_char(
                COMMAND_CHAR_UUID,
                command,
                response=True
            )
            for client in clients.values()
        ]
    )

    print("[BENCHMARK] Both streams started")

    start_time = time.monotonic()

    for interval in range(
        TEST_DURATION // STATS_INTERVAL
    ):

        await asyncio.sleep(STATS_INTERVAL)

        elapsed = time.monotonic() - start_time

        print_statistics(elapsed)

    # Stop both streams.
    await asyncio.gather(
        *[
            client.write_gatt_char(
                COMMAND_CHAR_UUID,
                b"CONTROL,STOP_STREAM",
                response=True
            )
            for client in clients.values()
        ]
    )

    print(f"[BENCHMARK] {mode.upper()} completed")


# ============================================================
# MAIN
# ============================================================

async def main():

    found = await discover_gloves()

    if len(found) != 2:
        print(
            f"[ERROR] Found {len(found)}/2 FireBeetles"
        )
        return

    clients = {}

    try:

        # Establish both BLE connections.
        for device_id, device in found.items():

            client = BleakClient(device)

            await client.connect()

            clients[device_id] = client

            stats[device_id].connected = True

            print(f"[{device_id}] Connected")

            await client.start_notify(
                SENSOR_CHAR_UUID,
                make_notification_handler(device_id)
            )

            print(
                f"[{device_id}] Notifications subscribed"
            )

        # Run normal benchmark.
        await run_benchmark(
            "normal",
            clients
        )

        await asyncio.sleep(2)

        # Run maximum-speed benchmark.
        await run_benchmark(
            "max",
            clients
        )

    finally:

        for device_id, client in clients.items():

            if client.is_connected:

                try:
                    await client.write_gatt_char(
                        COMMAND_CHAR_UUID,
                        b"CONTROL,STOP_STREAM",
                        response=True
                    )

                except Exception:
                    pass

                await client.disconnect()

            print(f"[{device_id}] Disconnected")

    print()
    print("[BENCHMARK] All tests completed")


asyncio.run(main())