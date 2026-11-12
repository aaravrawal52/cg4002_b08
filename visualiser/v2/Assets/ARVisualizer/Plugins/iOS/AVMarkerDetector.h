#pragma once
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Slot order is part of the C#/native ABI: left root, left tip, right root, right tip.
typedef struct {
    int ids[4];
    float minimumMargin, minimumEdgePixels, quadDecimate, tipWidthMetres;
    // Marker-local metres: X right, Y towards the printed TOP, Z out of the printed face.
    float leftContactOffset[3], rightContactOffset[3];
} AVMarkerConfig;

typedef struct {
    int valid, id;
    double x, y; // Sensor pixels, top-left origin.
    float confidence;
    // Camera optical coordinates: X right, Y down, Z forward.
    double contactOffset[3];
} AVMarkerPoint;

typedef struct AVMarkerDetector AVMarkerDetector;
int AVMarker_ConfigValid(const AVMarkerConfig *config);
AVMarkerDetector *AVMarker_Create(void);
void AVMarker_Destroy(AVMarkerDetector *detector);
// Caller owns the Y plane for the duration of this call. No camera/session is opened here.
int AVMarker_Detect(AVMarkerDetector *detector, const uint8_t *pixels, int width, int height, int stride,
                    const AVMarkerConfig *config, double fx, double fy, double cx, double cy, AVMarkerPoint points[4]);

#ifdef __cplusplus
}
#endif
