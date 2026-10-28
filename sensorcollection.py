
import serial
import csv
import os

# Change these settings
SERIAL_PORT = "COM6"  # Change to your ESP32's COM port
BAUD_RATE = 115200
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PERSON = "person7"
ACTION = "pinch_and_drag"  # Change to your desired action
CSV_PATH = os.path.join(SCRIPT_DIR, PERSON, ACTION, ACTION+"_data.csv")

# CSV column headers
headers = [
    "sequence",
    "time",
    "ax", "ay", "az",
    "gx", "gy", "gz",
    "hall1", "hall2",
    "tag"
]

# Create the directory if necessary
os.makedirs(os.path.dirname(CSV_PATH), exist_ok=True)

# Write headers only if the file does not exist or is empty
write_header = (
    not os.path.exists(CSV_PATH)
    or os.path.getsize(CSV_PATH) == 0
)

with serial.Serial(SERIAL_PORT, BAUD_RATE, timeout=1) as ser:
    print(f"Saving sensor data to {CSV_PATH}")
    print("Press Ctrl+C to stop.")

    try:
        with open(CSV_PATH, "a", newline="") as csvfile:
            writer = csv.writer(csvfile)

            if write_header:
                writer.writerow(headers)
                csvfile.flush()

            while True:
                line = ser.readline().decode(
                    "utf-8", errors="ignore"
                ).strip()

                if not line:
                    continue

                # Ignore startup messages and other serial output
                if not line.startswith("DATA,"):
                    continue

                values = line.split(",")

                if (len(values) - 1) != len(headers):
                    print("Invalid row:", line)
                    continue

                writer.writerow(values[1:])  # Skip the "DATA" prefix
                csvfile.flush()

                print("Saved:", line)

    except KeyboardInterrupt:
        print("\nRecording stopped.")
