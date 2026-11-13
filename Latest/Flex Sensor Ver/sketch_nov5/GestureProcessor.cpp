#include "GestureProcessor.h"

#include <Arduino.h>
#include "MadgwickAHRS.h"
#include <math.h>

// ============================================================
// CONFIGURATION
// ============================================================

#define GESTURE_DEBUG 0

static constexpr float GRAVITY = 9.80665f;

static constexpr unsigned long SAMPLE_INTERVAL_US = 14286;
static constexpr unsigned long MAX_GESTURE_US = 2500000;
static constexpr unsigned long QUIET_TIME_US = 180000;

static constexpr size_t CALIBRATION_SAMPLES = 70;

static constexpr size_t MOTION_WINDOW_SIZE = 18;
static constexpr size_t FLEX_WINDOW_OVERLAP = 7;

static constexpr size_t FLEX_WINDOW_STEP =
    MOTION_WINDOW_SIZE - FLEX_WINDOW_OVERLAP;

static constexpr size_t FLEX_HISTORY_SIZE =
    MOTION_WINDOW_SIZE + FLEX_WINDOW_STEP;

static constexpr size_t MAX_SAMPLES =
    MAX_GESTURE_US / SAMPLE_INTERVAL_US + 1;

static constexpr float MIN_MOTION_THRESHOLD = 2.0f;
static constexpr float MIN_ROTATION_THRESHOLD = 1.0f;
static constexpr float MIN_FLEX_THRESHOLD = 20.0f;

static constexpr int START_CONFIRMATION_COUNT = 3;


// ============================================================
// FILTER AND CALIBRATION STATE
// ============================================================

static Madgwick filter;

static bool calibrationComplete = false;
static size_t calibrationCount = 0;
static size_t madgwickStabalizationCount = 0;

static float flexBaseline1 = 0;
static float flexBaseline2 = 0;

static float accelBaselineX = 0;
static float accelBaselineY = 0;
static float accelBaselineZ = 0;

static float gyroBaselineX = 0;
static float gyroBaselineY = 0;
static float gyroBaselineZ = 0;

static float motionNoiseThreshold = 0;
static float rotationNoiseThreshold = 0;
static float flex1NoiseThreshold = 0;
static float flex2NoiseThreshold = 0;

static float calibrationMotion[CALIBRATION_SAMPLES];
static float calibrationRotation[CALIBRATION_SAMPLES];
static float calibrationFlex1[CALIBRATION_SAMPLES];
static float calibrationFlex2[CALIBRATION_SAMPLES];


// ============================================================
// GESTURE STATE
// ============================================================

struct GesturePose {
    float roll;
    float pitch;
};

static bool gestureActive = false;
static size_t sampleCount = 0;

static unsigned long lastMotionTime = 0;

static Sample gestureSamples[MAX_SAMPLES];
static GesturePose gesturePoses[MAX_SAMPLES];

static Sample gestureSampleBuffer[MOTION_WINDOW_SIZE];
static GesturePose gesturePosesBuffer[MOTION_WINDOW_SIZE];

static size_t gestureSampleBufferCount = 0;
static size_t gestureSampleBufferIndex = 0;

static int startConfirmationCount = 0;


// ============================================================
// MOTION DETECTION WINDOWS
// ============================================================

static float motionWindow[MOTION_WINDOW_SIZE] = {};
static float rotationWindow[MOTION_WINDOW_SIZE] = {};

static float flex1Window[FLEX_HISTORY_SIZE] = {};
static float flex2Window[FLEX_HISTORY_SIZE] = {};

static size_t motionWindowCount = 0;
static size_t rotationWindowCount = 0;

static size_t flex1WindowCount = 0;
static size_t flex2WindowCount = 0;

static size_t motionWindowIndex = 0;
static size_t rotationWindowIndex = 0;

static size_t flex1WindowIndex = 0;
static size_t flex2WindowIndex = 0;

static size_t flex1SamplesSinceComparison = FLEX_WINDOW_STEP;
static size_t flex2SamplesSinceComparison = FLEX_WINDOW_STEP;


// ============================================================
// INITIALIZATION
// ============================================================

