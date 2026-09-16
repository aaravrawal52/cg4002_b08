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
const unsigned long MAX_GESTURE_US = 2500000; // hard upper bound: 2.5 seconds
const unsigned long QUIET_TIME_US = 180000; // end after 180 ms below threshold
const size_t MOTION_WINDOW_SIZE = 25; // 250 ms at 100 Hz
const size_t CALIBRATION_SAMPLES = 100;

struct Sample {
  unsigned long time;
  float ax, ay, az;
  float gx, gy, gz;
  int hall1, hall2;
};

const size_t MAX_SAMPLES = MAX_GESTURE_US / SAMPLE_INTERVAL_US;
Sample gestureSamples[MAX_SAMPLES];
size_t sampleCount = 0;
float motionWindow[MOTION_WINDOW_SIZE] = {0};
size_t motionWindowCount = 0;
size_t motionWindowIndex = 0;
float hall1Window[MOTION_WINDOW_SIZE] = {0};
float hall2Window[MOTION_WINDOW_SIZE] = {0};
float rotationWindow[MOTION_WINDOW_SIZE] = {0};
size_t hall1WindowCount = 0;
size_t hall2WindowCount = 0;
size_t rotationWindowCount = 0;
size_t hall1WindowIndex = 0;
size_t hall2WindowIndex = 0;
size_t rotationWindowIndex = 0;
bool gestureActive = false;
unsigned long lastSampleTime = 0;
unsigned long lastMotionTime = 0;
float hallBaseline1 = 0;
float hallBaseline2 = 0;
float accelBaselineX = 0;
float accelBaselineY = 0;
float accelBaselineZ = 0;
float motionNoiseThreshold = 0;
float hall1NoiseThreshold = 0;
float hall2NoiseThreshold = 0;
float rotationNoiseThreshold = 0;

void classifyGesture();
void processSample(const Sample &sample);
void finishGesture();
float hallChange(int value, float baseline) {
  return fabsf(value - baseline);
}

bool windowMeanExceeded(float value, float *window, size_t *windowCount,
                        size_t *windowIndex, float noiseThreshold,
                        float *windowMean) {
  window[*windowIndex] = value;
  *windowIndex = (*windowIndex + 1) % MOTION_WINDOW_SIZE;
  if (*windowCount < MOTION_WINDOW_SIZE) ++(*windowCount);

  if (*windowCount < MOTION_WINDOW_SIZE) {
    *windowMean = 0;
    return false;
  }

  float sum = 0;
  for (size_t i = 0; i < MOTION_WINDOW_SIZE; ++i) {
    sum += window[i];
  }

  *windowMean = sum / MOTION_WINDOW_SIZE;
  return *windowMean > noiseThreshold;
}

void calculateCalibrationThreshold(const float *values, size_t count,
                                   float *mean, float *standardDeviation,
                                   float *threshold) {
  float sum = 0;
  float sumSquares = 0;
  for (size_t i = 0; i < count; ++i) {
    sum += values[i];
    sumSquares += values[i] * values[i];
  }
  *mean = sum / count;
  float variance = (sumSquares / count) - (*mean * *mean);
  *standardDeviation = sqrtf(fmaxf(variance, 0.0f));
  *threshold = *mean + (4.0f * *standardDeviation);
}

