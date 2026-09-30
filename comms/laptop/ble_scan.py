import asyncio

from bleak import BleakScanner


async def main():
    print("[SCAN] Scanning for BLE devices...")
    print()

    devices = await BleakScanner.discover(
        timeout=10.0,
        return_adv=True
    )

    found_glove = False

    for device, advertisement in devices.values():
        name = (
            advertisement.local_name
            or device.name
            or "Unknown"
        )

        print(
            f"[SCAN] {name}"
        )

        if name == "CG4002_GLOVE":
            found_glove = True

            print()
            print(
                "=== CG4002 GLOVE FOUND ==="
            )

            print(
                f"Name: {name}"
            )

            print(
                f"Address: {device.address}"
            )

            print(
                f"RSSI: {advertisement.rssi}"
            )

            print(
                "=========================="
            )

            print()

    if not found_glove:
        print(
            "[SCAN] CG4002_GLOVE not found"
        )


asyncio.run(main())