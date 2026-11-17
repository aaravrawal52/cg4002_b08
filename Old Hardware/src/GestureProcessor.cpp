#include "GestureProcessor.h"

#include <Arduino.h>
#include <MadgwickAHRS.h>
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
static constexpr size_t HALL_WINDOW_OVERLAP = 7;

static constexpr size_t HALL_WINDOW_STEP =
    MOTION_WINDOW_SIZE - HALL_WINDOW_OVERLAP;

static constexpr size_t HALL_HISTORY_SIZE =
    MOTION_WINDOW_SIZE + HALL_WINDOW_STEP;

static constexpr size_t MAX_SAMPLES =
    MAX_GESTURE_US / SAMPLE_INTERVAL_US + 1;

static constexpr float MIN_MOTION_THRESHOLD = 1.0f;
static constexpr float MIN_ROTATION_THRESHOLD = 1.0f;
static constexpr float MIN_HALL_THRESHOLD = 300.0f;

static constexpr int START_CONFIRMATION_COUNT = 3;


// ============================================================
// FILTER AND CALIBRATION STATE
// ============================================================

static Madgwick filter;

static bool calibrationComplete = false;
static size_t calibrationCount = 0;

static float hallBaseline1 = 0;
static float hallBaseline2 = 0;

static float accelBaselineX = 0;
static float accelBaselineY = 0;
static float accelBaselineZ = 0;

static float gyroBaselineX = 0;
static float gyroBaselineY = 0;
static float gyroBaselineZ = 0;

static float motionNoiseThreshold = 0;
static float rotationNoiseThreshold = 0;
static float hall1NoiseThreshold = 0;
static float hall2NoiseThreshold = 0;

static float calibrationMotion[CALIBRATION_SAMPLES];
static float calibrationRotation[CALIBRATION_SAMPLES];
static float calibrationHall1[CALIBRATION_SAMPLES];
static float calibrationHall2[CALIBRATION_SAMPLES];


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

static float hall1Window[HALL_HISTORY_SIZE] = {};
static float hall2Window[HALL_HISTORY_SIZE] = {};

static size_t motionWindowCount = 0;
static size_t rotationWindowCount = 0;

static size_t hall1WindowCount = 0;
static size_t hall2WindowCount = 0;

static size_t motionWindowIndex = 0;
static size_t rotationWindowIndex = 0;

static size_t hall1WindowIndex = 0;
static size_t hall2WindowIndex = 0;

static size_t hall1SamplesSinceComparison = HALL_WINDOW_STEP;
static size_t hall2SamplesSinceComparison = HALL_WINDOW_STEP;


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

    hall1WindowCount = 0;
    hall2WindowCount = 0;

    motionWindowIndex = 0;
    rotationWindowIndex = 0;

    hall1WindowIndex = 0;
    hall2WindowIndex = 0;

    hall1SamplesSinceComparison = HALL_WINDOW_STEP;
    hall2SamplesSinceComparison = HALL_WINDOW_STEP;

    hallBaseline1 = 0;
    hallBaseline2 = 0;

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
    bool hallSensor,
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

    if (hallSensor) {
        threshold = 4.0f * standardDeviation;
    } else {
        threshold = mean + 4.0f * standardDeviation;
    }

    return fmaxf(threshold, minimumThreshold);
}


// ============================================================
// CALIBRATION
// ============================================================

