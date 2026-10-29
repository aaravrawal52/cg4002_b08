#include "SensorManager.h"

#include <Arduino.h>
#include <Wire.h>
#include <Adafruit_MPU6050.h>
#include <Adafruit_Sensor.h>

// Hardware configuration from sensor teammate.
#define SDA_PIN 21
#define SCL_PIN 22

#define HALL_SENSOR_PIN 36
#define HALL_SENSOR_PIN_2 39

static Adafruit_MPU6050 mpu;

static constexpr unsigned long SAMPLE_INTERVAL_US = 14286;

static unsigned long lastSampleTime = 0;

bool initializeSensors() {

    Wire.begin(SDA_PIN, SCL_PIN);

    if (!mpu.begin(0x68, &Wire)) {
        Serial.println(
            "[SENSOR] MPU6050 initialization failed"
        );

        return false;
    }

    mpu.setAccelerometerRange(
        MPU6050_RANGE_4_G
    );

    mpu.setGyroRange(
        MPU6050_RANGE_500_DEG
    );

    mpu.setFilterBandwidth(
        MPU6050_BAND_21_HZ
    );

    analogReadResolution(12);
    analogSetAttenuation(ADC_11db);

    pinMode(HALL_SENSOR_PIN, INPUT);
    pinMode(HALL_SENSOR_PIN_2, INPUT);

    Serial.println(
        "[SENSOR] MPU6050 and Hall sensors initialized"
    );

    return true;
}

static int readMpuRegister(uint8_t reg) {
    Wire.beginTransmission(0x68);
    Wire.write(reg);

    if (Wire.endTransmission(false) != 0) {
        return -1;
    }

    if (Wire.requestFrom(0x68, 1) != 1) {
        return -1;
    }

    return Wire.read();
}

static bool restartIMU() {
    Serial.println("[MPU] Restarting...");

    if (!mpu.begin(0x68, &Wire)) {
        Serial.println("[MPU] Restart failed: device not found");
        return false;
    }

    // Restore your settings after reset.
    mpu.setAccelerometerRange(MPU6050_RANGE_4_G);
    mpu.setGyroRange(MPU6050_RANGE_500_DEG);
    mpu.setFilterBandwidth(MPU6050_BAND_21_HZ);

    lastSampleTime = micros();

    int power = readMpuRegister(0x6B);

    if (power < 0 || (power & 0x40)) {
        Serial.println("[MPU] Restart failed: cannot confirm awake");
        return false;
    }

    Serial.println("[MPU] Initialised; waiting for valid readings");
    return true;
}

bool readSensorSample(Sample &sample) {

    unsigned long now = micros();

    if (
        now - lastSampleTime
        < SAMPLE_INTERVAL_US
    ) {
        return false;
    }

    lastSampleTime = now;

    sensors_event_t a{}, g{}, temp{};

    static unsigned int badSampleCount = 0;
    static unsigned long lastRestartMs = 0;

    bool readOK = mpu.getEvent(&a, &g, &temp);

    bool allZero =
        a.acceleration.x == 0.0f &&
        a.acceleration.y == 0.0f &&
        a.acceleration.z == 0.0f &&
        g.gyro.x == 0.0f &&
        g.gyro.y == 0.0f &&
        g.gyro.z == 0.0f;

    if (!readOK || allZero) {
        if (badSampleCount < 3) {
            badSampleCount++;
        }

        if (badSampleCount >= 3 &&
            millis() - lastRestartMs >= 1000UL) {

            restartIMU();

            // Start the retry cooldown after the attempt finishes.
            lastRestartMs = millis();
        }

        // Never pass this invalid sample to Madgwick or the classifier.
        return false;
    }

    badSampleCount = 0;

    sample.time = now;

    sample.ax = a.acceleration.x;
    sample.ay = a.acceleration.y;
    sample.az = a.acceleration.z;

    sample.gx = g.gyro.x;
    sample.gy = g.gyro.y;
    sample.gz = g.gyro.z;

    sample.hall1 = analogRead(
        HALL_SENSOR_PIN
    );

    sample.hall2 = analogRead(
        HALL_SENSOR_PIN_2
    );

    return true;
}

