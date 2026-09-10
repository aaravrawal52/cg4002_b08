#include <Arduino.h>
#include <Adafruit_MPU6050.h>
#include <Adafruit_Sensor.h>
#include <Wire.h>

// Define I2C pins
#define SDA_PIN 41  // GPIO41
#define SCL_PIN 42  // GPIO42

// Define Hall sensor pin
#define HALL_SENSOR_PIN 1  // ADC1_0 (GPIO1)

// Create MPU6050 sensor object
Adafruit_MPU6050 mpu;

// Timing variables for sensor reads
unsigned long lastIMUTime = 0;
unsigned long lastADCTime = 0;
const unsigned long IMU_INTERVAL = 10000;  // 10ms for 100Hz 
const unsigned long ADC_INTERVAL = 20000;  // 20ms for 50Hz

//For testing so serial monitor doesn't get flooded with data
const unsigned long IMU_INTERVAL_TEST = 500000;  // 500ms for 100Hz 
const unsigned long ADC_INTERVAL_TEST = 1000000;  // 1000ms for 50Hz

void setup() {
  // Initialize Serial communication
  Serial.begin(115200);
  delay(1000);
  
  Serial.println("ESP32 MPU-6050 I2C Sensor Reader");
  Serial.println("==================================");
  
  // Initialize I2C on custom pins (SDA=GPIO41, SCL=GPIO42)
  Wire.begin(SDA_PIN, SCL_PIN);
  
  // Initialize MPU6050
  if (!mpu.begin()) {
    Serial.println("Failed to find MPU6050 chip");
    while (1) {
      delay(10);
    }
  }
  
  Serial.println("MPU6050 Found!");
  
  // Set accelerometer range
  mpu.setAccelerometerRange(MPU6050_RANGE_4_G);
  Serial.print("Accelerometer range set to: ");
  switch (mpu.getAccelerometerRange()) {
    case MPU6050_RANGE_2_G:
      Serial.println("+-2G");
      break;
    case MPU6050_RANGE_4_G:
      Serial.println("+-4G");
      break;
    case MPU6050_RANGE_8_G:
      Serial.println("+-8G");
      break;
    case MPU6050_RANGE_16_G:
      Serial.println("+-16G");
      break;
  }
  
  // Set gyroscope range
  mpu.setGyroRange(MPU6050_RANGE_500_DEG);
  Serial.print("Gyro range set to: ");
  switch (mpu.getGyroRange()) {
    case MPU6050_RANGE_250_DEG:
      Serial.println("+- 250 deg/s");
      break;
    case MPU6050_RANGE_500_DEG:
      Serial.println("+- 500 deg/s");
      break;
    case MPU6050_RANGE_1000_DEG:
      Serial.println("+- 1000 deg/s");
      break;
    case MPU6050_RANGE_2000_DEG:
      Serial.println("+- 2000 deg/s");
      break;
  }
  
  // Set filter bandwidth
  mpu.setFilterBandwidth(MPU6050_BAND_21_HZ);
  Serial.print("Filter bandwidth set to: ");
  switch (mpu.getFilterBandwidth()) {
    case MPU6050_BAND_260_HZ:
      Serial.println("260 Hz");
      break;
    case MPU6050_BAND_184_HZ:
      Serial.println("184 Hz");
      break;
    case MPU6050_BAND_94_HZ:
      Serial.println("94 Hz");
      break;
    case MPU6050_BAND_44_HZ:
      Serial.println("44 Hz");
      break;
    case MPU6050_BAND_21_HZ:
      Serial.println("21 Hz");
      break;
    case MPU6050_BAND_10_HZ:
      Serial.println("10 Hz");
      break;
    case MPU6050_BAND_5_HZ:
      Serial.println("5 Hz");
      break;
  }
  
  Serial.println("\nStarting sensor reading...\n");
  
  // Configure Hall sensor ADC
  analogReadResolution(12);           // Set 12-bit resolution (0-4095)
  analogSetAttenuation(ADC_11db);     // Set attenuation to 11dB for 0-3.3V range
  pinMode(HALL_SENSOR_PIN, INPUT);    // Set GPIO1 as analog input
  Serial.println("Hall sensor ADC configured: GPIO1 (ADC1_0), 12-bit, 0-3.3V range\n");
}

void loop() {
  unsigned long currentTime = micros();
  
  // Read IMU at 100Hz (every 10ms)
  if (currentTime - lastIMUTime >= IMU_INTERVAL_TEST) {
    lastIMUTime = currentTime;
    
    sensors_event_t a, g, temp;
    mpu.getEvent(&a, &g, &temp);.
    
    // Print accelerometer data
    Serial.print("Acceleration X: ");
    Serial.print(a.acceleration.x);
    Serial.print(" m/s² | Y: ");
    Serial.print(a.acceleration.y);
    Serial.print(" m/s² | Z: ");
    Serial.print(a.acceleration.z);
    Serial.println(" m/s²");
    
    // Print gyroscope data
    Serial.print("Gyro X: ");
    Serial.print(g.gyro.x);
    Serial.print(" rad/s | Y: ");
    Serial.print(g.gyro.y);
    Serial.print(" rad/s | Z: ");
    Serial.print(g.gyro.z);
    Serial.println(" rad/s");
  }
  
  // Read ADC at 50Hz (every 20ms)
  if (currentTime - lastADCTime >= ADC_INTERVAL_TEST) {
    lastADCTime = currentTime;
    
    // Read and print Hall sensor data
    int hallSensorValue = analogRead(HALL_SENSOR_PIN);
    Serial.print("Hall Sensor (ADC1_0): ");
    Serial.print(hallSensorValue);
    Serial.print(" (0-4095) | Voltage: ");
    Serial.print((hallSensorValue / 4095.0) * 3.3);
    Serial.println(" V");
    
    Serial.println("---");
  }
}