void classifyGesture() {
  if (sampleCount < 3) {
    Serial.println("too short");
    return;
  }

  float hall1Change = 0;
  float hall2Change = 0;
  float hall1Delta = 0;
  float hall2Delta = 0;
  float xImpulse = 0;
  float yImpulse = 0;
  float totalAccelEnergy = 0;
  float totalGyroEnergy = 0;
  int directionChanges = 0;
  float previousTurn = 0;

  for (size_t i = 0; i < sampleCount; ++i) {
    const Sample &sample = gestureSamples[i];
    hall1Change = max(hall1Change, hallChange(sample.hall1, hallBaseline1));
    hall2Change = max(hall2Change, hallChange(sample.hall2, hallBaseline2));
    hall1Delta += sample.hall1 - hallBaseline1;
    hall2Delta += sample.hall2 - hallBaseline2;

    float ax = sample.ax - accelBaselineX;
    float ay = sample.ay - accelBaselineY;
    float accelEnergy = sqrtf(ax * ax + ay * ay);
    float gyroEnergy = sqrtf(sample.gx * sample.gx + sample.gy * sample.gy + sample.gz * sample.gz);
    totalAccelEnergy += accelEnergy;
    totalGyroEnergy += gyroEnergy;
    xImpulse += ax;
    yImpulse += ay;

    // A circular motion has repeated signed turns, unlike a straight swipe.
    float turn = sample.gx + sample.gy + sample.gz;
    if (fabsf(turn) > 0.35 && previousTurn != 0 && turn * previousTurn < 0) {
      directionChanges++;
    }
    if (fabsf(turn) > 0.35) previousTurn = turn;
  }

  float averageAccel = totalAccelEnergy / sampleCount;
  float averageGyro = totalGyroEnergy / sampleCount;
  if (hall1Change > hall1NoiseThreshold && hall2Change > hall2NoiseThreshold) {
    // This assumes bending toward each magnet increases its ADC value.
    Serial.print("pinch: ");
    Serial.println((hall1Delta + hall2Delta) > 0 ? "in" : "out");
  }
  if (hall1Change > hall1NoiseThreshold || hall2Change > hall2NoiseThreshold) 
      Serial.println("finger bend");
  if (directionChanges >= 2 && averageGyro > 0.7) 
      Serial.println("circle");
  if (averageAccel < 0.45 && averageGyro > 0.7)   
      Serial.println("rotation");
  if (fabsf(xImpulse) >= fabsf(yImpulse)) {
    Serial.print("swipe: ");
    Serial.println(xImpulse < 0 ? "left" : "right");
  }
  if (yImpulse < 0) {
    Serial.println("swipe down");
  } else {
    Serial.println("swipe up");
  }
}

void finishGesture() {
  if (!gestureActive) return;
  Serial.print("Gesture: ");
  Serial.print("(samples=");
  Serial.print(sampleCount);
  Serial.println(")");
  classifyGesture();
  gestureActive = false;
  sampleCount = 0;
}

bool gestureStartDetect(const Sample &sample){
  float motion = sqrtf(sample.ax * sample.ax + sample.ay * sample.ay + sample.az * sample.az);
  float rotation = sqrtf(sample.gx * sample.gx + sample.gy * sample.gy + sample.gz * sample.gz);
  float motionWindowMean = 0;
  float hall1WindowMean = 0;
  float hall2WindowMean = 0;
  float rotationWindowMean = 0;

    bool motionWindowExceededNoise = windowMeanExceeded(
      motion, motionWindow, &motionWindowCount, &motionWindowIndex,
      motionNoiseThreshold, &motionWindowMean);
    bool hall1WindowExceededNoise = windowMeanExceeded(
      hallChange(sample.hall1, hallBaseline1), hall1Window, &hall1WindowCount,
      &hall1WindowIndex, hall1NoiseThreshold, &hall1WindowMean);
    bool hall2WindowExceededNoise = windowMeanExceeded(
      hallChange(sample.hall2, hallBaseline2), hall2Window, &hall2WindowCount,
      &hall2WindowIndex, hall2NoiseThreshold, &hall2WindowMean);
    bool rotationWindowExceededNoise = windowMeanExceeded(
      rotation, rotationWindow, &rotationWindowCount, &rotationWindowIndex,
      rotationNoiseThreshold, &rotationWindowMean);

  bool moving = motionWindowExceededNoise || hall1WindowExceededNoise ||
                hall2WindowExceededNoise || rotationWindowExceededNoise;

  if (!gestureActive && moving) {
  
    Serial.print("motion: ");
    Serial.print(motion);
    Serial.print(", motionThreshold: ");
    Serial.println(motionNoiseThreshold);
    
    Serial.print("hall1: ");
    Serial.print(hallChange(sample.hall1, hallBaseline1));
    Serial.print(", hall1Threshold: ");
    Serial.println(hall1NoiseThreshold);
    
    Serial.print("hall2: ");
    Serial.print(hallChange(sample.hall2, hallBaseline2));
    Serial.print(", hall2Threshold: ");
    Serial.println(hall2NoiseThreshold);
    
    Serial.print("rotation: ");
    Serial.print(rotation);
    Serial.print(", rotationThreshold: ");
    Serial.println(rotationNoiseThreshold);
  }
    
    return moving;
  }
  
