#import <ARKit/ARKit.h>
#import <Vision/Vision.h>
#import <UIKit/UIKit.h>
#import <QuartzCore/QuartzCore.h>
#import <ImageIO/ImageIO.h>
#include <mutex>
#include <algorithm>
#include <cmath>
#include <cstddef>

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
static AVHandSample avSample = {};
static CFTimeInterval avCaptureTime = 0;
static NSTimeInterval avLastFrameTime = -1;

extern "C" void AVHand_Reset()
{
    std::lock_guard<std::mutex> lock(avMutex);
    ++avGeneration;
    avSample = {};
    avCaptureTime = 0;
    avLastFrameTime = -1;
}

extern "C" int AVHand_Read(AVHandSample *sample)
{
    std::lock_guard<std::mutex> lock(avMutex);
    *sample = avSample;
    sample->age = CACurrentMediaTime() - avCaptureTime;
    return avSample.rootConfidence > 0 || avSample.tipConfidence > 0;
}

// Raw LiDAR from the SAME frame as Vision. Reject holes and inconsistent depth edges;
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

extern "C" int AVHand_Submit(void *nativeSession, int orientation, int width, int height)
{
    if (@available(iOS 14.0, *))
    {
        if (!nativeSession || width <= 0 || height <= 0) return -1;
        auto *data = static_cast<AVNativeSession *>(nativeSession);
        if (data->version != 1 || !data->sessionPtr) return -1;
        ARSession *session = (__bridge ARSession *)data->sessionPtr;
        ARFrame *frame = session.currentFrame;
        if (!frame) return 0;
        uint64_t generation;
        {
            std::lock_guard<std::mutex> lock(avMutex);
            if (avBusy || frame.timestamp == avLastFrameTime) return 0;
            avBusy = true;
            avLastFrameTime = frame.timestamp;
            generation = avGeneration;
        }
        CGAffineTransform display = [frame displayTransformForOrientation:(UIInterfaceOrientation)orientation viewportSize:CGSizeMake(width, height)];
        CFTimeInterval captureTime = CACurrentMediaTime();
        // ARC retains this one frame for the worker, including image, pose, intrinsics and depth.
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
            @autoreleasepool
            {
                AVHandSample sample = {};
                @try
                {
                    VNDetectHumanHandPoseRequest *request = [[VNDetectHumanHandPoseRequest alloc] init];
                    request.maximumHandCount = 1;
                    VNImageRequestHandler *handler = [[VNImageRequestHandler alloc] initWithCVPixelBuffer:frame.capturedImage orientation:kCGImagePropertyOrientationUp options:@{}];
                    NSError *error = nil;
                    if ([handler performRequests:@[request] error:&error] && request.results.count > 0)
                    {
                        VNHumanHandPoseObservation *hand = request.results.firstObject;
                        if (@available(iOS 15.0, *))
                            sample.handedness = hand.chirality == VNChiralityRight ? 1 : hand.chirality == VNChiralityLeft ? -1 : 0;
                        VNRecognizedPoint *root = [hand recognizedPointForJointName:VNHumanHandPoseObservationJointNameIndexMCP error:&error];
                        VNRecognizedPoint *tip = [hand recognizedPointForJointName:VNHumanHandPoseObservationJointNameIndexTip error:&error];
                        CGPoint rootImage = root ? CGPointMake(root.location.x, 1 - root.location.y) : CGPointZero;
                        CGPoint tipImage = tip ? CGPointMake(tip.location.x, 1 - tip.location.y) : CGPointZero;
                        if (root && root.confidence > 0)
                        {
                            CGPoint viewport = CGPointApplyAffineTransform(rootImage, display);
                            sample.rootX = viewport.x; sample.rootY = 1 - viewport.y; sample.rootConfidence = root.confidence;
                        }
                        if (tip && tip.confidence > 0)
                        {
                            CGPoint viewport = CGPointApplyAffineTransform(tipImage, display);
                            sample.tipX = viewport.x; sample.tipY = 1 - viewport.y; sample.tipConfidence = tip.confidence;
                        }
                        float tipDepth, rootDepth;
                        simd_float3 rootCamera = {};
                        if (sample.rootConfidence > 0 && AVReadDepth(frame.sceneDepth, rootImage, &rootDepth))
                        {
                            rootCamera = AVCameraPoint(frame.camera, rootImage, rootDepth);
                            simd_float4 world = simd_mul(frame.camera.transform, simd_make_float4(rootCamera.x, rootCamera.y, rootCamera.z, 1));
                            sample.rootSessionX = world.x; sample.rootSessionY = world.y; sample.rootSessionZ = -world.z;
                            sample.hasRootDepth = 1;
                        }
                        if (sample.tipConfidence > 0 && sample.hasRootDepth && AVReadDepth(frame.sceneDepth, tipImage, &tipDepth))
                        {
                            simd_float3 tipCamera = AVCameraPoint(frame.camera, tipImage, tipDepth);
                            // Reject background depth at a fingertip whose base is on a nearer hand.
                            float fingerLength = simd_distance(tipCamera, rootCamera);
                            if (fingerLength >= 0.015f && fingerLength <= 0.22f)
                            {
                                simd_float4 world = simd_mul(frame.camera.transform, simd_make_float4(tipCamera.x, tipCamera.y, tipCamera.z, 1));
                                sample.tipSessionX = world.x; sample.tipSessionY = world.y; sample.tipSessionZ = -world.z;
                                sample.hasDepth = 1;
                            }
                        }
                    }
                }
                @catch (NSException *exception) { sample = {}; }
                std::lock_guard<std::mutex> lock(avMutex);
                if (generation == avGeneration) { avSample = sample; avCaptureTime = captureTime; }
                avBusy = false;
            }
        });
        return 1;
    }
    return -1;
}
