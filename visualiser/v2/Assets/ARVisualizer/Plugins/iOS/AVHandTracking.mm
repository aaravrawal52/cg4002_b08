#import <ARKit/ARKit.h>
#import <UIKit/UIKit.h>
#import <QuartzCore/QuartzCore.h>
#include "AVMarkerDetector.h"
#include <mutex>
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstring>

// ARKit XR Plugin 6.6: Includes~/UnityXRNativePtrs.h, version 1 ABI.
struct AVNativeSession { int version; void *sessionPtr; };
struct AVHandSample
{
    float rootX, rootY, rootConfidence;
    float tipX, tipY, tipConfidence;
    float tipSessionX, tipSessionY, tipSessionZ;
    int hasDepth;
    double age;
    float rootSessionX, rootSessionY, rootSessionZ;
    int hasRootDepth, handedness;
};
static_assert(sizeof(AVHandSample) == 72 && offsetof(AVHandSample, age) == 40 &&
    offsetof(AVHandSample, rootSessionX) == 48 && offsetof(AVHandSample, handedness) == 64,
    "Hand sample must match C# Sequential layout");
static std::mutex avMutex;
static bool avBusy = false;
static uint64_t avGeneration = 0;
static AVHandSample avFirstObservation = {}, avSecondObservation = {};
static CFTimeInterval avCaptureTime = 0;
static NSTimeInterval avLastFrameTime = -1;
static AVMarkerConfig avConfig = {};
static_assert(sizeof(AVMarkerConfig) == 56, "Marker config must match C# Sequential layout");

extern "C" void AVMarker_Reset()
{
    std::lock_guard<std::mutex> lock(avMutex);
    ++avGeneration;
    avFirstObservation = {};
    avSecondObservation = {};
    avCaptureTime = 0;
    avLastFrameTime = -1;
}

// The slots are always left and right, assigned by marker ID. No hand-pose inference is used.
extern "C" double AVMarker_ReadObservations(AVHandSample *first, AVHandSample *second)
{
    std::lock_guard<std::mutex> lock(avMutex);
    *first = avFirstObservation;
    *second = avSecondObservation;
    first->age = second->age = CACurrentMediaTime() - avCaptureTime;
    return avCaptureTime;
}

// Raw LiDAR from the SAME frame as the marker image. Reject holes and inconsistent depth edges;
// never substitute the virtual screen's depth for the finger.
static bool AVReadDepth(ARDepthData *data, CGPoint imagePoint, float *depth)
{
    if (!data || imagePoint.x < 0 || imagePoint.x >= 1 || imagePoint.y < 0 || imagePoint.y >= 1) return false;
    CVPixelBufferRef map = data.depthMap, confidence = data.confidenceMap;
    if (!map || !confidence || CVPixelBufferGetPixelFormatType(map) != kCVPixelFormatType_DepthFloat32) return false;
    size_t width = CVPixelBufferGetWidth(map), height = CVPixelBufferGetHeight(map);
    if (CVPixelBufferGetWidth(confidence) != width || CVPixelBufferGetHeight(confidence) != height) return false;
    CVPixelBufferLockBaseAddress(map, kCVPixelBufferLock_ReadOnly);
    CVPixelBufferLockBaseAddress(confidence, kCVPixelBufferLock_ReadOnly);
    const uint8_t *depthBytes = (const uint8_t *)CVPixelBufferGetBaseAddress(map);
    const uint8_t *confidenceBytes = (const uint8_t *)CVPixelBufferGetBaseAddress(confidence);
    size_t depthStride = CVPixelBufferGetBytesPerRow(map), confidenceStride = CVPixelBufferGetBytesPerRow(confidence);
    int x = std::min((int)width - 1, (int)(imagePoint.x * width));
    int y = std::min((int)height - 1, (int)(imagePoint.y * height));
    float samples[9]; int count = 0;
    if (depthBytes && confidenceBytes)
    {
        float center = ((const float *)(depthBytes + y * depthStride))[x];
        if (std::isfinite(center) && center >= 0.1f && center <= 3.f && confidenceBytes[y * confidenceStride + x] >= 1)
        {
            for (int dy = -1; dy <= 1; ++dy)
                for (int dx = -1; dx <= 1; ++dx)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || py < 0 || px >= (int)width || py >= (int)height) continue;
                    float d = ((const float *)(depthBytes + py * depthStride))[px];
                    if (confidenceBytes[py * confidenceStride + px] >= 1 && std::isfinite(d) && std::fabs(d - center) <= 0.025f)
                        samples[count++] = d;
                }
        }
    }
    CVPixelBufferUnlockBaseAddress(confidence, kCVPixelBufferLock_ReadOnly);
    CVPixelBufferUnlockBaseAddress(map, kCVPixelBufferLock_ReadOnly);
    if (count < 3) return false;
    std::sort(samples, samples + count);
    *depth = samples[count / 2];
    return true;
}

static simd_float3 AVCameraPoint(ARCamera *camera, CGPoint imagePoint, float depth)
{
    matrix_float3x3 k = camera.intrinsics;
    CGSize size = camera.imageResolution;
    // Sensor pixels have top-left origin. ARKit camera space is Y-up and looks along -Z.
    return simd_make_float3((imagePoint.x * size.width - k.columns[2].x) * depth / k.columns[0].x,
        -(imagePoint.y * size.height - k.columns[2].y) * depth / k.columns[1].y, -depth);
}

