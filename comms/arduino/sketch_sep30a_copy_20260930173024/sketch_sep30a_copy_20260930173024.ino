#include <Arduino.h>

#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>


// ============================================================
// CG4002 BLE PROTOCOL CONFIGURATION
// ============================================================

// One BLE service represents the CG4002 glove.
#define SERVICE_UUID \
  "12345678-1234-1234-1234-1234567890ab"

// FireBeetle -> Mac
//
// The Mac SUBSCRIBES to this characteristic.
// FireBeetle sends data using BLE notifications.
#define SENSOR_CHAR_UUID \
  "12345678-1234-1234-1234-1234567890ac"

// Mac -> FireBeetle
//
// The Mac WRITES commands into this characteristic.
#define COMMAND_CHAR_UUID \
  "12345678-1234-1234-1234-1234567890ad"


// ============================================================
// GLOBAL BLE OBJECTS
// ============================================================

BLECharacteristic* sensorCharacteristic;
BLECharacteristic* commandCharacteristic;


// ============================================================
// CONNECTION STATE
// ============================================================

bool deviceConnected = false;


// ============================================================
// DUMMY SENSOR STREAM
// ============================================================

unsigned long sequence = 0;
unsigned long lastSendTime = 0;

// Start slowly while testing bidirectional communication.
//
// 1000 ms = 1 packet/sec.
//
// Later:
// 10 ms = 100 packets/sec.
const unsigned long SEND_INTERVAL_MS = 14;
bool streamingEnabled = false;
bool maxSpeedMode = false;


// file transfer
uint32_t fileExpectedSize = 0;
uint32_t fileReceivedSize = 0;
uint32_t fileCRC = 0xFFFFFFFF;
bool fileTransferActive = false;

// ============================================================
// BLE SERVER CALLBACKS
// ============================================================
//
// These callbacks are triggered automatically by the BLE
// library when the Mac connects or disconnects.
//

class ServerCallbacks : public BLEServerCallbacks {

  void onConnect(BLEServer* server) override {

    deviceConnected = true;

    Serial.println();
    Serial.println("[BLE] Mac connected");
  }


  void onDisconnect(BLEServer* server) override {

    deviceConnected = false;

    Serial.println();
    Serial.println("[BLE] Mac disconnected");

    // Start advertising again so that the Mac can find us
    // after a reset, disconnect or loss of connection.
    BLEDevice::startAdvertising();

    Serial.println("[BLE] Advertising restarted");
  }
};


// ============================================================
// COMMAND CALLBACK
// ============================================================
//
// This callback runs whenever the Mac WRITES something into
// COMMAND_CHAR_UUID.
//
// Flow:
//
// Mac
//  |
//  | BLE WRITE
//  v
// COMMAND characteristic
//  |
//  v
// onWrite()
//  |
//  | modify packet
//  v
// SENSOR characteristic
//  |
//  | BLE NOTIFY
//  v
// Mac
//
uint32_t updateCRC32(
  uint32_t crc,
  const uint8_t* data,
  size_t length
) {
  for (size_t i = 0; i < length; i++) {
    crc ^= data[i];

    for (int bit = 0; bit < 8; bit++) {
      if (crc & 1) {
        crc = (crc >> 1) ^ 0xEDB88320;
      } else {
        crc >>= 1;
      }
    }
  }

  return crc;
}

uint32_t calculateCRC32(const String& data) {

  uint32_t crc = 0xFFFFFFFF;

  crc = updateCRC32(
    crc,
    reinterpret_cast<const uint8_t*>(
      data.c_str()
    ),
    data.length()
  );

  return crc ^ 0xFFFFFFFF;
}

class CommandCallbacks : public BLECharacteristicCallbacks {

