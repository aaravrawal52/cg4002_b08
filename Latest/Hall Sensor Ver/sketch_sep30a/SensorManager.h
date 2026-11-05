#pragma once

#include "SensorTypes.h"

bool initializeSensors();

bool readSensorSample(Sample &sample);

bool initializeGestureSound();

static bool restartIMU();