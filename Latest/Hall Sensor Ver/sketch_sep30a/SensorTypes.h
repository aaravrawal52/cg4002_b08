#pragma once

#include <Arduino.h>

struct Sample {
    unsigned long time;

    float ax;
    float ay;
    float az;

    float gx;
    float gy;
    float gz;

    int hall1;
    int hall2;
};