void initializeGestureProcessor(float sampleRateHz) {

    filter.begin(sampleRateHz);

    calibrationComplete = false;
    calibrationCount = 0;

    gestureActive = false;
    sampleCount = 0;

    gestureSampleBufferCount = 0;
    gestureSampleBufferIndex = 0;

    startConfirmationCount = 0;

    motionWindowCount = 0;
    rotationWindowCount = 0;

    flex1WindowCount = 0;
    flex2WindowCount = 0;

    motionWindowIndex = 0;
    rotationWindowIndex = 0;

    flex1WindowIndex = 0;
    flex2WindowIndex = 0;

    flex1SamplesSinceComparison = FLEX_WINDOW_STEP;
    flex2SamplesSinceComparison = FLEX_WINDOW_STEP;

    flexBaseline1 = 0;
    flexBaseline2 = 0;

    accelBaselineX = 0;
    accelBaselineY = 0;
    accelBaselineZ = 0;

    gyroBaselineX = 0;
    gyroBaselineY = 0;
    gyroBaselineZ = 0;

    Serial.println();
    Serial.println("[GESTURE] Processor initialized");
    Serial.printf(
        "[GESTURE] Sampling frequency: %.1f Hz\n",
        sampleRateHz
    );
}


bool gestureCalibrationComplete() {
    return calibrationComplete;
}


// ============================================================
// ORIENTATION
// ============================================================

static void getOrientation(const Sample &sample) {

    // MPU6050 gyro readings are in radians/second.
    // Madgwick expects degrees/second.

    float gx = sample.gx * 180.0f / PI;
    float gy = sample.gy * 180.0f / PI;
    float gz = sample.gz * 180.0f / PI;

    filter.updateIMU(
        gx,
        gy,
        gz,
        sample.ax,
        sample.ay,
        sample.az
    );
}


// ============================================================
// GRAVITY REMOVAL
// ============================================================

static void removeGravity(
    const Sample &sample,
    float &linearX,
    float &linearY,
    float &linearZ
) {

    float roll = filter.getRollRadians();
    float pitch = filter.getPitchRadians();

    float gravityX = -sinf(pitch);
    float gravityY = sinf(roll) * cosf(pitch);
    float gravityZ = cosf(roll) * cosf(pitch);

    linearX = sample.ax - gravityX * GRAVITY;
    linearY = sample.ay - gravityY * GRAVITY;
    linearZ = sample.az - gravityZ * GRAVITY;
}


// ============================================================
// CALIBRATION THRESHOLDS
// ============================================================

static float calculateThreshold(
    const float *values,
    size_t count,
    bool flexSensor,
    float minimumThreshold
) {

    float sum = 0;
    float sumSquares = 0;

    for (size_t i = 0; i < count; i++) {
        sum += values[i];
        sumSquares += values[i] * values[i];
    }

    float mean = sum / count;

    float variance =
        sumSquares / count - mean * mean;

    float standardDeviation =
        sqrtf(fmaxf(variance, 0.0f));

    float threshold;

    if (flexSensor) {
        threshold = 4.0f * standardDeviation;
    } else {
        threshold = mean + 4.0f * standardDeviation;
    }

    return fmaxf(threshold, minimumThreshold);
}


// ============================================================
// CALIBRATION
// ============================================================
void stabalizeMadgwick(const Sample &rawSample){
    getOrientation(rawSample);
    return;
}

