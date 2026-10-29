#include <Arduino.h>
#include <Adafruit_MPU6050.h>
#include <Adafruit_Sensor.h>
#include <Wire.h>

// Define I2C pins
#define SDA_PIN 41  // GPIO41
#define SCL_PIN 42  // GPIO42

// Hall sensors are assumed to be connected to ADC1_0 and ADC1_1.
#define HALL_SENSOR_PIN 1  // ADC1_0 (GPIO1)
#define HALL_SENSOR_PIN_2 2  // ADC1_1 (GPIO2)

// Create MPU6050 sensor object
Adafruit_MPU6050 mpu;

const unsigned long SAMPLE_INTERVAL_US = 10000; // 100 Hz
const size_t WINDOW_SAMPLES = 1000; // 10 seconds at 100 Hz
const float MOTION_THRESHOLD = 8; // Current main.cpp value, m/s^2
const float HALL_THRESHOLD = 80.0; // Current main.cpp value, ADC counts
const float ROTATION_THRESHOLD = 3.0; // Current main.cpp value, rad/s

float hallBaseline1 = 0;
float hallBaseline2 = 0;
float accelBaselineX = 0;
float accelBaselineY = 0;
float accelBaselineZ = 0;
unsigned long lastSampleTime = 0;
size_t windowCount = 0;

float motionValues[WINDOW_SAMPLES];
float rotationValues[WINDOW_SAMPLES];
float hall1Changes[WINDOW_SAMPLES];
float hall2Changes[WINDOW_SAMPLES];

struct Statistics {
  float mean;
  float standardDeviation;
  float minimum;
  float maximum;
  float percentile99;
};

void sortValues(float *values, size_t count) {
  for (size_t i = 1; i < count; ++i) {
    float value = values[i];
    size_t position = i;
    while (position > 0 && values[position - 1] > value) {
      values[position] = values[position - 1];
      --position;
    }
    values[position] = value;
  }
}

Statistics calculateStatistics(float *values, size_t count) {
  Statistics result = {0, 0, values[0], values[0], 0};
  float sum = 0;
  float sumSquares = 0;
  for (size_t i = 0; i < count; ++i) {
    sum += values[i];
    sumSquares += values[i] * values[i];
    result.minimum = fminf(result.minimum, values[i]);
    result.maximum = fmaxf(result.maximum, values[i]);
  }
  result.mean = sum / count;
  float variance = (sumSquares / count) - (result.mean * result.mean);
  result.standardDeviation = sqrtf(fmaxf(variance, 0));
  sortValues(values, count);
  result.percentile99 = values[(count * 99) / 100];
  return result;
}

void printStatistics(const char *name, Statistics statistics, float currentThreshold) {
  float threeSigma = statistics.mean + (3.0 * statistics.standardDeviation);
  float suggestedThreshold = fmaxf(statistics.percentile99, threeSigma);
  Serial.print(name);
  Serial.print(" mean=");
  Serial.print(statistics.mean, 3);
  Serial.print(" sd=");
  Serial.print(statistics.standardDeviation, 3);
  Serial.print(" min=");
  Serial.print(statistics.minimum, 3);
  Serial.print(" max=");
  Serial.print(statistics.maximum, 3);
  Serial.print(" p99=");
  Serial.print(statistics.percentile99, 3);
  Serial.print(" 3sd=");
  Serial.print(threeSigma, 3);
  Serial.print(" suggested>=");
  Serial.print(suggestedThreshold, 3);
  Serial.print(" current=");
  Serial.println(currentThreshold, 3);
}

void printWindowSummary() {
  Statistics motion = calculateStatistics(motionValues, windowCount);
  Statistics rotation = calculateStatistics(rotationValues, windowCount);
  Statistics hall1 = calculateStatistics(hall1Changes, windowCount);
  Statistics hall2 = calculateStatistics(hall2Changes, windowCount);
  size_t motionTriggers = 0;
  size_t rotationTriggers = 0;
  size_t hallTriggers = 0;
  for (size_t i = 0; i < windowCount; ++i) {
    if (motionValues[i] > MOTION_THRESHOLD) ++motionTriggers;
    if (rotationValues[i] > ROTATION_THRESHOLD) ++rotationTriggers;
    if (hall1Changes[i] > HALL_THRESHOLD || hall2Changes[i] > HALL_THRESHOLD) ++hallTriggers;
  }
  Serial.println();
  Serial.println("--- Idle/noise window summary ---");
  Serial.print("Samples: ");
  Serial.println(windowCount);
  printStatistics("motion_mps2", motion, MOTION_THRESHOLD);
  printStatistics("rotation_rads", rotation, ROTATION_THRESHOLD);
  printStatistics("hall1_delta", hall1, HALL_THRESHOLD);
  printStatistics("hall2_delta", hall2, HALL_THRESHOLD);
  Serial.print("current motion threshold triggers: ");
  Serial.print(motionTriggers);
  Serial.print("/");
  Serial.println(windowCount);
  Serial.print("current rotation threshold triggers: ");
  Serial.print(rotationTriggers);
  Serial.print("/");
  Serial.println(windowCount);
  Serial.print("current Hall threshold triggers: ");
  Serial.print(hallTriggers);
  Serial.print("/");
  Serial.println(windowCount);
  Serial.println("Use suggested values as idle-noise floors; repeat while performing gestures to check separation.");
  Serial.println();
  windowCount = 0;
}