  void onWrite(
    BLECharacteristic* characteristic
  ) override {

    // Read whatever the Mac wrote.
    String command =
      characteristic->getValue();


    if (command.length() == 0) {
      return;
    }


    Serial.println();

    Serial.print(
      "[COMMAND] Received from Mac: "
    );

    Serial.println(command);
// ========================================================

    // CONTROL COMMAND: START STREAM

    // ========================================================

    if (command == "CONTROL,START_STREAM") {

      streamingEnabled = true;
      maxSpeedMode = false;

      // Start stream sequence numbers from 0.

      sequence = 0;

      // Reset timing as well.

      lastSendTime = millis();

      Serial.println(

        "[CONTROL] Stream started"

      );

      return;

    }

    // ========================================================

    // CONTROL COMMAND: STOP STREAM

    // ========================================================

    if (command == "CONTROL,STOP_STREAM") {

      streamingEnabled = false;
      maxSpeedMode = false;
      Serial.println(

        "[CONTROL] Stream stopped"

      );

      return;

    }
    if (command == "CONTROL,MAX_STREAM") {

      streamingEnabled = true;
      maxSpeedMode = true;

      sequence = 0;
      lastSendTime = millis();

      Serial.println(
        "[CONTROL] Maximum-speed stream started"
      );

      return;
    }
    // ========================================================
// FILE TRANSFER: START
// ========================================================

    if (command.startsWith("FILE_START,")) {

      int comma =
        command.indexOf(',');

      String sizeString =
        command.substring(comma + 1);

      fileExpectedSize =
        sizeString.toInt();

      fileReceivedSize = 0;

      fileCRC = 0xFFFFFFFF;

      fileTransferActive = true;

      Serial.print(
        "[FILE] Transfer started. Expected: "
      );

      Serial.print(
        fileExpectedSize
      );

      Serial.println(
        " bytes"
      );

      return;
    }
    // ========================================================
// FILE TRANSFER: DATA
// ========================================================

if (command.startsWith("FILE_DATA,")) {

  if (!fileTransferActive) {

    Serial.println(
      "[FILE] DATA received without active transfer"
    );

    return;
  }

  int comma =
    command.indexOf(',');

  String payload =
    command.substring(comma + 1);

  const uint8_t* bytes =
    reinterpret_cast<const uint8_t*>(
      payload.c_str()
    );

  size_t length =
    payload.length();

  fileCRC = updateCRC32(
    fileCRC,
    bytes,
    length
  );

  fileReceivedSize += length;

  return;
}

// ========================================================
// FILE TRANSFER: END
// ========================================================

if (command == "FILE_END") {

  if (!fileTransferActive) {

    Serial.println(
      "[FILE] END received without active transfer"
    );

    return;
  }

  fileTransferActive = false;

  uint32_t finalCRC =
    fileCRC ^ 0xFFFFFFFF;

  String result =
    "FILE_RESULT,"
    + String(fileReceivedSize)
    + ","
    + String(finalCRC);

  sensorCharacteristic->setValue(
    result.c_str()
  );

  sensorCharacteristic->notify();

  Serial.println();

  Serial.println(
    "========== FILE RESULT =========="
  );

  Serial.print(
    "Expected bytes: "
  );

  Serial.println(
    fileExpectedSize
  );

  Serial.print(
    "Received bytes: "
  );

  Serial.println(
    fileReceivedSize
  );

  Serial.print(
    "CRC32: "
  );

  Serial.println(
    finalCRC,
    HEX
  );

  Serial.println(
    "================================="
  );

  return;
}

    
// ========================================================
// NORMAL PIPELINE PACKET WITH CRC32
// ========================================================

// Expected:
//
// DATA,GLOVE_01,seq,timestamp,
// ax,ay,az,gx,gy,gz,hall1,hall2,CRC
//
// CRC is calculated over everything BEFORE the final comma.

    int lastComma = command.lastIndexOf(',');

    if (lastComma < 0) {

      Serial.println(
        "[CRC] Invalid packet format"
      );

      return;
    }


    // Everything before transmitted CRC.
    String packetWithoutCRC =
      command.substring(
        0,
        lastComma
      );


    // CRC received from Mac.
    String receivedCRCString =
      command.substring(
        lastComma + 1
      );

    uint32_t receivedCRC =
      strtoul(
        receivedCRCString.c_str(),
        NULL,
        16
      );


    // Calculate our own CRC.
    uint32_t calculatedCRC =
      calculateCRC32(
        packetWithoutCRC
      );


    if (receivedCRC != calculatedCRC) {

      Serial.print(
        "[CRC] FAILED received="
      );

      Serial.print(
        receivedCRC,
        HEX
      );

      Serial.print(
        " calculated="
      );

      Serial.println(
        calculatedCRC,
        HEX
      );

      Serial.println(
        "[PACKET] Corrupted packet dropped"
      );

      return;
    }


    Serial.println(
      "[CRC] Valid packet"
    );


    // FireBeetle modifies the validated packet.
    //
    // We deliberately do NOT include the old CRC because
    // modifying the packet invalidates that CRC.

    String modifiedPacket =
      "RESPONSE,"
      + packetWithoutCRC
      + ",MODIFIED_BY_GLOVE";


    // Calculate a NEW CRC over the modified packet.

    uint32_t responseCRC =
      calculateCRC32(
        modifiedPacket
      );


    char crcBuffer[9];

    snprintf(
      crcBuffer,
      sizeof(crcBuffer),
      "%08lX",
      (unsigned long) responseCRC
    );


    String response =
      modifiedPacket
      + ","
      + String(crcBuffer);


    sensorCharacteristic->setValue(
      response.c_str()
    );

    sensorCharacteristic->notify();


    Serial.print(
      "[RESPONSE] Sent with CRC="
    );

    Serial.println(
      crcBuffer
    );
  }
};


