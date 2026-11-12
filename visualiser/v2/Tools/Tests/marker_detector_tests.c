#include "AVMarkerDetector.h"
#include "AprilTag/apriltag.h"
#include "AprilTag/tag25h9.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#undef assert
#define assert(x) do { if (!(x)) { fprintf(stderr, "CHECK FAILED: %s at line %d\n", #x, __LINE__); exit(1); } } while (0)

static apriltag_family_t *family;
static AVMarkerDetector *detector;
static AVMarkerConfig config = {{0, 1, 2, 3}, 30, 14, 1, .014f, {0}, {0}};
static AVMarkerPoint points[4];

static image_u8_t *canvas(void)
{
    image_u8_t *im = image_u8_create(700, 500);
    memset(im->buf, 255, im->stride * im->height);
    return im;
}

static void stamp(image_u8_t *image, int id, int x, int y, int scale, int rotation, int damage)
{
    image_u8_t *tag = apriltag_to_image(family, id);
    if (damage) tag->buf[(family->bit_y[0] + 1) * tag->stride + family->bit_x[0] + 1] ^= 255;
    int size = tag->width * scale;
    for (int dy = 0; dy < size; ++dy) for (int dx = 0; dx < size; ++dx) {
        int tx = dx / scale, ty = dy / scale;
        for (int r = 0; r < rotation; ++r) { int old = tx; tx = ty; ty = tag->width - 1 - old; }
        image->buf[(y + dy) * image->stride + x + dx] = tag->buf[ty * tag->stride + tx];
    }
    image_u8_destroy(tag);
}

static int detect(image_u8_t *image)
{
    return AVMarker_Detect(detector, image->buf, image->width, image->height, image->stride,
        &config, 600, 600, 350, 250, points);
}

int main(int argc, char **argv)
{
    family = tag25h9_create(); detector = AVMarker_Create();
    assert(AVMarker_ConfigValid(&config));
    image_u8_t *im = canvas();
    for (int i = 0; i < 4; ++i) stamp(im, i, 500 - i * 140, 100, 8, i, 0);
    assert(detect(im) == 4);
    for (int i = 0; i < 4; ++i) {
        assert(points[i].valid && points[i].id == i);
        assert(fabs(points[i].x - (500 - i * 140 + 36)) < 2);
    }
    config.quadDecimate = 1.5f; assert(detect(im) == 4); config.quadDecimate = 1;
    image_u8_destroy(im);
    im = canvas(); stamp(im, 0, 50, 50, 8, 0, 0); stamp(im, 2, 400, 150, 8, 2, 0);
    assert(detect(im) == 2 && points[0].valid && points[2].valid);
    assert(!points[1].valid && !points[3].valid);
    image_u8_destroy(im);
    im = canvas(); stamp(im, 0, 50, 50, 8, 0, 0); stamp(im, 0, 400, 150, 8, 2, 0);
    stamp(im, 3, 200, 300, 8, 0, 0);
    assert(detect(im) == 1 && !points[0].valid && points[3].valid);
    image_u8_destroy(im);
    im = canvas(); stamp(im, 12, 50, 50, 8, 0, 0); assert(detect(im) == 0); image_u8_destroy(im);
    im = canvas(); stamp(im, 0, 50, 50, 8, 0, 1); assert(detect(im) == 0); image_u8_destroy(im);
    im = canvas(); assert(detect(im) == 0); image_u8_destroy(im);
    im = canvas(); stamp(im, 1, 50, 50, 8, 0, 0);
    config.minimumEdgePixels = 100; assert(detect(im) == 0); config.minimumEdgePixels = 14;
    for (int y = 0; y < im->height; ++y) for (int x = 0; x < im->width; ++x)
        im->buf[y * im->stride + x] = im->buf[y * im->stride + x] == 0 ? 110 : 140;
    config.minimumMargin = 255; assert(detect(im) == 0); config.minimumMargin = 30;
    image_u8_destroy(im); im = canvas(); stamp(im, 1, 50, 50, 8, 0, 0);
    config.leftContactOffset[0] = .003f; config.leftContactOffset[1] = .008f; config.leftContactOffset[2] = -.005f;
    assert(detect(im) == 1);
    assert(fabs(points[1].contactOffset[0] - .003) < .001);
    assert(fabs(points[1].contactOffset[1] + .008) < .001);
    assert(fabs(points[1].contactOffset[2] - .005) < .001);
    memset(config.leftContactOffset, 0, sizeof(config.leftContactOffset));
    config.ids[1] = 0; assert(detect(im) == -1); config.ids[1] = 1;
    config.ids[1] = 35; assert(detect(im) == -1); config.ids[1] = 1;
    image_u8_destroy(im);
    for (int id = 0; id < 4 && argc > 1; ++id) {
        char path[1024]; snprintf(path, sizeof(path), "%s/%d.pgm", argv[1], id);
        image_u8_t *printed = image_u8_create_from_pnm(path), *reference = apriltag_to_image(family, id);
        assert(printed && printed->width == reference->width && printed->height == reference->height);
        for (int y = 0; y < printed->height; ++y)
            assert(memcmp(printed->buf + y * printed->stride, reference->buf + y * reference->stride, printed->width) == 0);
        image_u8_destroy(printed); image_u8_destroy(reference);
    }
    AVMarker_Destroy(detector); tag25h9_destroy(family);
    puts("MARKER_NATIVE_TESTS_PASSED: four roles, rotations, crossing, missing/duplicate/unknown/damaged tags, quality gates, offsets, print patterns");
    return 0;
}
