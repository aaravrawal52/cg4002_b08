# cg4002_b08
AR computer code

## How the system fits together

```
Gloves (FireBeetle ESP32) --BLE--> Mac gateway --SSH tunnel--> Ultra96 (DPU models)
                                        |
                                        +--WebSocket over Tailscale--> Phone visualiser
```

- **GLOVE_01** is the right hand (flex sensors). **GLOVE_02** is the left hand (Hall sensors).
- The Mac runs `comms/laptop/ble_gateway.py`. It is the centre of the system: it talks to both gloves, the Ultra96 and the phone.
- The Ultra96 runs `comms/ultra96/ultra96_server.py`, which classifies gestures on the DPU and sends the results back to the Mac.
- The phone runs the Unity visualiser (`visualiser/v3`), which receives gestures from the Mac.

## Starting the whole system

Start everything in this order. The Ultra96 server must be running before the gateway starts, because the gateway connects to it straight away.

### 0. One-time setup

- **Mac:** install the Python packages the gateway needs:

  ```bash
  pip install bleak websockets
  ```

- **Mac and phone:** install Tailscale and sign both in to the team's Tailscale network.
- **Phone app:** in Unity, set the Mac's Tailscale IP (`100.x.x.x`) on the `GestureWebSocketReceiver` component (field `macTailscaleIP`, port `8765`), then build the app to the phone. Run `tailscale ip -4` on the Mac to find this IP.
- **Gloves:** the gloves must already be flashed (`Firebeetle/Flex Sensor Ver` for GLOVE_01 and `Firebeetle/Hall Sensor Ver` for GLOVE_02).

### 1. Turn on both gloves

Power on GLOVE_01 and GLOVE_02. They start advertising over BLE as `CG4002_GLOVE_01` and `CG4002_GLOVE_02`.

### 2. Start the Ultra96 server (Terminal 1 on the Mac)

Open an SSH session to the Ultra96 through the NUS jump host. The `-L` option also creates the tunnel that the gateway uses (Mac `localhost:5001` → Ultra96 `127.0.0.1:5001`):

```bash
ssh -J <nus_id>@stujump.comp.nus.edu.sg -L 5001:127.0.0.1:5001 xilinx@<ultra96-host>
```

`/tmp` on the Ultra96 is wiped every day, so always start from a fresh clone. The Ultra96 uses the `feature/dual-glove` branch, which has the deployed models in `deploy/` at the repo root (where the server looks for them):

```bash
cd /tmp && rm -rf cg4002_b08 && git clone -b feature/dual-glove https://github.com/aaravrawal52/cg4002_b08.git
```

```bash
cd /tmp/cg4002_b08 && sudo -E /usr/local/share/pynq-venv/bin/python3 comms/ultra96/ultra96_server.py
```

Wait until it prints `[SERVER] Listening on 127.0.0.1:5001`. **Keep this terminal open**: closing it closes the tunnel too.

### 3. Start the gateway (Terminal 2 on the Mac)

From the repo root on the Mac:

```bash
python3 comms/laptop/ble_gateway.py
```

You should see, in this order:

1. `[PHONE] WebSocket server listening on 0.0.0.0:8765`
2. `[GATEWAY] TCP connected`, then `Handshake successful: ACK,GLOVE_01` and `ACK,GLOVE_02` (the Ultra96 link is up)
3. `[BLE] GLOVE_01 Connected` / `[BLE] GLOVE_02 Connected`, then each glove's data starts streaming

The gateway keeps scanning for any glove that is not connected and reconnects a glove automatically if it drops.

### 4. Open the visualiser on the phone

Turn on Tailscale on the phone, then open the app. It connects to `ws://<mac-tailscale-ip>:8765`, and the gateway prints `VISUALIZER CONNECTED`. Gestures now appear on the phone.

### Stopping

Press `Ctrl+C` in the gateway terminal first, then in the Ultra96 terminal.

## Troubleshooting

| Problem | What to check |
|---|---|
| Gateway exits with `Connection refused` | The Ultra96 server is not running, or the SSH terminal (and so the tunnel) was closed. Redo step 2. |
| SSH warns it cannot listen on port 5001 | An old SSH tunnel is still open on the Mac. Close it (run `lsof -i :5001` to find it) and redo step 2. |
| A glove is never found | Check that the glove is on and charged. Turning the Mac's Bluetooth off and on clears a stuck connection. |
| Phone does not connect | Check that Tailscale is on on both devices and that the IP set in the app matches `tailscale ip -4` on the Mac. |
