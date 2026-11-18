import asyncio
import json
import time

import websockets


HOST = "0.0.0.0"
PORT = 8765


async def handle_phone(websocket):
    print("[PHONE] Connected")

    sequence = 0

    try:
        while True:
            message = {
                "type": "GESTURE",
                "sequence": sequence,
                "gesture": "SWIPE_LEFT",
                "confidence": 0.94,
                "timestamp": int(time.time() * 1000),
            }

            await websocket.send(
                json.dumps(message)
            )

            print(
                f"[PHONE] Sent gesture "
                f"seq={sequence}"
            )

            sequence += 1

            await asyncio.sleep(2)

    except websockets.ConnectionClosed:
        print("[PHONE] Disconnected")


async def main():
    print(
        f"[PHONE] WebSocket server "
        f"listening on {HOST}:{PORT}"
    )

    async with websockets.serve(
        handle_phone,
        HOST,
        PORT
    ):
        await asyncio.Future()


try:
    asyncio.run(main())

except KeyboardInterrupt:
    print("\n[PHONE] Server stopped")