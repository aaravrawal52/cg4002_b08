#include <Arduino.h>
#include "GestureProcessor.h"
#include <esp_arduino_version.h>
#include <freertos/FreeRTOS.h>
#include <freertos/queue.h>
#include <freertos/task.h>

#if !defined(CONFIG_IDF_TARGET_ESP32)
#error "This file is configured for the original ESP32 / FireBeetle DFR0478."
#endif

#ifndef GESTURE_SOUND_PWM_PIN
#define GESTURE_SOUND_PWM_PIN 25
#endif

namespace {
constexpr uint8_t BUZZER_PIN = GESTURE_SOUND_PWM_PIN;
constexpr uint8_t PWM_RESOLUTION = 10;
constexpr uint32_t INITIAL_FREQUENCY = 2000;
constexpr UBaseType_t QUEUE_LENGTH = 8;
#if ESP_ARDUINO_VERSION_MAJOR < 3
constexpr uint8_t PWM_CHANNEL = 6; // Reserve this channel and its paired timer.
#endif

struct Note {
    uint16_t frequencyHz;
    uint16_t durationMs;
};
struct Melody {
    const Note *notes;
    size_t count;
};

constexpr Note UP[] = {{2000, 100}, {2500, 100}, {3000, 140}, {0, 60}};
constexpr Note DOWN[] = {{3000, 100}, {2500, 100}, {2000, 140}, {0, 60}};
constexpr Note LEFT[] = {{2000, 100}, {0, 65}, {2000, 100}, {0, 60}};
constexpr Note RIGHT[] = {
    {3500, 70}, {0, 50}, {3500, 70}, {0, 50}, {3500, 70}, {0, 60}
};
constexpr Note UNKNOWN[] = {{1500, 350}, {0, 60}};

template <size_t N>
Melody melodyFor(const Note (&notes)[N]) { return {notes, N}; }

Melody selectMelody(GestureType type) {
    switch (type) {
        case GestureType::SWIPE_UP:     return melodyFor(UP);
        case GestureType::SWIPE_DOWN:   return melodyFor(DOWN);
        case GestureType::SWIPE_LEFT:   return melodyFor(LEFT);
        case GestureType::SWIPE_RIGHT:  return melodyFor(RIGHT);
        case GestureType::UNRECOGNIZED: return melodyFor(UNKNOWN);
        default:                       return {nullptr, 0};
    }
}

QueueHandle_t soundQueue = nullptr;
TaskHandle_t soundTask = nullptr;

void silenceBuzzer() {
#if ESP_ARDUINO_VERSION_MAJOR >= 3
    ledcWrite(BUZZER_PIN, 0);
#else
    ledcWrite(PWM_CHANNEL, 0);
#endif
}

bool setFrequency(uint16_t frequencyHz) {
    if (frequencyHz == 0) {
        silenceBuzzer();
        return true;
    }
#if ESP_ARDUINO_VERSION_MAJOR >= 3
    return ledcWriteTone(BUZZER_PIN, frequencyHz) != 0;
#else
    return ledcWriteTone(PWM_CHANNEL, frequencyHz) != 0;
#endif
}

bool attachBuzzer() {
    pinMode(BUZZER_PIN, OUTPUT);
    digitalWrite(BUZZER_PIN, LOW);
#if ESP_ARDUINO_VERSION_MAJOR >= 3
    if (!ledcAttach(BUZZER_PIN, INITIAL_FREQUENCY, PWM_RESOLUTION)) return false;
#else
    if (ledcSetup(PWM_CHANNEL, INITIAL_FREQUENCY, PWM_RESOLUTION) == 0) return false;
    ledcAttachPin(BUZZER_PIN, PWM_CHANNEL);
#endif
    silenceBuzzer();
    return true;
}

void detachBuzzer() {
    silenceBuzzer();
#if ESP_ARDUINO_VERSION_MAJOR >= 3
    ledcDetach(BUZZER_PIN);
#else
    ledcDetachPin(BUZZER_PIN);
#endif
    pinMode(BUZZER_PIN, OUTPUT);
    digitalWrite(BUZZER_PIN, LOW);
}

// Called only by the dedicated sound task, never by the gesture processor.
void playMelody(const Melody &melody) {
    for (size_t i = 0; i < melody.count; ++i) {
        const Note &note = melody.notes[i];
        if (!setFrequency(note.frequencyHz)) break;
        const TickType_t ticks = pdMS_TO_TICKS(note.durationMs);
        vTaskDelay(ticks > 0 ? ticks : 1);
    }
    silenceBuzzer();
}

void soundTaskMain(void *) {
    for (;;) {
        GestureType type;
        // Sleeps without using CPU while there are no pending sounds.
        if (xQueueReceive(soundQueue, &type, portMAX_DELAY) == pdTRUE) {
            playMelody(selectMelody(type));
        }
    }
}
} // namespace

bool initializeGestureSound() {
    if (soundTask != nullptr) return true;
    soundQueue = xQueueCreate(QUEUE_LENGTH, sizeof(GestureType));
    if (soundQueue == nullptr) return false;

    if (!attachBuzzer()) {
        vQueueDelete(soundQueue);
        soundQueue = nullptr;
        return false;
    }

    // ESP32 stack size is in bytes. Priority 1; task sleeps between notes.
    if (xTaskCreate(soundTaskMain, "gesture_sound", 3072, nullptr, 1,
                    &soundTask) != pdPASS) {
        soundTask = nullptr;
        detachBuzzer();
        vQueueDelete(soundQueue);
        soundQueue = nullptr;
        return false;
    }
    return true;
}

bool playGestureSound(GestureType type) {
    if (soundTask == nullptr || selectMelody(type).notes == nullptr) return false;
    // No delay or wait for queue space, even when a sound is already playing.
    return xQueueSend(soundQueue, &type, 0) == pdTRUE;
}