void processSample(const Sample &sample) {
  float ax = sample.ax - accelBaselineX;
  float ay = sample.ay - accelBaselineY;
  float az = sample.az - accelBaselineZ;
  float motion = sqrtf(ax * ax + ay * ay + az * az);
  float rotation = sqrtf(sample.gx * sample.gx + sample.gy * sample.gy + sample.gz * sample.gz);
  bool moving = gestureStartDetect(sample);

  if (!gestureActive && moving) {
    gestureActive = true;
    sampleCount = 0;
    lastMotionTime = sample.time;
    Serial.println("Gesture started");
  }
  if (!gestureActive) return;

  if (sampleCount < MAX_SAMPLES) gestureSamples[sampleCount++] = sample;
  if (moving) lastMotionTime = sample.time;
  if (sample.time - lastMotionTime >= QUIET_TIME_US ||
      sample.time - gestureSamples[0].time >= MAX_GESTURE_US || sampleCount >= MAX_SAMPLES) {
    finishGesture();
  }
}

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

  float calibrationMotion[CALIBRATION_SAMPLES];
  float calibrationRotation[CALIBRATION_SAMPLES];
  float calibrationHall1[CALIBRATION_SAMPLES];
  float calibrationHall2[CALIBRATION_SAMPLES];
  Serial.println("Measuring idle sensor noise: keep the hand still for 1 second...");
  for (size_t i = 0; i < CALIBRATION_SAMPLES; ++i) {
    sensors_event_t a, g, temp;
    mpu.getEvent(&a, &g, &temp);
    calibrationMotion[i] = sqrtf(a.acceleration.x * a.acceleration.x + a.acceleration.y * a.acceleration.y + a.acceleration.z * a.acceleration.z);
    calibrationRotation[i] = sqrtf(g.gyro.x * g.gyro.x + g.gyro.y * g.gyro.y + g.gyro.z * g.gyro.z);
    calibrationHall1[i] = hallChange(analogRead(HALL_SENSOR_PIN), hallBaseline1);
    calibrationHall2[i] = hallChange(analogRead(HALL_SENSOR_PIN_2), hallBaseline2);
    delay(10);
  }

  float motionMean = 0;
  float motionStandardDeviation = 0;
  float rotationMean = 0;
  float rotationStandardDeviation = 0;
  float hall1Mean = 0;
  float hall1StandardDeviation = 0;
  float hall2Mean = 0;
  float hall2StandardDeviation = 0;
  calculateCalibrationThreshold(calibrationMotion, CALIBRATION_SAMPLES,
                                &motionMean, &motionStandardDeviation,
                                &motionNoiseThreshold);
  calculateCalibrationThreshold(calibrationRotation, CALIBRATION_SAMPLES,
                                &rotationMean, &rotationStandardDeviation,
                                &rotationNoiseThreshold);
  calculateCalibrationThreshold(calibrationHall1, CALIBRATION_SAMPLES,
                                &hall1Mean, &hall1StandardDeviation,
                                &hall1NoiseThreshold);
  calculateCalibrationThreshold(calibrationHall2, CALIBRATION_SAMPLES,
                                &hall2Mean, &hall2StandardDeviation,
                                &hall2NoiseThreshold);

  Serial.println("Calibration complete. Thresholds (mean + 4*sd):");
  Serial.print("motion mean=");
  Serial.print(motionMean, 3);
  Serial.print(" sd=");
  Serial.print(motionStandardDeviation, 3);
  Serial.print(" threshold=");
  Serial.println(motionNoiseThreshold, 3);
  Serial.print("rotation mean=");
  Serial.print(rotationMean, 3);
  Serial.print(" sd=");
  Serial.print(rotationStandardDeviation, 3);
  Serial.print(" threshold=");
  Serial.println(rotationNoiseThreshold, 3);
  Serial.print("hall1 mean=");
  Serial.print(hall1Mean, 3);
  Serial.print(" sd=");
  Serial.print(hall1StandardDeviation, 3);
  Serial.print(" threshold=");
  Serial.println(hall1NoiseThreshold, 3);
  Serial.print("hall2 mean=");
  Serial.print(hall2Mean, 3);
  Serial.print(" sd=");
  Serial.print(hall2StandardDeviation, 3);
  Serial.print(" threshold=");
  Serial.println(hall2NoiseThreshold, 3);
  Serial.println("Move your hand to produce a gesture.\n");
}

void loop() {
  unsigned long currentTime = micros();
  if (currentTime - lastSampleTime >= SAMPLE_INTERVAL_US) {
    lastSampleTime = currentTime;
    sensors_event_t a, g, temp;
    mpu.getEvent(&a, &g, &temp);
    Sample sample = {
      currentTime, a.acceleration.x, a.acceleration.y, a.acceleration.z,
      g.gyro.x, g.gyro.y, g.gyro.z,
      analogRead(HALL_SENSOR_PIN), analogRead(HALL_SENSOR_PIN_2)
    };
    processSample(sample);
  }
}