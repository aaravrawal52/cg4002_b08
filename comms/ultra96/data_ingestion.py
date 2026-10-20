from enum import Enum


SENSOR_FIELDS = [
    "sequence",
    "timestamp",
    "ax",
    "ay",
    "az",
    "gx",
    "gy",
    "gz",
    "hall1",
    "hall2",
    "tag",
]


TAGS = {
    "IDLE",
    "ACTIVE",
    "UNCLASSIFIED",
}


class States(Enum):
    WAITING = "waiting"
    COLLECTING = "collecting"
    DISCARDING = "discarding"


class CircularBuffer:

    def __init__(self, size):
        self.size = size
        self.buffer = [None] * size
        self.start = 0
        self.count = 0

    def append(self, item):

        self.buffer[
            (self.start + self.count) % self.size
        ] = item

        if self.count == self.size:
            self.start = (
                self.start + 1
            ) % self.size

        else:
            self.count += 1

    def get(self):

        return [
            self.buffer[
                (self.start + i) % self.size
            ]
            for i in range(self.count)
        ]

    def clear(self):

        self.buffer = [None] * self.size
        self.start = 0
        self.count = 0


def parse_sensor_message(message):

    parts = message.strip().split(",")

    if parts[0] != "SENSOR":
        raise ValueError(
            f"Unknown packet type: {parts[0]}"
        )

    if len(parts) != len(SENSOR_FIELDS) + 1:
        raise ValueError(
            f"Expected {len(SENSOR_FIELDS) + 1} "
            f"fields, got {len(parts)}"
        )

    row = dict(
        zip(SENSOR_FIELDS, parts[1:])
    )

    if row["tag"] not in TAGS:
        raise ValueError(
            f"Invalid sensor tag: {row['tag']}"
        )

    return row


class GestureSegmenter:

    MAX_SEGMENT = 250
    PRE_BUFFER = 25

    def __init__(self):

        self.cb = CircularBuffer(
            self.PRE_BUFFER
        )

        self.active_buffer = []

        self.state = States.WAITING

    def add_row(self, row):

        if row["tag"] == "IDLE":

            if self.state == States.COLLECTING:

                self.cb.clear()
                self.active_buffer = []

            self.cb.append(row)

            self.state = States.WAITING

        elif row["tag"] == "UNCLASSIFIED":

            segment = None

            if self.state == States.COLLECTING:

                self.active_buffer.append(row)

                segment = self.active_buffer

            self.state = States.WAITING

            self.active_buffer = []

            return segment

        else:

            if self.state == States.DISCARDING:
                return None

            if self.state == States.WAITING:

                self.active_buffer = self.cb.get()
                self.cb.clear()

            self.state = States.COLLECTING

            self.active_buffer.append(row)

            if len(self.active_buffer) >= self.MAX_SEGMENT:

                self.state = States.DISCARDING
                self.active_buffer = []

        return None