bool calibrateGestureProcessor(const Sample &rawSample) {

    if (madgwickStabalizationCount < 200) {
        stabalizeMadgwick(rawSample);
        madgwickStabalizationCount++;
        return false;
    }

    if (calibrationComplete) {
        return true;
    }

    if (calibrationCount >= CALIBRATION_SAMPLES) {
        return calibrationComplete;
    }

    // Preserve raw input.
    Sample sample = rawSample;

    getOrientation(sample);

    removeGravity(
        sample,
        sample.ax,
        sample.ay,
        sample.az
    );

    accelBaselineX += sample.ax;
    accelBaselineY += sample.ay;
    accelBaselineZ += sample.az;

    gyroBaselineX += sample.gx;
    gyroBaselineY += sample.gy;
    gyroBaselineZ += sample.gz;

    flexBaseline1 += sample.flex1;
    flexBaseline2 += sample.flex2;

    calibrationMotion[calibrationCount] = sqrtf(
        sample.ax * sample.ax +
        sample.ay * sample.ay +
        sample.az * sample.az
    );

    calibrationRotation[calibrationCount] = sqrtf(
        sample.gx * sample.gx +
        sample.gy * sample.gy +
        sample.gz * sample.gz
    );

    calibrationFlex1[calibrationCount] = sample.flex1;
    calibrationFlex2[calibrationCount] = sample.flex2;

    calibrationCount++;

    if (calibrationCount < CALIBRATION_SAMPLES) {
        return false;
    }

    const float divisor =
        static_cast<float>(CALIBRATION_SAMPLES);

    accelBaselineX /= divisor;
    accelBaselineY /= divisor;
    accelBaselineZ /= divisor;

    gyroBaselineX /= divisor;
    gyroBaselineY /= divisor;
    gyroBaselineZ /= divisor;

    flexBaseline1 /= divisor;
    flexBaseline2 /= divisor;

    Serial.printf("AccelBaseline X: %.3f\n", accelBaselineX);
    Serial.printf("AccelBaseline Y: %.3f\n",accelBaselineY);
    Serial.printf("AccelBaseline Z: %.3f\n",accelBaselineZ);
    Serial.printf("gyroBaselineX X: %.3f\n",gyroBaselineX);
    Serial.printf("gyroBaselineY X: %.3f\n",gyroBaselineY);
    Serial.printf("gyroBaselineZ X: %.3f\n",gyroBaselineZ);

    motionNoiseThreshold = calculateThreshold(
        calibrationMotion,
        CALIBRATION_SAMPLES,
        false,
        MIN_MOTION_THRESHOLD
    );

    rotationNoiseThreshold = calculateThreshold(
        calibrationRotation,
        CALIBRATION_SAMPLES,
        false,
        MIN_ROTATION_THRESHOLD
    );

    flex1NoiseThreshold = calculateThreshold(
        calibrationFlex1,
        CALIBRATION_SAMPLES,
        true,
        MIN_FLEX_THRESHOLD
    );

    flex2NoiseThreshold = calculateThreshold(
        calibrationFlex2,
        CALIBRATION_SAMPLES,
        true,
        MIN_FLEX_THRESHOLD
    );

    calibrationComplete = true;

    Serial.println();
    Serial.println("========== GESTURE CALIBRATION ==========");

    Serial.printf(
        "Motion threshold: %.3f\n",
        motionNoiseThreshold
    );

    Serial.printf(
        "Rotation threshold: %.3f\n",
        rotationNoiseThreshold
    );

    Serial.printf(
        "Flex1 threshold: %.3f\n",
        flex1NoiseThreshold
    );

    Serial.printf(
        "Flex2 threshold: %.3f\n",
        flex2NoiseThreshold
    );

    Serial.println("[GESTURE] Calibration complete");
    Serial.println("=========================================");

    return true;
}


// ============================================================
// IMU WINDOW DETECTION
// ============================================================

static bool isNoiseThresholdExceededIMU(
    float value,
    float *window,
    size_t *windowCount,
    size_t *windowIndex,
    float threshold
) {

    window[*windowIndex] = value;

    *windowIndex =
        (*windowIndex + 1) % MOTION_WINDOW_SIZE;

    if (*windowCount < MOTION_WINDOW_SIZE) {
        ++(*windowCount);
    }

    if (*windowCount < MOTION_WINDOW_SIZE) {
        return false;
    }

    float sum = 0;

    for (size_t i = 0; i < MOTION_WINDOW_SIZE; i++) {
        sum += window[i];
    }

    return (sum / MOTION_WINDOW_SIZE) > threshold;
}


// ============================================================
// FLEX WINDOW DETECTION
// ============================================================

static bool isNoiseThresholdExceededFlex(
    int value,
    float *window,
    size_t *windowCount,
    size_t *windowIndex,
    size_t *samplesSinceComparison,
    float threshold
) {

    window[*windowIndex] = value;

    *windowIndex =
        (*windowIndex + 1) % FLEX_HISTORY_SIZE;

    if (*windowCount < FLEX_HISTORY_SIZE) {
        ++(*windowCount);
    }

    if (*windowCount < FLEX_HISTORY_SIZE) {
        return false;
    }

    ++(*samplesSinceComparison);

    if (*samplesSinceComparison < FLEX_WINDOW_STEP) {
        return false;
    }

    *samplesSinceComparison = 0;

    const size_t currentStart =
        (*windowIndex + FLEX_HISTORY_SIZE -
         MOTION_WINDOW_SIZE) % FLEX_HISTORY_SIZE;

    const size_t previousStart =
        (currentStart + FLEX_HISTORY_SIZE -
         FLEX_WINDOW_STEP) % FLEX_HISTORY_SIZE;

    float currentSum = 0;
    float previousSum = 0;

    for (size_t i = 0; i < MOTION_WINDOW_SIZE; i++) {

        currentSum += window[
            (currentStart + i) % FLEX_HISTORY_SIZE
        ];

        previousSum += window[
            (previousStart + i) % FLEX_HISTORY_SIZE
        ];
    }

    float currentMean =
        currentSum / MOTION_WINDOW_SIZE;

    float previousMean =
        previousSum / MOTION_WINDOW_SIZE;

    return fabsf(currentMean - previousMean) > threshold;
}


