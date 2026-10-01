#include <Arduino.h>
#include <Adafruit_MPU6050.h>
#include <Adafruit_Sensor.h>
#include <MadgwickAHRS.h>
#include <Wire.h>

//Define DEBUG to enable debug output
#define DEBUG 1

// Define I2C pins
#define SDA_PIN 41  // GPIO41
#define SCL_PIN 42  // GPIO42

// Hall sensors are assumed to be connected to ADC1_0 and ADC1_1.
#define HALL_SENSOR_PIN 1  // ADC1_0 (GPIO1)
#define HALL_SENSOR_PIN_2 2  // ADC1_1 (GPIO2)

// Create MPU6050 sensor object
Adafruit_MPU6050 mpu;

// Create Madgwick filter object
Madgwick filter;

// Constants for sample
const unsigned long SAMPLE_INTERVAL_US = 10000; // 100 Hz
const unsigned long MAX_GESTURE_US = 2500000; // hard upper bound: 2.5 seconds
const unsigned long QUIET_TIME_US = 180000; // end after 180 ms below threshold
const float GRAVITY = 9.80665f; // Standard gravity in m/s^2
const int CALIBRATION_SAMPLES = 100; // Number of samples for calibration
const size_t MOTION_WINDOW_SIZE = 25; // 250 ms at 100 Hz
const size_t HALL_WINDOW_OVERLAP = 10;
const size_t HALL_WINDOW_STEP = MOTION_WINDOW_SIZE - HALL_WINDOW_OVERLAP;
const size_t HALL_HISTORY_SIZE = MOTION_WINDOW_SIZE + HALL_WINDOW_STEP;
const size_t MAX_SAMPLES = MAX_GESTURE_US / SAMPLE_INTERVAL_US;

// Sample variables
unsigned long lastSampleTime = 0;

//Baselines for accelerometer and hall sensor readings
float hallBaseline1 = 0;
float hallBaseline2 = 0;
float accelBaselineX = 0;
float accelBaselineY = 0;
float accelBaselineZ = 0;
float gyroBaselineX = 0;
float gyroBaselineY = 0;
float gyroBaselineZ = 0;
float motionNoiseThreshold = 0;
float hall1NoiseThreshold = 0;
float hall2NoiseThreshold = 0;
float rotationNoiseThreshold = 0;

//Gesture start detection
float motionWindow[MOTION_WINDOW_SIZE] = {0};
float hall1Window[HALL_HISTORY_SIZE] = {0};
float hall2Window[HALL_HISTORY_SIZE] = {0};
float rotationWindow[MOTION_WINDOW_SIZE] = {0};
size_t motionWindowCount = 0;
size_t motionWindowIndex = 0;
size_t hall1WindowCount = 0;
size_t hall2WindowCount = 0;
size_t rotationWindowCount = 0;
size_t hall1WindowIndex = 0;
size_t hall2WindowIndex = 0;
size_t rotationWindowIndex = 0;
size_t hall1SamplesSinceComparison = HALL_WINDOW_STEP;
size_t hall2SamplesSinceComparison = HALL_WINDOW_STEP;

//Test
#if DEBUG
int count = 0;
#endif

struct Sample {
  unsigned long time;
  float ax, ay, az;
  float gx, gy, gz;
  int hall1, hall2;
};

struct GesturePose {
    float roll;
    float pitch;
};

//Gesture Classification
size_t sampleCount = 0;
bool gestureActive = false;
unsigned long lastMotionTime = 0;
Sample gestureSamples[MAX_SAMPLES];
GesturePose gesturePoses[MAX_SAMPLES];
Sample gestureSampleBuffer[MOTION_WINDOW_SIZE];
GesturePose gesturePosesBuffer[MOTION_WINDOW_SIZE];
size_t gestureSampleBufferCount = 0;
size_t gestureSampleBufferIndex = 0;

void classifyGesture();
void processSample(const Sample &sample);
void finishGesture();

void appendGestureSample(const Sample &sample, const GesturePose &pose) {
    if (sampleCount >= MAX_SAMPLES) return;

    gestureSamples[sampleCount] = sample;
    gesturePoses[sampleCount] = pose;
    ++sampleCount;
}