extern "C" int AVMarker_Submit(void *nativeSession, int orientation, int width, int height, const AVMarkerConfig *config)
{
    if (@available(iOS 14.0, *))
    {
        if (!nativeSession || width <= 0 || height <= 0 || !AVMarker_ConfigValid(config)) return -1;
        auto *data = static_cast<AVNativeSession *>(nativeSession);
        if (data->version != 1 || !data->sessionPtr) return -1;
        ARSession *session = (__bridge ARSession *)data->sessionPtr;
        ARFrame *frame = session.currentFrame;
        if (!frame) return 0;
        uint64_t generation;
        {
            std::lock_guard<std::mutex> lock(avMutex);
            if (memcmp(config, &avConfig, sizeof(avConfig)) != 0)
            {
                avConfig = *config;
                ++avGeneration;
                avFirstObservation = {}; avSecondObservation = {};
                avCaptureTime = 0; avLastFrameTime = -1;
            }
            if (avBusy || frame.timestamp == avLastFrameTime) return 0;
            avBusy = true;
            avLastFrameTime = frame.timestamp;
            generation = avGeneration;
        }
        CGAffineTransform display = [frame displayTransformForOrientation:(UIInterfaceOrientation)orientation viewportSize:CGSizeMake(width, height)];
        // ARFrame timestamps use the device's monotonic clock, as does CACurrentMediaTime.
        // Measure age from image capture, including any camera delivery/worker delay.
        CFTimeInterval captureTime = frame.timestamp;
        AVMarkerConfig settings = *config;
        // ARC retains this one frame for the worker, including image, pose, intrinsics and depth.
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
            @autoreleasepool
            {
                AVHandSample observations[2] = {};
                static AVMarkerDetector *detector = AVMarker_Create();
                AVMarkerPoint markers[4] = {};
                CVPixelBufferRef image = frame.capturedImage;
                matrix_float3x3 intrinsics = frame.camera.intrinsics;
                if (detector && CVPixelBufferGetPlaneCount(image) >= 1
                    && CVPixelBufferLockBaseAddress(image, kCVPixelBufferLock_ReadOnly) == kCVReturnSuccess)
                {
                    // AprilTag reads the existing full-resolution luma plane; no RGB conversion/copy.
                    AVMarker_Detect(detector, (const uint8_t *)CVPixelBufferGetBaseAddressOfPlane(image, 0),
                        (int)CVPixelBufferGetWidthOfPlane(image, 0), (int)CVPixelBufferGetHeightOfPlane(image, 0),
                        (int)CVPixelBufferGetBytesPerRowOfPlane(image, 0), &settings,
                        intrinsics.columns[0].x, intrinsics.columns[1].y,
                        intrinsics.columns[2].x, intrinsics.columns[2].y, markers);
                    CVPixelBufferUnlockBaseAddress(image, kCVPixelBufferLock_ReadOnly);
                }
                CGSize imageSize = frame.camera.imageResolution;
                for (int side = 0; side < 2; ++side)
                {
                    AVHandSample &sample = observations[side];
                    sample.handedness = side == 0 ? -1 : 1;
                    const AVMarkerPoint &root = markers[side * 2], &tip = markers[side * 2 + 1];
                    CGPoint rootImage = CGPointMake(root.x / imageSize.width, root.y / imageSize.height);
                    CGPoint tipImage = CGPointMake(tip.x / imageSize.width, tip.y / imageSize.height);
                    if (root.valid)
                    {
                        CGPoint viewport = CGPointApplyAffineTransform(rootImage, display);
                        sample.rootX = viewport.x; sample.rootY = 1 - viewport.y; sample.rootConfidence = root.confidence;
                    }
                    if (tip.valid)
                    {
                        CGPoint viewport = CGPointApplyAffineTransform(tipImage, display);
                        sample.tipX = viewport.x; sample.tipY = 1 - viewport.y; sample.tipConfidence = tip.confidence;
                    }
                    float rootDepth, tipDepth;
                    simd_float3 rootCamera = {};
                    if (root.valid && AVReadDepth(frame.sceneDepth, rootImage, &rootDepth))
                    {
                        rootCamera = AVCameraPoint(frame.camera, rootImage, rootDepth);
                        simd_float4 world = simd_mul(frame.camera.transform, simd_make_float4(rootCamera.x, rootCamera.y, rootCamera.z, 1));
                        sample.rootSessionX = world.x; sample.rootSessionY = world.y; sample.rootSessionZ = -world.z;
                        sample.hasRootDepth = 1;
                    }
                    if (root.valid && tip.valid && sample.hasRootDepth && AVReadDepth(frame.sceneDepth, tipImage, &tipDepth))
                    {
                        simd_float3 tipCamera = AVCameraPoint(frame.camera, tipImage, tipDepth);
                        // Apply a calibrated marker-to-finger-pad offset; pose never supplies touch depth.
                        tipCamera += simd_make_float3(tip.contactOffset[0], -tip.contactOffset[1], -tip.contactOffset[2]);
                        float fingerLength = simd_distance(tipCamera, rootCamera);
                        if (fingerLength >= 0.015f && fingerLength <= 0.22f)
                        {
                            simd_float4 world = simd_mul(frame.camera.transform, simd_make_float4(tipCamera.x, tipCamera.y, tipCamera.z, 1));
                            sample.tipSessionX = world.x; sample.tipSessionY = world.y; sample.tipSessionZ = -world.z;
                            sample.hasDepth = 1;
                        }
                    }
                }
                std::lock_guard<std::mutex> lock(avMutex);
                if (generation == avGeneration)
                {
                    avFirstObservation = observations[0]; avSecondObservation = observations[1];
                    avCaptureTime = captureTime;
                }
                avBusy = false;
            }
        });
        return 1;
    }
    return -1;
}
