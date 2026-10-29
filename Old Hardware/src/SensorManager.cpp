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

bool readSensorSample(Sample &sample) {

    unsigned long now = micros();

    if (
        now - lastSampleTime
        < SAMPLE_INTERVAL_US
    ) {
        return false;
    }

    lastSampleTime = now;

    sensors_event_t a, g, temp;

    if (!mpu.getEvent(&a, &g, &temp)) {
        return false;
    }

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

