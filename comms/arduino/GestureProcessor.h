#pragma once

#include <Arduino.h>
#include "SensorTypes.h"

enum class GestureType {
    NONE,
    SWIPE_LEFT,
    SWIPE_RIGHT,
    SWIPE_UP,
    SWIPE_DOWN,
    ROTATION,
    ZOOM_IN,
    ZOOM_OUT,
    POTENTIAL_ZOOM_IN,
    POTENTIAL_ZOOM_OUT,
    UNRECOGNIZED
};

struct GestureResult {
    GestureType type = GestureType::NONE;
    bool completed = false;
    uint32_t sampleCount = 0;
};

void initializeGestureProcessor(float sampleRateHz = 70.0f);

// Feed stationary samples during startup calibration.
// Returns true once calibration is complete.
bool calibrateGestureProcessor(const Sample &rawSample);

// Process one sample after calibration.
// Returns a completed result only when a gesture ends.
GestureResult processGestureSample(const Sample &rawSample);

bool gestureCalibrationComplete();

const char *gestureTypeName(GestureType type);