void bufferPreGestureSample(const Sample &sample, const GesturePose &pose) {
    gestureSampleBuffer[gestureSampleBufferIndex] = sample;
    gesturePosesBuffer[gestureSampleBufferIndex] = pose;
    gestureSampleBufferIndex = (gestureSampleBufferIndex + 1) % MOTION_WINDOW_SIZE;
    if (gestureSampleBufferCount < MOTION_WINDOW_SIZE) {
        ++gestureSampleBufferCount;
    }
}

void appendBufferedGestureSamples() {
    const size_t firstSample = (gestureSampleBufferIndex + MOTION_WINDOW_SIZE
        - gestureSampleBufferCount) % MOTION_WINDOW_SIZE;

    for (size_t i = 0; i < gestureSampleBufferCount; ++i) {
        const size_t bufferIndex = (firstSample + i) % MOTION_WINDOW_SIZE;
        appendGestureSample(gestureSampleBuffer[bufferIndex], gesturePosesBuffer[bufferIndex]);
    }

    gestureSampleBufferCount = 0;
    gestureSampleBufferIndex = 0;
}


//Using Madgwick filter to get orientation from accelerometer and gyroscope data
void getOrientation(const Sample &sample) {
    float ax, ay, az;
    float gx, gy, gz;
    float roll, pitch, heading;

    ax = sample.ax;
    ay = sample.ay;
    az = sample.az;
    gx = (sample.gx / PI) * 180; // Convert to degrees/s
    gy = (sample.gy / PI) * 180; // Convert to degrees/s
    gz = (sample.gz / PI) * 180; // Convert to degrees/s

    // update the filter, which computes orientation
    filter.updateIMU(gx, gy, gz, ax, ay, az);

#if DEBUG
    // print the heading, pitch and roll
    /*
    roll = filter.getRoll();
    pitch = filter.getPitch();
    Serial.print("Orientation: ");
    Serial.print("Roll: ");
    Serial.print(roll);
    Serial.print(" Pitch: ");
    Serial.print(pitch);
    */
#endif

} 

void removeGravity(const Sample &sample,
                float &linearX, float &linearY, float &linearZ) {
    float roll = filter.getRollRadians();
    float pitch = filter.getPitchRadians();

    // Gravity direction in the sensor frame.
    float gravityX = -sinf(pitch);
    float gravityY = sinf(roll) * cosf(pitch);
    float gravityZ = cosf(roll) * cosf(pitch);

    linearX = sample.ax - gravityX * GRAVITY;
    linearY = sample.ay - gravityY * GRAVITY;
    linearZ = sample.az - gravityZ * GRAVITY;
}

// Calculate the threshold for a noise. Threshold = 4 * standard deviation.
void calculateCalibrationThreshold(const float *values, size_t count, float *threshold) {
    float mean = 0;
    float standardDeviation = 0;

    float sum = 0;
    float sumSquares = 0;
    for (size_t i = 0; i < count; ++i) {
    sum += values[i];
    sumSquares += values[i] * values[i];
    }
    mean = sum / count;
    float variance = (sumSquares / count) - (mean * mean);
    standardDeviation = sqrtf(fmaxf(variance, 0.0f));
    *threshold = mean + (4.0f * standardDeviation);
}

// Returns true if the mean of the IMU values in the window exceeds its threshold.
bool isNoiseThresholdExceededIMU(float value, float *window, size_t *windowCount,
                                size_t *windowIndex, float noiseThreshold) {

    window[*windowIndex] = value;
    *windowIndex = (*windowIndex + 1) % MOTION_WINDOW_SIZE;
    if (*windowCount < MOTION_WINDOW_SIZE) ++(*windowCount);

    if (*windowCount < MOTION_WINDOW_SIZE) {
        return false;
    }

    float sum = 0;
    for (size_t i = 0; i < MOTION_WINDOW_SIZE; ++i) {
        sum += window[i];
    }

    return (sum / MOTION_WINDOW_SIZE) > noiseThreshold;
}