// ============================================================
// GESTURE DETECTION
// ============================================================

static bool gestureDetect(
    Sample &sample,
    bool &imuMoving,
    bool &flexMoving
) {

    float motion = sqrtf(
        sample.ax * sample.ax +
        sample.ay * sample.ay +
        sample.az * sample.az
    );

    float rotation = sqrtf(
        sample.gx * sample.gx +
        sample.gy * sample.gy +
        sample.gz * sample.gz
    );

    bool motionDetected = isNoiseThresholdExceededIMU(
        motion,
        motionWindow,
        &motionWindowCount,
        &motionWindowIndex,
        motionNoiseThreshold
    );

    bool rotationDetected = isNoiseThresholdExceededIMU(
        rotation,
        rotationWindow,
        &rotationWindowCount,
        &rotationWindowIndex,
        rotationNoiseThreshold
    );

    bool flex1Detected = isNoiseThresholdExceededFlex(
        sample.flex1,
        flex1Window,
        &flex1WindowCount,
        &flex1WindowIndex,
        &flex1SamplesSinceComparison,
        flex1NoiseThreshold
    );

    bool flex2Detected = isNoiseThresholdExceededFlex(
        sample.flex2,
        flex2Window,
        &flex2WindowCount,
        &flex2WindowIndex,
        &flex2SamplesSinceComparison,
        flex2NoiseThreshold
    );

    imuMoving = motionDetected || rotationDetected;
    flexMoving = flex1Detected || flex2Detected;

    #if GESTURE_DEBUG
    bool moving  = imuMoving || flexMoving;
    if (!gestureActive && moving) {
        Serial.print("motion: ");
        Serial.print(motionDetected);
        Serial.print(", motionThreshold: ");
        Serial.println(motionNoiseThreshold);

        Serial.print("flex1: ");
        Serial.print(flex1Detected);
        Serial.print(", flex1Threshold: ");
        Serial.println(flex1NoiseThreshold);

        Serial.print("flex2: ");
        Serial.print(flex2Detected);
        Serial.print(", flex2Threshold: ");
        Serial.println(flex2NoiseThreshold);

        Serial.print("rotation: ");
        Serial.print(rotationDetected);
        Serial.print(", rotationThreshold: ");
        Serial.println(rotationNoiseThreshold);
    }
    #endif

    return imuMoving || flexMoving;
}


// ============================================================
// SAMPLE BUFFERING
// ============================================================

static void appendGestureSample(
    const Sample &sample,
    const GesturePose &pose
) {

    if (sampleCount >= MAX_SAMPLES) {
        return;
    }

    gestureSamples[sampleCount] = sample;
    gesturePoses[sampleCount] = pose;

    sampleCount++;
}


static void bufferPreGestureSample(
    const Sample &sample,
    const GesturePose &pose
) {

    gestureSampleBuffer[gestureSampleBufferIndex] = sample;
    gesturePosesBuffer[gestureSampleBufferIndex] = pose;

    gestureSampleBufferIndex =
        (gestureSampleBufferIndex + 1) % MOTION_WINDOW_SIZE;

    if (gestureSampleBufferCount < MOTION_WINDOW_SIZE) {
        gestureSampleBufferCount++;
    }
}


static void appendBufferedGestureSamples() {

    size_t firstSample =
        (gestureSampleBufferIndex +
         MOTION_WINDOW_SIZE -
         gestureSampleBufferCount) % MOTION_WINDOW_SIZE;

    for (size_t i = 0; i < gestureSampleBufferCount; i++) {

        size_t index =
            (firstSample + i) % MOTION_WINDOW_SIZE;

        appendGestureSample(
            gestureSampleBuffer[index],
            gesturePosesBuffer[index]
        );
    }

    gestureSampleBufferCount = 0;
    gestureSampleBufferIndex = 0;
}