bool calibrateGestureProcessor(const Sample &rawSample) {

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

    hallBaseline1 += sample.hall1;
    hallBaseline2 += sample.hall2;

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

    calibrationHall1[calibrationCount] = sample.hall1;
    calibrationHall2[calibrationCount] = sample.hall2;

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

    hallBaseline1 /= divisor;
    hallBaseline2 /= divisor;

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

    hall1NoiseThreshold = calculateThreshold(
        calibrationHall1,
        CALIBRATION_SAMPLES,
        true,
        MIN_HALL_THRESHOLD
    );

    hall2NoiseThreshold = calculateThreshold(
        calibrationHall2,
        CALIBRATION_SAMPLES,
        true,
        MIN_HALL_THRESHOLD
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
        "Hall1 threshold: %.3f\n",
        hall1NoiseThreshold
    );

    Serial.printf(
        "Hall2 threshold: %.3f\n",
        hall2NoiseThreshold
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
// HALL WINDOW DETECTION
// ============================================================

static bool isNoiseThresholdExceededHall(
    int value,
    float *window,
    size_t *windowCount,
    size_t *windowIndex,
    size_t *samplesSinceComparison,
    float threshold
) {

    window[*windowIndex] = value;

    *windowIndex =
        (*windowIndex + 1) % HALL_HISTORY_SIZE;

    if (*windowCount < HALL_HISTORY_SIZE) {
        ++(*windowCount);
    }

    if (*windowCount < HALL_HISTORY_SIZE) {
        return false;
    }

    ++(*samplesSinceComparison);

    if (*samplesSinceComparison < HALL_WINDOW_STEP) {
        return false;
    }

    *samplesSinceComparison = 0;

    const size_t currentStart =
        (*windowIndex + HALL_HISTORY_SIZE -
         MOTION_WINDOW_SIZE) % HALL_HISTORY_SIZE;

    const size_t previousStart =
        (currentStart + HALL_HISTORY_SIZE -
         HALL_WINDOW_STEP) % HALL_HISTORY_SIZE;

    float currentSum = 0;
    float previousSum = 0;

    for (size_t i = 0; i < MOTION_WINDOW_SIZE; i++) {

        currentSum += window[
            (currentStart + i) % HALL_HISTORY_SIZE
        ];

        previousSum += window[
            (previousStart + i) % HALL_HISTORY_SIZE
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
    bool &hallMoving
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

    bool hall1Detected = isNoiseThresholdExceededHall(
        sample.hall1,
        hall1Window,
        &hall1WindowCount,
        &hall1WindowIndex,
        &hall1SamplesSinceComparison,
        hall1NoiseThreshold
    );

    bool hall2Detected = isNoiseThresholdExceededHall(
        sample.hall2,
        hall2Window,
        &hall2WindowCount,
        &hall2WindowIndex,
        &hall2SamplesSinceComparison,
        hall2NoiseThreshold
    );

    imuMoving = motionDetected || rotationDetected;
    hallMoving = hall1Detected || hall2Detected;

    return imuMoving || hallMoving;
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
        return GestureType::UNCLASSIFIED;
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

    float hallMinimum = 0;
    float hallMaximum = 0;

    float hallStartSum = 0;
    float hallMiddleSum = 0;
    float hallEndSum = 0;

    const size_t hallSegmentLength =
        (sampleCount / 5) > 0 ? sampleCount / 5 : 1;

    const size_t hallMiddleStart =
        (sampleCount - hallSegmentLength) / 2;

    const size_t hallEndStart =
        sampleCount - hallSegmentLength;

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

        float hallSeparation =
            (sample.hall1 + sample.hall2) * 0.5f;

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

        if (
            i >= hallMiddleStart &&
            i < hallMiddleStart + hallSegmentLength
        ) {
            hallMiddleSum += hallSeparation;
        }

        if (i >= hallEndStart) {
            hallEndSum += hallSeparation;
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
    }

    float hallStartMean =
        hallStartSum / hallSegmentLength;

    float hallMiddleMean =
        hallMiddleSum / hallSegmentLength;

    float hallEndMean =
        hallEndSum / hallSegmentLength;

    float hallRange =
        hallMaximum - hallMinimum;

    float hallNoiseRange =
        0.5f * (
            hall1NoiseThreshold +
            hall2NoiseThreshold
        );

    float hallClosedThreshold =
        hallMinimum + 0.35f * hallRange;

    float hallOpenThreshold =
        hallMinimum + 0.65f * hallRange;

    bool hallDataIsUsable =
        hallRange > hallNoiseRange;

    bool startsClosed =
        hallStartMean <= hallClosedThreshold;

    bool startsOpen =
        hallStartMean >= hallOpenThreshold;

    bool middleClosed =
        hallMiddleMean <= hallClosedThreshold;

    bool middleOpen =
        hallMiddleMean >= hallOpenThreshold;

    bool endsClosed =
        hallEndMean <= hallClosedThreshold;

    bool endsOpen =
        hallEndMean >= hallOpenThreshold;

    // --------------------------------------------------------
    // ZOOM DETECTION
    // --------------------------------------------------------

    if (
        hallDataIsUsable &&
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
            return GestureType::POTENTIAL_ZOOM_OUT;
        }

        if (
            startsOpen &&
            middleClosed &&
            endsOpen
        ) {
            return GestureType::POTENTIAL_ZOOM_IN;
        }
    }

    // --------------------------------------------------------
    // ROTATION
    // --------------------------------------------------------

    if (rotationEnergy > 4.0f) {
        return GestureType::ROTATION;
    }

    // --------------------------------------------------------
    // SWIPES
    // --------------------------------------------------------

    if (
        (horizontalEnergy > 1.0f &&
         verticalEnergy < 1.0f) ||

        (verticalEnergy > 1.0f &&
         horizontalEnergy < 1.0f)
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

    return GestureType::UNCLASSIFIED;
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

    // Match teammate's baseline-corrected gyro processing.
    sample.gx -= gyroBaselineX;
    sample.gy -= gyroBaselineY;
    sample.gz -= gyroBaselineZ;

    getOrientation(sample);

    removeGravity(
        sample,
        sample.ax,
        sample.ay,
        sample.az
    );

    sample.ax -= accelBaselineX;
    sample.ay -= accelBaselineY;
    sample.az -= accelBaselineZ;

    GesturePose pose = {
        filter.getRollRadians(),
        filter.getPitchRadians()
    };

    bool imuMoving = false;
    bool hallMoving = false;

    bool moving = gestureDetect(
        sample,
        imuMoving,
        hallMoving
    );

    if (!gestureActive) {

        if (hallMoving) {

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

        case GestureType::UNCLASSIFIED:
            return "UNCLASSIFIED";

        default:
            return "NONE";
    }
}

bool isGestureActive() {
    return gestureActive;
}