// Compare Hall means over two 25-sample windows with a 10-sample overlap.
bool isNoiseThresholdExceededHall(int value, float *window, size_t *windowCount,
                                   size_t *windowIndex,
                                   size_t *samplesSinceComparison,
                                   float noiseThreshold) {
    window[*windowIndex] = value;
    *windowIndex = (*windowIndex + 1) % HALL_HISTORY_SIZE;
    if (*windowCount < HALL_HISTORY_SIZE) ++(*windowCount);

    if (*windowCount < HALL_HISTORY_SIZE) return false;

    ++(*samplesSinceComparison);
    if (*samplesSinceComparison < HALL_WINDOW_STEP) return false;
    *samplesSinceComparison = 0;

    const size_t currentWindowStart = (*windowIndex + HALL_HISTORY_SIZE
        - MOTION_WINDOW_SIZE) % HALL_HISTORY_SIZE;
    const size_t previousWindowStart = (currentWindowStart + HALL_HISTORY_SIZE
        - HALL_WINDOW_STEP) % HALL_HISTORY_SIZE;
    float currentWindowSum = 0;
    float previousWindowSum = 0;

    for (size_t i = 0; i < MOTION_WINDOW_SIZE; ++i) {
        currentWindowSum += window[(currentWindowStart + i) % HALL_HISTORY_SIZE];
        previousWindowSum += window[(previousWindowStart + i) % HALL_HISTORY_SIZE];
    }

    const float currentWindowMean = currentWindowSum / MOTION_WINDOW_SIZE;
    const float previousWindowMean = previousWindowSum / MOTION_WINDOW_SIZE;

#if DEBUG
    if (count % 100 == 0) {
        Serial.print("hall1Change: ");
        Serial.print(fabsf(currentWindowMean - previousWindowMean));
        Serial.print(", hall1Threshold: ");
        Serial.println(hall1NoiseThreshold);
        Serial.print("hall2Change: ");
        Serial.print(fabsf(currentWindowMean - previousWindowMean));
        Serial.print(", hall2Threshold: ");
        Serial.println(hall2NoiseThreshold);
    }
#endif


    return fabsf(currentWindowMean - previousWindowMean) > noiseThreshold;
}

bool gestureDetect(Sample &sample) {
    float motion = sqrtf(sample.ax * sample.ax + sample.ay * sample.ay + sample.az * sample.az);
    float rotation = sqrtf(sample.gx * sample.gx + sample.gy * sample.gy + sample.gz * sample.gz);

    // Check if the motion, rotation, and hall sensor values exceed the noise thresholds.
    bool motionWindowExceededNoise = isNoiseThresholdExceededIMU(
        motion, motionWindow, &motionWindowCount, &motionWindowIndex,
        motionNoiseThreshold);
    bool hall1WindowExceededNoise = isNoiseThresholdExceededHall(
        sample.hall1, hall1Window, &hall1WindowCount,
        &hall1WindowIndex, &hall1SamplesSinceComparison,
        hall1NoiseThreshold);
    bool hall2WindowExceededNoise = isNoiseThresholdExceededHall(
        sample.hall2, hall2Window, &hall2WindowCount,
        &hall2WindowIndex, &hall2SamplesSinceComparison,
        hall2NoiseThreshold);
    bool rotationWindowExceededNoise = isNoiseThresholdExceededIMU(
        rotation, rotationWindow, &rotationWindowCount, &rotationWindowIndex,
        rotationNoiseThreshold);

    bool moving = motionWindowExceededNoise || hall1WindowExceededNoise ||
    hall2WindowExceededNoise || rotationWindowExceededNoise;

    #if DEBUG
    if (!gestureActive && moving) {
        Serial.print("motion: ");
        Serial.print(motionWindowExceededNoise);
        Serial.print(", motionThreshold: ");
        Serial.println(motionNoiseThreshold);

        Serial.print("hall1: ");
        Serial.print(hall1WindowExceededNoise);
        Serial.print(", hall1Threshold: ");
        Serial.println(hall1NoiseThreshold);

        Serial.print("hall2: ");
        Serial.print(hall2WindowExceededNoise);
        Serial.print(", hall2Threshold: ");
        Serial.println(hall2NoiseThreshold);

        Serial.print("rotation: ");
        Serial.print(rotationWindowExceededNoise);
        Serial.print(", rotationThreshold: ");
        Serial.println(rotationNoiseThreshold);
    }
    #endif

    return moving;
}