// ============================================================
// DETERMINISTIC CLASSIFICATION
// ============================================================

static GestureType classifyGesture() {

    if (sampleCount < 3) {
        return GestureType::UNRECOGNIZED;
    }

    float horizontalEnergy = 0;
    float verticalEnergy = 0;
    float rotationEnergy = 0;

    float maxHorizontalAcceleration = 0;
    float minHorizontalAcceleration = 0;

    float maxVerticalAcceleration = 0;
    float minVerticalAcceleration = 0;

    unsigned long maxHorizontalTime = 0;
    unsigned long minHorizontalTime = 0;

    unsigned long maxVerticalTime = 0;
    unsigned long minVerticalTime = 0;

    float flexMinimumIndex = 0;
    float flexMaximumIndex = 0;

    float flexMinimumMiddle = 0;
    float flexMaximumMiddle = 0;

    float flexStartSum = 0;
    float flexMiddleSum = 0;
    float flexEndSum = 0;

    const size_t flexSegmentLength =
        (sampleCount / 5) > 0 ? sampleCount / 5 : 1;

    const size_t flexMiddleStart =
        (sampleCount - flexSegmentLength) / 2;

    const size_t flexEndStart =
        sampleCount - flexSegmentLength;

    for (size_t i = 0; i < sampleCount; i++) {

        const Sample &sample = gestureSamples[i];
        const GesturePose &pose = gesturePoses[i];

        float dt = (i == 0)
            ? SAMPLE_INTERVAL_US / 1000000.0f
            : (sample.time - gestureSamples[i - 1].time)
                / 1000000.0f;

        float sinRoll = sinf(pose.roll);
        float cosRoll = cosf(pose.roll);

        float sinPitch = sinf(pose.pitch);
        float cosPitch = cosf(pose.pitch);

        float worldX =
            cosPitch * sample.ax +
            sinPitch * sinRoll * sample.ay +
            sinPitch * cosRoll * sample.az;

        float worldY =
            cosRoll * sample.ay -
            sinRoll * sample.az;

        float worldZ =
            -sinPitch * sample.ax +
            cosPitch * sinRoll * sample.ay +
            cosPitch * cosRoll * sample.az;

        float horizontalAcceleration = worldX;
        float verticalAcceleration = worldZ;

        float flexValueIndex =
            sample.flex2;

        float flexValueMiddle =
            sample.flex1;

        if (i == 0) {
            flexMinimumIndex = flexValueIndex;
            flexMaximumIndex = flexValueIndex;
        } else {
            flexMinimumIndex = fminf(flexMinimumIndex, flexValueIndex);
            flexMaximumIndex = fmaxf(flexMaximumIndex, flexValueIndex);
        }

        if (i == 0) {
            flexMinimumMiddle = flexValueMiddle;
            flexMaximumMiddle = flexValueMiddle;
        } else {
            flexMinimumMiddle = fminf(flexMinimumMiddle, flexValueMiddle);
            flexMaximumMiddle = fmaxf(flexMaximumMiddle, flexValueMiddle);
        }

        if (i < flexSegmentLength) {
            flexStartSum += flexValueIndex;
        }

        if (
            i >= flexMiddleStart &&
            i < flexMiddleStart + flexSegmentLength
        ) {
            flexMiddleSum += flexValueIndex;
        }

        if (i >= flexEndStart) {
            flexEndSum += flexValueIndex;
        }

        horizontalEnergy +=
            fabsf(horizontalAcceleration) * dt;

        verticalEnergy +=
            fabsf(verticalAcceleration) * dt;

        rotationEnergy += sqrtf(
            sample.gx * sample.gx +
            sample.gy * sample.gy +
            sample.gz * sample.gz
        ) * dt;

        if (
            horizontalAcceleration >
            maxHorizontalAcceleration
        ) {
            maxHorizontalAcceleration =
                horizontalAcceleration;

            maxHorizontalTime = sample.time;
        }

        if (
            horizontalAcceleration <
            minHorizontalAcceleration
        ) {
            minHorizontalAcceleration =
                horizontalAcceleration;

            minHorizontalTime = sample.time;
        }

        if (
            verticalAcceleration >
            maxVerticalAcceleration
        ) {
            maxVerticalAcceleration =
                verticalAcceleration;

            maxVerticalTime = sample.time;
        }

        if (
            verticalAcceleration <
            minVerticalAcceleration
        ) {
            minVerticalAcceleration =
                verticalAcceleration;

            minVerticalTime = sample.time;
        }
    #if GESTURE_DEBUG
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
        Serial.print(" flex1= ");
        Serial.print(sample.flex1);
        Serial.print(" flex2= ");
        Serial.print(sample.flex2);
        Serial.print(" dt=");
        Serial.println(dt, 5);
    #endif
    }

    float flexStartMean =
        flexStartSum / flexSegmentLength;

    float flexMiddleMean =
        flexMiddleSum / flexSegmentLength;

    float flexEndMean =
        flexEndSum / flexSegmentLength;

    float flexRangeIndex =
        flexMaximumIndex - flexMinimumIndex;

    float flexRangeMiddle =
        flexMaximumMiddle - flexMinimumMiddle;

    float flexNoiseRangeIndex =
          1.5*flex2NoiseThreshold;

    float flexNoiseRangeMiddle =
          1.5*flex1NoiseThreshold;

    float flexClosedThreshold =
        flexMinimumIndex + 0.35f * flexRangeIndex;

    float flexOpenThreshold =
        flexMinimumIndex + 0.65f * flexRangeIndex;

    bool flexDataIsUsableIndex =
        flexRangeIndex > flexNoiseRangeIndex;

    bool flexDataIsUsableMiddle = 
        flexRangeMiddle > flexNoiseRangeMiddle;

    bool startsClosed =
        flexStartMean <= flexClosedThreshold;

    bool startsOpen =
        flexStartMean >= flexOpenThreshold;

    bool middleClosed =
        flexMiddleMean <= flexClosedThreshold;

    bool middleOpen =
        flexMiddleMean >= flexOpenThreshold;

    bool endsClosed =
        flexEndMean <= flexClosedThreshold;

    bool endsOpen =
        flexEndMean >= flexOpenThreshold;

    #if GESTURE_DEBUG
    Serial.println("gesture summary:");
    Serial.print("  samples=");
    Serial.println(sampleCount);
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
    Serial.print(" flex usable= ");
    Serial.println(flexDataIsUsable);
    #endif

    // --------------------------------------------------------
    // ZOOM DETECTION
    // --------------------------------------------------------

    if (
        flexDataIsUsableIndex &&
        rotationEnergy < 4.0f &&
        horizontalEnergy < 1.0f &&
        verticalEnergy < 1.0f
    ) {

        if (startsClosed && endsOpen) {
            return GestureType::ZOOM_IN;
        }

        if (startsOpen && endsClosed) {
            return GestureType::ZOOM_OUT;
        }

        if (
            startsClosed &&
            middleOpen &&
            endsClosed
        ) {
            return GestureType::ZOOM_OUT;
        }

        if (
            startsOpen &&
            middleClosed &&
            endsOpen
        ) {
            return GestureType::ZOOM_IN;
        }
    }

    // --------------------------------------------------------
    // ROTATION
    // --------------------------------------------------------

    if (rotationEnergy > 4.0f && !flexDataIsUsableIndex &&
        flexDataIsUsableMiddle &&
        horizontalEnergy < 1.0f &&
        verticalEnergy < 1.0f ) {
        return GestureType::ROTATION;
    }

    // --------------------------------------------------------
    // SWIPES
    // --------------------------------------------------------

    if (
        ((horizontalEnergy > 1.0f &&
         verticalEnergy < 1.5f) ||

        (verticalEnergy > 1.0f &&
         horizontalEnergy < 1.0f)) 
         && !flexDataIsUsableIndex
         && !flexDataIsUsableMiddle
    ) {

        if (horizontalEnergy > verticalEnergy) {

            if (minHorizontalTime < maxHorizontalTime) {
                return GestureType::SWIPE_LEFT;
            }

            return GestureType::SWIPE_RIGHT;
        }

        if (minVerticalTime < maxVerticalTime) {
            return GestureType::SWIPE_DOWN;
        }

        return GestureType::SWIPE_UP;
    }

    return GestureType::UNRECOGNIZED;
}


