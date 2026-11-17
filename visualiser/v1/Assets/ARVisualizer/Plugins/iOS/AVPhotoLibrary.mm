#import <Photos/Photos.h>
#import <UIKit/UIKit.h>
#import <AVFoundation/AVFoundation.h>
extern "C" void UnitySendMessage(const char *, const char *, const char *);

@interface AVPhotoTask : NSObject
@property (nonatomic, copy) NSString *receiver;
@property (nonatomic, copy) NSString *key;
@property (nonatomic, copy) NSString *directory;
@property (nonatomic) int number;
@property (nonatomic) BOOL cancelled;
@property (nonatomic, strong) NSMutableArray<NSNumber *> *imageRequests;
@property (nonatomic, strong) NSMutableArray<NSString *> *files;
@property (nonatomic, strong) AVAssetExportSession *exporter;
@end
@implementation AVPhotoTask
@end
static NSMutableDictionary<NSString *, AVPhotoTask *> *AVPhotoTasks;

static void AVPhotoFinish(AVPhotoTask *task, NSDictionary *payload)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (task.cancelled) return;
        NSMutableDictionary *result = [payload mutableCopy];
        result[@"request"] = @(task.number);
        NSData *json = [NSJSONSerialization dataWithJSONObject:result options:0 error:nil];
        if (json) UnitySendMessage(task.receiver.UTF8String, "OnLibraryResult", [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding].UTF8String);
        [AVPhotoTasks removeObjectForKey:task.key];
    });
}
static void AVPhotoError(AVPhotoTask *task, NSString *message) { AVPhotoFinish(task, @{@"ok":@NO, @"error":message}); }
static NSString *AVPhotoPath(AVPhotoTask *task, NSString *extension)
{
    NSString *path = [task.directory stringByAppendingPathComponent:[NSUUID.UUID.UUIDString stringByAppendingPathExtension:extension]];
    [task.files addObject:path]; return path;
}
static NSString *AVPhotoWrite(AVPhotoTask *task, UIImage *image, CGFloat maximum)
{
    if (!image || task.cancelled || image.size.width <= 0 || image.size.height <= 0) return nil;
    CGFloat scale = MIN(1, maximum / MAX(image.size.width, image.size.height));
    CGSize size = CGSizeMake(MAX(1, round(image.size.width * scale)), MAX(1, round(image.size.height * scale)));
    // Render upright, including HEIC and rotated photos, into a bounded JPEG texture.
    UIGraphicsBeginImageContextWithOptions(size, YES, 1);
    [image drawInRect:CGRectMake(0, 0, size.width, size.height)];
    UIImage *upright = UIGraphicsGetImageFromCurrentImageContext();
    UIGraphicsEndImageContext();
    NSString *path = AVPhotoPath(task, @"jpg");
    return [UIImageJPEGRepresentation(upright, 0.9) writeToFile:path atomically:YES] ? path : nil;
}
static PHAsset *AVPhotoAsset(NSString *identifier) { return [PHAsset fetchAssetsWithLocalIdentifiers:@[identifier] options:nil].firstObject; }

extern "C" void AVPhotos_Cancel(const char *receiver, int request)
{
    NSString *key = [NSString stringWithFormat:@"%s/%d", receiver, request];
    dispatch_async(dispatch_get_main_queue(), ^{
        AVPhotoTask *task = AVPhotoTasks[key];
        if (!task) return;
        task.cancelled = YES;
        for (NSNumber *number in task.imageRequests) [[PHImageManager defaultManager] cancelImageRequest:number.intValue];
        [task.exporter cancelExport];
        for (NSString *path in task.files) [[NSFileManager defaultManager] removeItemAtPath:path error:nil];
        [AVPhotoTasks removeObjectForKey:key];
    });
}