// ============================================================
// SETUP
// ============================================================

void setup() {

  Serial.begin(115200);

  delay(1000);


  Serial.println();
  Serial.println(
    "===================================="
  );

  Serial.println(
    "      CG4002 FIREBEETLE COMMS"
  );

  Serial.println(
    "===================================="
  );


  // ----------------------------------------------------------
  // INITIALIZE BLE
  // ----------------------------------------------------------

  BLEDevice::init(
    "CG4002_GLOVE"
  );


  // ----------------------------------------------------------
  // CREATE BLE SERVER
  // ----------------------------------------------------------

  BLEServer* server =
    BLEDevice::createServer();


  server->setCallbacks(
    new ServerCallbacks()
  );


  // ----------------------------------------------------------
  // CREATE CG4002 SERVICE
  // ----------------------------------------------------------

  BLEService* service =
    server->createService(
      SERVICE_UUID
    );


  // ----------------------------------------------------------
  // SENSOR / RESPONSE CHARACTERISTIC
  //
  // FireBeetle -> Mac
  // ----------------------------------------------------------

  sensorCharacteristic =
    service->createCharacteristic(
      SENSOR_CHAR_UUID,

      BLECharacteristic::PROPERTY_READ |
      BLECharacteristic::PROPERTY_NOTIFY
    );


  // BLE2902 lets the client enable/disable notifications.

  sensorCharacteristic->addDescriptor(
    new BLE2902()
  );


  sensorCharacteristic->setValue(
    "READY"
  );


  // ----------------------------------------------------------
  // COMMAND CHARACTERISTIC
  //
  // Mac -> FireBeetle
  // ----------------------------------------------------------

  commandCharacteristic =
    service->createCharacteristic(
      COMMAND_CHAR_UUID,

      BLECharacteristic::PROPERTY_WRITE
    );


  commandCharacteristic->setCallbacks(
    new CommandCallbacks()
  );


  // ----------------------------------------------------------
  // START SERVICE
  // ----------------------------------------------------------

  service->start();


  // ----------------------------------------------------------
  // START ADVERTISING
  // ----------------------------------------------------------

  BLEAdvertising* advertising =
    BLEDevice::getAdvertising();


  advertising->addServiceUUID(
    SERVICE_UUID
  );


  advertising->setScanResponse(
    true
  );


  BLEDevice::startAdvertising();


  Serial.println(
    "[BLE] Advertising as CG4002_GLOVE"
  );

  Serial.println(
    "[BLE] Waiting for Mac..."
  );
}


// ============================================================
// MAIN LOOP
// ============================================================

void loop() {

  // If nobody is connected, don't generate dummy traffic.

  if (!deviceConnected) {

    delay(10);

    return;
  }


  unsigned long now =
    millis();


  // ----------------------------------------------------------
  // PERIODIC DUMMY SENSOR PACKET
  // ----------------------------------------------------------

    if (
        streamingEnabled &&
        (
          maxSpeedMode ||
          now - lastSendTime >= SEND_INTERVAL_MS
        )
    ) { 

    lastSendTime = now;


    // Current dummy packet:
    //
    // DATA,<sequence>,<timestamp>
    //
    // Example:
    //
    // DATA,42,15321
    //
    // Later this payload will contain actual-format sensor
    // fields.

float ax = 1.20;
float ay = -0.40;
float az = 9.71;
float gx = 0.10;
float gy = 0.05;
float gz = -0.02;
int hall1 = 2050;
int hall2 = 2110;
String message =
  "DATA,"
  "GLOVE_01,"
  + String(sequence)
  + ","
  + String(now)
  + ","
  + String(ax, 2)
  + ","
  + String(ay, 2)
  + ","
  + String(az, 2)
  + ","
  + String(gx, 2)
  + ","
  + String(gy, 2)
  + ","
  + String(gz, 2)
  + ","
  + String(hall1)
  + ","
  + String(hall2);

    sensorCharacteristic->setValue(
      message.c_str()
    );


    sensorCharacteristic->notify();


    Serial.print(
      "[DATA] Sent: "
    );

    Serial.println(
      message
    );


    sequence++;
  }
}