// ============================================================
// GESTURE COMPLETION
// ============================================================

static GestureResult finishGesture() {

    GestureResult result;

    if (!gestureActive) {
        return result;
    }

    result.sampleCount = sampleCount;
    result.type = classifyGesture();
    result.completed = true;
    playGestureSound(result.type);

    gestureActive = false;
    sampleCount = 0;

    return result;
}


// ============================================================
// PROCESS ONE SENSOR SAMPLE
// ============================================================

GestureResult processGestureSample(const Sample &rawSample) {

    GestureResult result;

    if (!calibrationComplete) {
        return result;
    }

    // Work on a copy so BLE can transmit raw readings.
    Sample sample = rawSample;

    /*
    Serial.printf("Raw X: %.3f ", sample.ax);
    Serial.printf("Raw Y: %.3f ", sample.ay);
    Serial.printf("Raw Z: %.3f", sample.az);
    Serial.print("Flex 1: ");
    Serial.println(sample.flex1);
    */
    

    getOrientation(sample);
    
    removeGravity(
        sample,
        sample.ax,
        sample.ay,
        sample.az
    );

    // Match teammate's baseline-corrected gyro processing.
    sample.gx -= gyroBaselineX;
    sample.gy -= gyroBaselineY;
    sample.gz -= gyroBaselineZ;

    sample.ax -= accelBaselineX;
    sample.ay -= accelBaselineY;
    sample.az -= accelBaselineZ;
    
    /*
    Serial.printf("Processed X: %.3f ", sample.ax);
    Serial.printf("Processed Y: %.3f ", sample.ay);
    Serial.printf("Processed Z: %.3f ", sample.az);
    Serial.printf("Roll: %.3f ", filter.getRollRadians());
    Serial.printf("Ptich: %.3f\n", filter.getPitchRadians());
    */
    

    GesturePose pose = {
        filter.getRollRadians(),
        filter.getPitchRadians()
    };

    bool imuMoving = false;
    bool flexMoving = false;

    bool moving = gestureDetect(
        sample,
        imuMoving,
        flexMoving
    );

    if (!gestureActive) {

        if (flexMoving) {

            startConfirmationCount =
                START_CONFIRMATION_COUNT;

        } else if (imuMoving) {

            if (
                startConfirmationCount <
                START_CONFIRMATION_COUNT
            ) {
                startConfirmationCount++;
            }

        } else {

            startConfirmationCount = 0;
        }

        if (
            startConfirmationCount >=
            START_CONFIRMATION_COUNT
        ) {

            gestureActive = true;
            startConfirmationCount = 0;
            sampleCount = 0;

            lastMotionTime = sample.time;

            appendBufferedGestureSamples();

            Serial.println("[GESTURE] Started");

        } else {

            bufferPreGestureSample(sample, pose);

            return result;
        }
    }

    if (gestureActive) {

        appendGestureSample(sample, pose);

        if (moving) {
            lastMotionTime = sample.time;
        }

        if (
            sample.time - lastMotionTime >= QUIET_TIME_US ||

            sample.time - gestureSamples[0].time >=
                MAX_GESTURE_US ||

            sampleCount >= MAX_SAMPLES
        ) {

            result = finishGesture();

            Serial.println("[GESTURE] Finished");

            return result;
        }
    }

    return result;
}


// ============================================================
// HUMAN-READABLE GESTURE NAMES
// ============================================================

const char *gestureTypeName(GestureType type) {

    switch (type) {

        case GestureType::SWIPE_LEFT:
            return "SWIPE_LEFT";

        case GestureType::SWIPE_RIGHT:
            return "SWIPE_RIGHT";

        case GestureType::SWIPE_UP:
            return "SWIPE_UP";

        case GestureType::SWIPE_DOWN:
            return "SWIPE_DOWN";

        case GestureType::ROTATION:
            return "ROTATION";

        case GestureType::ZOOM_IN:
            return "ZOOM_IN";

        case GestureType::ZOOM_OUT:
            return "ZOOM_OUT";

        case GestureType::POTENTIAL_ZOOM_IN:
            return "POTENTIAL_ZOOM_IN";

        case GestureType::POTENTIAL_ZOOM_OUT:
            return "POTENTIAL_ZOOM_OUT";

        case GestureType::UNRECOGNIZED:
            return "UNRECOGNIZED";

        default:
            return "NONE";
    }
}

bool isGestureActive() {
    return gestureActive;
}