extern "C" void AVPhotos_Request(const char *receiver, int request, const char *operation, const char *assetId, int page, const char *directory)
{
    NSString *recipient = [NSString stringWithUTF8String:receiver];
    NSString *op = [NSString stringWithUTF8String:operation];
    NSString *identifier = [NSString stringWithUTF8String:assetId];
    NSString *folder = [NSString stringWithUTF8String:directory];
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!AVPhotoTasks) AVPhotoTasks = [NSMutableDictionary dictionary];
        AVPhotoTask *task = [AVPhotoTask new]; task.receiver = recipient; task.number = request;
        task.key = [NSString stringWithFormat:@"%@/%d", recipient, request]; task.directory = folder;
        task.imageRequests = [NSMutableArray array]; task.files = [NSMutableArray array];
        AVPhotoTasks[task.key] = task;
        if ([op isEqualToString:@"authorize"])
        {
            [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelReadWrite handler:^(PHAuthorizationStatus status) {
                BOOL allowed = status == PHAuthorizationStatusAuthorized || status == PHAuthorizationStatusLimited;
                AVPhotoFinish(task, @{@"ok":@(allowed), @"limited":@(status == PHAuthorizationStatusLimited),
                    @"error":allowed ? @"" : @"Photos access is unavailable. Enable Photos access for this app in iPhone Settings, then retry."});
            }];
            return;
        }
        PHAuthorizationStatus status = [PHPhotoLibrary authorizationStatusForAccessLevel:PHAccessLevelReadWrite];
        if (status != PHAuthorizationStatusAuthorized && status != PHAuthorizationStatusLimited)
        { AVPhotoError(task, @"Photos access changed. Close this menu and open Photos again."); return; }
        if ([op isEqualToString:@"page"])
        {
            PHFetchOptions *options = [PHFetchOptions new];
            options.predicate = [NSPredicate predicateWithFormat:@"mediaType == %d OR mediaType == %d", PHAssetMediaTypeImage, PHAssetMediaTypeVideo];
            options.sortDescriptors = @[[NSSortDescriptor sortDescriptorWithKey:@"creationDate" ascending:NO]];
            PHFetchResult<PHAsset *> *assets = [PHAsset fetchAssetsWithOptions:options];
            NSUInteger start = MIN(assets.count, (NSUInteger)MAX(0, page) * 6), count = MIN((NSUInteger)6, assets.count - start);
            NSMutableArray *items = [NSMutableArray array];
            for (NSUInteger i = 0; i < count; ++i) [items addObject:[NSMutableDictionary dictionary]];
            if (count == 0) { AVPhotoFinish(task, @{@"ok":@YES, @"total":@(assets.count), @"assets":items}); return; }
            __block NSUInteger remaining = count;
            for (NSUInteger i = 0; i < count; ++i)
            {
                PHAsset *asset = assets[start + i];
                PHImageRequestOptions *imageOptions = [PHImageRequestOptions new];
                imageOptions.networkAccessAllowed = YES; imageOptions.deliveryMode = PHImageRequestOptionsDeliveryModeHighQualityFormat;
                imageOptions.resizeMode = PHImageRequestOptionsResizeModeFast;
                PHImageRequestID imageRequest = [[PHImageManager defaultManager] requestImageForAsset:asset targetSize:CGSizeMake(320, 240)
                    contentMode:PHImageContentModeAspectFit options:imageOptions resultHandler:^(UIImage *image, NSDictionary *info) {
                    if ([info[PHImageResultIsDegradedKey] boolValue]) return;
                    dispatch_async(dispatch_get_main_queue(), ^{
                        if (task.cancelled) return;
                        BOOL video = asset.mediaType == PHAssetMediaTypeVideo;
                        NSString *date = asset.creationDate ? [NSDateFormatter localizedStringFromDate:asset.creationDate dateStyle:NSDateFormatterMediumStyle timeStyle:NSDateFormatterNoStyle] : @"";
                        NSString *caption = video ? [NSString stringWithFormat:@"Video · %d:%02d · %@", (int)asset.duration / 60, (int)asset.duration % 60, date] : [@"Photo · " stringByAppendingString:date];
                        items[i] = @{@"id":asset.localIdentifier, @"kind":video ? @"video" : @"photo", @"caption":caption,
                            @"path":AVPhotoWrite(task, image, 320) ?: @""};
                        if (--remaining == 0) AVPhotoFinish(task, @{@"ok":@YES, @"total":@(assets.count), @"assets":items});
                    });
                }];
                [task.imageRequests addObject:@(imageRequest)];
            }
            return;
        }
        PHAsset *asset = AVPhotoAsset(identifier);
        if (!asset) { AVPhotoError(task, @"This item is no longer available. Refresh the library."); return; }
        if (asset.mediaType == PHAssetMediaTypeVideo)
        {
            PHVideoRequestOptions *options = [PHVideoRequestOptions new]; options.networkAccessAllowed = YES;
            options.deliveryMode = PHVideoRequestOptionsDeliveryModeHighQualityFormat;
            PHImageRequestID imageRequest = [[PHImageManager defaultManager] requestExportSessionForVideo:asset options:options
                exportPreset:AVAssetExportPreset1280x720 resultHandler:^(AVAssetExportSession *exporter, NSDictionary *info) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    if (task.cancelled) return;
                    if (!exporter || ![exporter.supportedFileTypes containsObject:AVFileTypeMPEG4])
                    { AVPhotoError(task, @"This video could not be downloaded or converted. Try another item."); return; }
                    task.exporter = exporter;
                    NSString *path = AVPhotoPath(task, @"mp4");
                    exporter.outputURL = [NSURL fileURLWithPath:path]; exporter.outputFileType = AVFileTypeMPEG4;
                    [exporter exportAsynchronouslyWithCompletionHandler:^{
                        dispatch_async(dispatch_get_main_queue(), ^{
                            if (task.cancelled || exporter.status != AVAssetExportSessionStatusCompleted)
                            {
                                [[NSFileManager defaultManager] removeItemAtPath:path error:nil];
                                if (!task.cancelled) AVPhotoError(task, @"Video export failed. Check iCloud connectivity and available storage.");
                            }
                            else AVPhotoFinish(task, @{@"ok":@YES, @"path":path});
                        });
                    }];
                });
            }];
            [task.imageRequests addObject:@(imageRequest)];
        }
        else
        {
            PHImageRequestOptions *options = [PHImageRequestOptions new]; options.networkAccessAllowed = YES;
            options.deliveryMode = PHImageRequestOptionsDeliveryModeHighQualityFormat; options.resizeMode = PHImageRequestOptionsResizeModeExact;
            PHImageRequestID imageRequest = [[PHImageManager defaultManager] requestImageForAsset:asset targetSize:CGSizeMake(2048, 2048)
                contentMode:PHImageContentModeAspectFit options:options resultHandler:^(UIImage *image, NSDictionary *info) {
                if ([info[PHImageResultIsDegradedKey] boolValue]) return;
                dispatch_async(dispatch_get_main_queue(), ^{
                    if (task.cancelled) return;
                    NSString *path = AVPhotoWrite(task, image, 2048);
                    if (path) AVPhotoFinish(task, @{@"ok":@YES, @"path":path});
                    else AVPhotoError(task, @"Photo could not be loaded. Check iCloud connectivity and try again.");
                });
            }];
            [task.imageRequests addObject:@(imageRequest)];
        }
    });
}
