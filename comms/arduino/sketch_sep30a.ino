#include <Arduino.h>

#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>
#include "mbedtls/md5.h"
#include "SensorManager.h"
#include "GestureProcessor.h"


// ============================================================
// FIREBEETLE DEVICE CONFIGURATION
// ============================================================

// 1 = First FireBeetle
// 2 = Second FireBeetle

#define GLOVE_NUMBER 1

#if GLOVE_NUMBER == 1

  #define DEVICE_ID "GLOVE_01"
  #define BLE_DEVICE_NAME "CG4002_GLOVE_01"

#elif GLOVE_NUMBER == 2

  #define DEVICE_ID "GLOVE_02"
  #define BLE_DEVICE_NAME "CG4002_GLOVE_02"

#else

  #error "Invalid GLOVE_NUMBER"

#endif


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

bool fileTransferActive = false;

// Incremental MD5 state.
// This lets us hash chunks as they arrive without storing
// the entire file in ESP32 RAM.
mbedtls_md5_context fileMD5Context;

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

  fileTransferActive = true;


  // Reset MD5 context for a new file.
  mbedtls_md5_free(
    &fileMD5Context
  );

  mbedtls_md5_init(
    &fileMD5Context
  );

  mbedtls_md5_starts(
    &fileMD5Context
  );


  Serial.println();

  Serial.println(
    "========== FILE TRANSFER =========="
  );

  Serial.print(
    "[FILE] Expected size: "
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
    command.substring(
      comma + 1
    );


  const unsigned char* bytes =
    reinterpret_cast<const unsigned char*>(
      payload.c_str()
    );

  size_t length =
    payload.length();


  // Feed this chunk into the running MD5.
  mbedtls_md5_update(
    &fileMD5Context,
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


  // MD5 is 16 bytes = 128 bits.
  unsigned char digest[16];

  mbedtls_md5_finish(
    &fileMD5Context,
    digest
  );


  // Convert binary MD5 into the usual
  // 32-character hexadecimal representation.
  char md5String[33];

  for (int i = 0; i < 16; i++) {

    sprintf(
      &md5String[i * 2],
      "%02x",
      digest[i]
    );
  }

  md5String[32] = '\0';


  // Return verification information to Mac.
  String result =
    "FILE_RESULT,"
    + String(fileReceivedSize)
    + ","
    + String(md5String);


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
    "Received MD5: "
  );

  Serial.println(
    md5String
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
// GESTURE EVENT TRANSMISSION
// ============================================================
//
// Sends the result of local deterministic classification.
//
// LOCAL_GESTURE:
//   FireBeetle recognized the gesture.
//
// ML_REQUEST:
//   Gesture detected but deterministic classification
//   could not recognize it.
//
// Both messages include:
//   device ID
//   event sequence
//   sensor sequence
//   gesture type (for LOCAL_GESTURE)
//   CRC32
// ============================================================

uint32_t gestureEventSequence = 0;

void sendGestureEvent(
    const GestureResult &result,
    uint32_t sensorSequence
) {

    if (!result.completed) {
        return;
    }

    if (!deviceConnected) {
        Serial.println(
            "[GESTURE] No BLE client connected"
        );
        return;
    }

    String payload;

    if (result.type == GestureType::UNRECOGNIZED) {

        payload =
            "ML_REQUEST,"
            + String(DEVICE_ID)
            + ","
            + String(gestureEventSequence)
            + ","
            + String(sensorSequence);

    } else {

        payload =
            "LOCAL_GESTURE,"
            + String(DEVICE_ID)
            + ","
            + String(gestureEventSequence)
            + ","
            + String(sensorSequence)
            + ","
            + String(gestureTypeName(result.type));
    }

    uint32_t crc = calculateCRC32(payload);

    char crcBuffer[9];

    snprintf(
        crcBuffer,
        sizeof(crcBuffer),
        "%08lX",
        (unsigned long)crc
    );

    String packet =
        payload + "," + String(crcBuffer);

    sensorCharacteristic->setValue(
        packet.c_str()
    );

    sensorCharacteristic->notify();

    Serial.print("[GESTURE TX] ");
    Serial.println(packet);

    gestureEventSequence++;
}

// ============================================================
// SETUP
// ============================================================

void setup() {

  Serial.begin(115200);
  delay(1000);

    Serial.println();

    Serial.println("========== BOOT DEBUG ==========");

    Serial.println("[DEBUG] Entered setup()");

    Serial.print("[DEBUG] Chip model: ");

    Serial.println(ESP.getChipModel());

    Serial.println("[DEBUG] Starting sensor initialization");

    if (!initializeSensors()) {

        Serial.println("[ERROR] Sensor initialization failed");

        while (true) {

            delay(1000);

        }

    }

    Serial.println("[DEBUG] Sensor initialization complete");
    // ============================================================
// INITIALIZE DETERMINISTIC GESTURE PROCESSOR
// ============================================================

  initializeGestureProcessor(70.0f);

  Serial.println();
  Serial.println("[GESTURE] Keep the glove stationary");
  Serial.println("[GESTURE] Calibrating for approximately 1 second...");
  if (!initializeSensors()) {

    Serial.println(
        "[ERROR] Sensor initialization failed"
    );

    while (true) {
        delay(1000);
    }
}
  mbedtls_md5_init(
  &fileMD5Context
);

  delay(1000);


  Serial.println();
  Serial.println(
    "===================================="
  );

  Serial.println(
    "      CG4002 FIREBEETLE COMMS"
  );
  Serial.print(
    "[DEVICE] ID: "
  );

  Serial.println(
    DEVICE_ID
  );

  Serial.println(
    "===================================="
  );


  // ----------------------------------------------------------
  // INITIALIZE BLE
  // ----------------------------------------------------------

  BLEDevice::init(
    BLE_DEVICE_NAME
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


  Serial.print(

    "[BLE] Advertising as "

  );

  Serial.println(

    BLE_DEVICE_NAME

  );

  Serial.println(
    "[BLE] Waiting for Mac..."
  );
}


// ============================================================
// MAIN LOOP
// ============================================================

void loop() {

    // ========================================================
    // 1. ACQUIRE REAL SENSOR DATA
    // ========================================================

    Sample rawSample;
    GestureResult result;

    if (!readSensorSample(rawSample)) {
        delay(1);
        return;
    }


    // ========================================================
    // 2. CALIBRATION / GESTURE PROCESSING
    // ========================================================

    // Gesture processing always runs, even when the
    // Mac is disconnected or streaming is disabled.

    if (!gestureCalibrationComplete()) {

        calibrateGestureProcessor(rawSample);

    } else {

    result = processGestureSample(rawSample);

    if (result.completed) {

        Serial.println();

        Serial.println("========== GESTURE RESULT ==========");

        Serial.print("[GESTURE] Classification: ");

        Serial.println(gestureTypeName(result.type));

        Serial.print("[GESTURE] Samples: ");

        Serial.println(result.sampleCount);

        Serial.println("====================================");

    }
    }


    // ========================================================
    // 3. CONTINUOUS RAW SENSOR BLE STREAMING
    // ========================================================

    // Do not stop sensor acquisition or gesture processing
    // when BLE is disconnected.

    if (!deviceConnected || !streamingEnabled) {
        return;
    }

    // The existing DATA packet format is preserved:
    //
    // DATA,DEVICE_ID,sequence,timestamp,
    // ax,ay,az,gx,gy,gz,hall1,hall2

    String message =
        "DATA,"
        + String(DEVICE_ID)
        + ","
        + String(sequence)
        + ","
        + String(rawSample.time / 1000UL)
        + ","
        + String(rawSample.ax, 2)
        + ","
        + String(rawSample.ay, 2)
        + ","
        + String(rawSample.az, 2)
        + ","
        + String(rawSample.gx, 2)
        + ","
        + String(rawSample.gy, 2)
        + ","
        + String(rawSample.gz, 2)
        + ","
        + String(rawSample.hall1)
        + ","
        + String(rawSample.hall2);

// ============================================================
// TRANSMIT RAW SENSOR PACKET
// ============================================================

// This is the sequence assigned to the current sample.
      uint32_t currentSensorSequence = sequence;

      sensorCharacteristic->setValue(
          message.c_str()
      );

      sensorCharacteristic->notify();

      sequence++;

      // ============================================================
      // TRANSMIT GESTURE EVENT AFTER SENSOR DATA
      // ============================================================

      if (result.completed) {

          sendGestureEvent(
              result,
              currentSensorSequence
          );
      }


    // ========================================================
    // 4. PERIODIC DEBUG OUTPUT
    // ========================================================

    // Avoid printing every packet, which would add
    // unnecessary serial I/O overhead.

    if (sequence % 100 == 0) {

        Serial.print(
            "[SENSOR] BLE seq="
        );

        Serial.println(sequence);
    }
}