void setup() {
  // Initialize Serial communication
  Serial.begin(115200);
  delay(1000);
  
  Serial.println("ESP32 Sensor Noise and Threshold Test");
  Serial.println("=====================================");
  
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
  pinMode(HALL_SENSOR_PIN_2, INPUT);  // Set GPIO2 as analog input

  // Establish the idle Hall values. Keep the hand still during this period.
  Serial.println("Calibrating sensors: keep the hand still for 1 second...");
  for (int i = 0; i < 100; ++i) {
    sensors_event_t a, g, temp;
    mpu.getEvent(&a, &g, &temp);
    accelBaselineX += a.acceleration.x;
    accelBaselineY += a.acceleration.y;
    accelBaselineZ += a.acceleration.z;
    hallBaseline1 += analogRead(HALL_SENSOR_PIN);
    hallBaseline2 += analogRead(HALL_SENSOR_PIN_2);
    delay(10);
  }
  accelBaselineX /= 100.0;
  accelBaselineY /= 100.0;
  accelBaselineZ /= 100.0;
  hallBaseline1 /= 100.0;
  hallBaseline2 /= 100.0;
  Serial.println("Calibration complete.");
  Serial.print("Baselines: accel=(");
  Serial.print(accelBaselineX, 3);
  Serial.print(", ");
  Serial.print(accelBaselineY, 3);
  Serial.print(", ");
  Serial.print(accelBaselineZ, 3);
  Serial.print(") hall1=");
  Serial.print(hallBaseline1, 2);
  Serial.print(" hall2=");
  Serial.println(hallBaseline2, 2);
  Serial.println("Keep the device still for the first 10-second window, then repeat with each gesture.");
  Serial.println("CSV: time_ms,ax,ay,az,gx,gy,gz,hall1,hall2,motion,rotation,hall1_delta,hall2_delta");
}

void loop() {
  unsigned long currentTime = micros();
  if (currentTime - lastSampleTime >= SAMPLE_INTERVAL_US) {
    lastSampleTime = currentTime;
    sensors_event_t a, g, temp;
    mpu.getEvent(&a, &g, &temp);
    int hall1 = analogRead(HALL_SENSOR_PIN);
    int hall2 = analogRead(HALL_SENSOR_PIN_2);
    float ax = a.acceleration.x - accelBaselineX;
    float ay = a.acceleration.y - accelBaselineY;
    float az = a.acceleration.z - accelBaselineZ;
    float motion = sqrtf(ax * ax + ay * ay + az * az);
    float rotation = sqrtf(g.gyro.x * g.gyro.x + g.gyro.y * g.gyro.y + g.gyro.z * g.gyro.z);
    float hall1Delta = fabsf(hall1 - hallBaseline1);
    float hall2Delta = fabsf(hall2 - hallBaseline2);

    if (windowCount < WINDOW_SAMPLES) {
      motionValues[windowCount] = motion;
      rotationValues[windowCount] = rotation;
      hall1Changes[windowCount] = hall1Delta;
      hall2Changes[windowCount] = hall2Delta;
      ++windowCount;
    }

    if (windowCount % 10 == 0) {
      Serial.print(currentTime / 1000);
      Serial.print(",");
      Serial.print(a.acceleration.x, 3);
      Serial.print(",");
      Serial.print(a.acceleration.y, 3);
      Serial.print(",");
      Serial.print(a.acceleration.z, 3);
      Serial.print(",");
      Serial.print(g.gyro.x, 3);
      Serial.print(",");
      Serial.print(g.gyro.y, 3);
      Serial.print(",");
      Serial.print(g.gyro.z, 3);
      Serial.print(",");
      Serial.print(hall1);
      Serial.print(",");
      Serial.print(hall2);
      Serial.print(",");
      Serial.print(motion, 3);
      Serial.print(",");
      Serial.print(rotation, 3);
      Serial.print(",");
      Serial.print(hall1Delta, 2);
      Serial.print(",");
      Serial.println(hall2Delta, 2);
    }

    if (windowCount == WINDOW_SAMPLES) printWindowSummary();
  }
}