void processSample(Sample &sample) {
    
    //Get Orientation and Remove Gravity 
    getOrientation(sample);
    removeGravity(sample, sample.ax, sample.ay, sample.az);

    //Determine Start of Gesture
    bool moving = gestureDetect(sample);
    const GesturePose pose = {
        filter.getRollRadians(), filter.getPitchRadians()
    };
    

    if (!gestureActive && moving) {
        gestureActive = true;
        sampleCount = 0;
        lastMotionTime = sample.time;
        appendBufferedGestureSamples();

        Serial.println("Gesture started");

    }

    //Buffer pre-gesture samples if no gesture is active and no motion is detected
    if (!gestureActive && !moving) {
        bufferPreGestureSample(sample, pose);
        return;
    }

    appendGestureSample(sample, pose);

    if (moving) lastMotionTime = sample.time;

    if (sample.time - lastMotionTime >= QUIET_TIME_US ||
        sample.time - gestureSamples[0].time >= MAX_GESTURE_US || sampleCount >= MAX_SAMPLES) {
        Serial.println("Gesture finished");
        finishGesture();
    }
}

float hallChange(int value, float baseline) {
  return fabsf(value - baseline);
}

void classifyGesture() {
    if (sampleCount < 3) {
        Serial.println("no gesture detected");
        return;
    }

    float horizontalEnergy = 0.0f;
    float verticalEnergy = 0.0f;
    float rotationEnergy = 0.0f;
    float maxHorizontalAcceleration = 0.0f;
    float maxHorizontalTime = 0.0f;
    float minHorizontalAcceleration = 0.0f;
    float minHorizontalTime = 0.0f;
    float maxVerticalAcceleration = 0.0f;
    float maxVerticalTime = 0.0f;
    float minVerticalAcceleration = 0.0f;
    float minVerticalTime = 0.0f;

    float hallMinimum = 0.0f;
    float hallMaximum = 0.0f;
    float hallStartSum = 0.0f;
    float hallMiddleSum = 0.0f;
    float hallEndSum = 0.0f;
    const size_t hallSegmentLength = (sampleCount / 5) > 0
        ? sampleCount / 5 : 1;
    const size_t hallMiddleStart = (sampleCount - hallSegmentLength) / 2;
    const size_t hallEndStart = sampleCount - hallSegmentLength;

    
#if DEBUG
    float yImpulse = 0.0f;
    float verticalImpulse = 0.0f;
    float minPitch = gesturePoses[0].pitch;
    float maxPitch = gesturePoses[0].pitch;
    float minRoll = gesturePoses[0].roll;
    float maxRoll = gesturePoses[0].roll;
#endif

    for (size_t i = 0; i < sampleCount; ++i) {
        const Sample &sample = gestureSamples[i];
        const GesturePose &pose = gesturePoses[i];
        const float dt = (i == 0)
                ? SAMPLE_INTERVAL_US / 1000000.0f
                : (sample.time - gestureSamples[i - 1].time) / 1000000.0f;

        // Rotate the sensor acceleration into a gravity-aligned frame. X and Y
        // are the two horizontal directions; Z points along gravity.
        const float sinRoll = sinf(pose.roll);
        const float cosRoll = cosf(pose.roll);
        const float sinPitch = sinf(pose.pitch);
        const float cosPitch = cosf(pose.pitch);
        const float worldX = cosPitch * sample.ax
                    + sinPitch * sinRoll * sample.ay
                    + sinPitch * cosRoll * sample.az;
        const float worldY = cosRoll * sample.ay
                            - sinRoll * sample.az;
        const float worldZ = -sinPitch * sample.ax
                    + cosPitch * sinRoll * sample.ay
                    + cosPitch * cosRoll * sample.az;
        const float horizontalAcceleration = worldY;
        const float verticalAcceleration = worldZ;

        const float bend1 = hallChange(sample.hall1, hallBaseline1);
        const float bend2 = hallChange(sample.hall2, hallBaseline2);
        const float hallSeparation = (sample.hall1 + sample.hall2) * 0.5f;

        if (i == 0) {
            hallMinimum = hallSeparation;
            hallMaximum = hallSeparation;
        } else {
            hallMinimum = fminf(hallMinimum, hallSeparation);
            hallMaximum = fmaxf(hallMaximum, hallSeparation);
        }
        if (i < hallSegmentLength) {
            hallStartSum += hallSeparation;
        }
        if (i >= hallMiddleStart && i < hallMiddleStart + hallSegmentLength) {
            hallMiddleSum += hallSeparation;
        }
        if (i >= hallEndStart) {
            hallEndSum += hallSeparation;
        }

#if DEBUG
        Serial.print("gesture sample=");
        Serial.print(i);
        Serial.print(" time_us=");
        Serial.print(sample.time);
        Serial.print(" roll=");
        Serial.print(pose.roll, 4);
        Serial.print(" pitch=");
        Serial.print(pose.pitch, 4);
        Serial.print(" ax=");
        Serial.print(sample.ax, 4);
        Serial.print(" ay=");
        Serial.print(sample.ay, 4);
        Serial.print(" az=");
        Serial.print(sample.az, 4);
        Serial.print(" ax_world=");
        Serial.print(worldX, 4);
        Serial.print(" ay_world=");
        Serial.print(worldY, 4);
        Serial.print(" az_world=");
        Serial.print(worldZ, 4);
        Serial.print(" horizontal_accel=");
        Serial.print(horizontalAcceleration, 4);
        Serial.print(" vertical_accel=");
        Serial.print(verticalAcceleration, 4);
        Serial.print(" bend1=");
        Serial.print(bend1, 4);
        Serial.print(" bend2=");
        Serial.print(bend2, 4);
        Serial.print(" dt=");
        Serial.println(dt, 5);
        
        yImpulse += worldY * dt;
        verticalImpulse += worldZ * dt;
        
        minPitch = fminf(minPitch, pose.pitch);
        maxPitch = fmaxf(maxPitch, pose.pitch);
        minRoll = fminf(minRoll, pose.roll);
        maxRoll = fmaxf(maxRoll, pose.roll);
#endif


        horizontalEnergy += fabsf(horizontalAcceleration) * dt;
        verticalEnergy += fabsf(verticalAcceleration) * dt;
        rotationEnergy += sqrtf(sample.gx * sample.gx + sample.gy * sample.gy + sample.gz * sample.gz) * dt;
        if (horizontalAcceleration > maxHorizontalAcceleration) {
            maxHorizontalTime = sample.time;
            maxHorizontalAcceleration = horizontalAcceleration;
        } else if (horizontalAcceleration < minHorizontalAcceleration) {
            minHorizontalTime = sample.time;
            minHorizontalAcceleration = horizontalAcceleration;
        }
        if (verticalAcceleration > maxVerticalAcceleration) {
            maxVerticalTime = sample.time;
            maxVerticalAcceleration = verticalAcceleration;
        } else if (verticalAcceleration < minVerticalAcceleration) {
            minVerticalTime = sample.time;
            minVerticalAcceleration = verticalAcceleration;
        }
        
    }

#if DEBUG
    const float pitchChange = gesturePoses[sampleCount - 1].pitch
            - gesturePoses[0].pitch;
    const float rollChange = gesturePoses[sampleCount - 1].roll
            - gesturePoses[0].roll;
    const float pitchRange = maxPitch - minPitch;
    const float rollRange = maxRoll - minRoll;

    Serial.println("gesture summary:");
    Serial.print("  samples=");
    Serial.println(sampleCount);
    Serial.print("  yImpulse=");
    Serial.println(yImpulse, 4);
    Serial.print("  verticalImpulse=");
    Serial.println(verticalImpulse, 4);
    Serial.print("  horizontalEnergy=");
    Serial.println(horizontalEnergy, 4);
    Serial.print("  verticalEnergy=");
    Serial.println(verticalEnergy, 4);
    Serial.print("  maxHorizontalAcceleration=");
    Serial.println(maxHorizontalAcceleration, 4);
    Serial.print("  maxVerticalAcceleration=");
    Serial.println(maxVerticalAcceleration, 4);
    Serial.print("  rotationEnergy=");
    Serial.println(rotationEnergy, 4);
    Serial.print("  pitchChange=");
    Serial.println(pitchChange, 4);
    Serial.print("  rollChange=");
    Serial.println(rollChange, 4);
    Serial.print("  pitchRange=");
    Serial.println(pitchRange, 4);
    Serial.print("  rollRange=");
    Serial.println(rollRange, 4);
#endif

    const float hallStartMean = hallStartSum / hallSegmentLength;
    const float hallMiddleMean = hallMiddleSum / hallSegmentLength;
    const float hallEndMean = hallEndSum / hallSegmentLength;
    const float hallRange = hallMaximum - hallMinimum;
    const float hallNoiseRange = 0.5f * (hall1NoiseThreshold + hall2NoiseThreshold);
    const float hallClosedThreshold = hallMinimum + 0.35f * hallRange;
    const float hallOpenThreshold = hallMinimum + 0.65f * hallRange;
    const bool hallDataIsUsable = hallRange > hallNoiseRange;
    const bool startsClosed = hallStartMean <= hallClosedThreshold;
    const bool startsOpen = hallStartMean >= hallOpenThreshold;
    const bool middleClosed = hallMiddleMean <= hallClosedThreshold;
    const bool middleOpen = hallMiddleMean >= hallOpenThreshold;
    const bool endsClosed = hallEndMean <= hallClosedThreshold;
    const bool endsOpen = hallEndMean >= hallOpenThreshold;

    if (hallDataIsUsable && rotationEnergy < 4.0f && horizontalEnergy < 1.0f && verticalEnergy < 1.0f) {
        if (startsClosed && endsOpen) {
            Serial.println("zoom in");
            return;
        }
        if (startsOpen && endsClosed) {
            Serial.println("zoom out");
            return;
        }
        if (startsClosed && middleOpen && endsClosed) {
            Serial.println("potential zoom out");
            return;
        }

        if (startsOpen && middleClosed && endsOpen) {
            Serial.println("potential zoom in");
            return;
        }
    }

    if (rotationEnergy > 4.0f ) {
        Serial.println("rotation");
        return;
    } else if (horizontalEnergy > 1.0f || verticalEnergy > 1.0f) {
        if (horizontalEnergy > verticalEnergy) {
            if (minHorizontalTime < maxHorizontalTime) {
                Serial.println("swipe right");
            } else {
                Serial.println("swipe left");
            }
        } else {
            if (minVerticalTime < maxVerticalTime) {
                Serial.println("swipe down");
            } else {
                Serial.println("swipe up");
            }
        }
    } else {
        Serial.println("no gesture detected");
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

void setup() {
    // Initialize Serial communication
    Serial.begin(115200);
    delay(1000);

    Serial.println("ESP32 MPU-6050 I2C Sensor Reader");
    Serial.println("==================================");

    //Initialize Madgwick filter
    filter.begin(100); // 100 Hz sample rate

    // Initialize I2C on custom pins (SDA=GPIO41, SCL=GPIO42)
    Wire.begin(SDA_PIN, SCL_PIN);

    // Initialize MPU6050
    bool imuConnected = false;
    while (imuConnected == false) {
        if (mpu.begin(0x68, &Wire)) {
            Serial.println("MPU6050 found!");
            imuConnected = true;
        } else {
            Serial.println("MPU6050 not found. Retrying...");
            delay(1000);
        }
    }

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

    // Arrays to store calibration data
    float calibrationMotion[CALIBRATION_SAMPLES];
    float calibrationRotation[CALIBRATION_SAMPLES];
    float calibrationHall1[CALIBRATION_SAMPLES];
    float calibrationHall2[CALIBRATION_SAMPLES];

    // Establish the idle Hall values. Keep the hand still during this period.
    Serial.println("Calibrating sensors: keep the hand still for 1 second...");
    for (int i = 0; i < CALIBRATION_SAMPLES; ++i) {
    unsigned long currentTime = micros();
    sensors_event_t a, g, temp;
    mpu.getEvent(&a, &g, &temp);
    Sample sample = {
        currentTime, a.acceleration.x, a.acceleration.y, a.acceleration.z,
        g.gyro.x, g.gyro.y, g.gyro.z,
        analogRead(HALL_SENSOR_PIN), analogRead(HALL_SENSOR_PIN_2)
    };
    getOrientation(sample);

    //Alters the sample values to remove the effect of gravity on the accelerometer readings
    removeGravity(sample, sample.ax, sample.ay, sample.az);

    //Calculate Baselines
    accelBaselineX += sample.ax;
    accelBaselineY += sample.ay;
    accelBaselineZ += sample.az;
    gyroBaselineX += sample.gx;
    gyroBaselineY += sample.gy;
    gyroBaselineZ += sample.gz;
    hallBaseline1 += sample.hall1;
    hallBaseline2 += sample.hall2;

    //Populate arrays for  calculation of stationary noise to use for gesture start/end detection thresholds
    calibrationMotion[i] = sqrtf(sample.ax * sample.ax + sample.ay * sample.ay + sample.az * sample.az);
    calibrationRotation[i] = sqrtf(g.gyro.x * g.gyro.x + g.gyro.y * g.gyro.y + g.gyro.z * g.gyro.z);
    calibrationHall1[i] = analogRead(HALL_SENSOR_PIN);
    calibrationHall2[i] = analogRead(HALL_SENSOR_PIN_2);

    delay(10);
    }

    //Average of the 100 samples
    accelBaselineX /= CALIBRATION_SAMPLES;
    accelBaselineY /= CALIBRATION_SAMPLES;
    accelBaselineZ /= CALIBRATION_SAMPLES;
    gyroBaselineX /= CALIBRATION_SAMPLES;
    gyroBaselineY /= CALIBRATION_SAMPLES;
    gyroBaselineZ /= CALIBRATION_SAMPLES;
    hallBaseline1 /= CALIBRATION_SAMPLES;
    hallBaseline2 /= CALIBRATION_SAMPLES;


    //calculate noise threshold
    calculateCalibrationThreshold(calibrationMotion, CALIBRATION_SAMPLES,
                                &motionNoiseThreshold);
    calculateCalibrationThreshold(calibrationRotation, CALIBRATION_SAMPLES,
                                &rotationNoiseThreshold);
    calculateCalibrationThreshold(calibrationHall1, CALIBRATION_SAMPLES,
                                &hall1NoiseThreshold);
    calculateCalibrationThreshold(calibrationHall2, CALIBRATION_SAMPLES,
                                &hall2NoiseThreshold);
    hall1NoiseThreshold -= hallBaseline1;
    hall2NoiseThreshold -= hallBaseline2;

    Serial.println("Calibration complete. Thresholds (mean + 4*sd, Hall thresholds are change thresholds):");
    Serial.print(" Motion threshold=");
    Serial.println(motionNoiseThreshold, 3);
    Serial.print(" Rotation threshold=");
    Serial.println(rotationNoiseThreshold, 3);
    Serial.print(" Hall1 threshold=");
    Serial.println(hall1NoiseThreshold, 3);
    Serial.print(" Hall2 threshold=");
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

#if DEBUG
        count++;
        if (count % 100 == 0) {
            /*
            Serial.print("Sample: ");
            Serial.print("ax=");  
            Serial.print(sample.ax);
            Serial.print(" ay=");  
            Serial.print(sample.ay);
            Serial.print(" az=");  
            Serial.print(sample.az);
            Serial.print(" gx=");  
            Serial.print(sample.gx);
            Serial.print(" gy=");  
            Serial.print(sample.gy);
            Serial.print(" gz=");  
            Serial.println(sample.gz);
            Serial.print(" roll=");
            Serial.print(filter.getRollRadians());
            Serial.print(" pitch=");
            Serial.println(filter.getPitchRadians());
            */
            Serial.print("Hall1: ");
            Serial.print(sample.hall1);
            Serial.print(" Hall2: ");
            Serial.println(sample.hall2);

        }
#endif

    }
}