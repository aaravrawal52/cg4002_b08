#include "AVMarkerDetector.h"
#include "AprilTag/apriltag.h"
#include "AprilTag/apriltag_pose.h"
#include "AprilTag/tag25h9.h"
#include <math.h>
#include <stdlib.h>
#include <string.h>

typedef struct { double x, y, edge, time; int valid; } MarkerTrack;
struct AVMarkerDetector {
    apriltag_detector_t *detector; apriltag_family_t *family;
    MarkerTrack tracked[4], pending[4];
    AVMarkerConfig config;
    int width, height;
    double time;
};

static int nearby(MarkerTrack previous, double x, double y, double edge, double time)
{
    return previous.valid && time >= previous.time && time - previous.time <= .5
        && edge >= previous.edge * .5 && edge <= previous.edge * 2
        && hypot(x - previous.x, y - previous.y) <= fmax(20, previous.edge * 1.5);
}

int AVMarker_ConfigValid(const AVMarkerConfig *c)
{
    if (!c || !isfinite(c->minimumMargin) || c->minimumMargin < 1 || c->minimumMargin > 255
        || !isfinite(c->minimumEdgePixels) || c->minimumEdgePixels < 4
        || !isfinite(c->quadDecimate) || c->quadDecimate < 1 || c->quadDecimate > 4
        || !isfinite(c->tipWidthMetres) || c->tipWidthMetres < .005f || c->tipWidthMetres > .1f) return 0;
    for (int i = 0; i < 4; ++i) {
        if (c->ids[i] < 0 || c->ids[i] >= 35) return 0;
        for (int j = 0; j < i; ++j) if (c->ids[i] == c->ids[j]) return 0;
    }
    for (int i = 0; i < 3; ++i)
        if (!isfinite(c->leftContactOffset[i]) || !isfinite(c->rightContactOffset[i])
            || fabs(c->leftContactOffset[i]) > .05f || fabs(c->rightContactOffset[i]) > .05f) return 0;
    return 1;
}

AVMarkerDetector *AVMarker_Create(void)
{
    AVMarkerDetector *d = calloc(1, sizeof(*d));
    if (!d) return NULL;
    d->family = tag25h9_create();
    d->detector = apriltag_detector_create();
    d->detector->nthreads = 2;
    d->detector->refine_edges = true;
    // Recover one damaged cell; temporal checks below protect acquisition and identity.
    apriltag_detector_add_family_bits(d->detector, d->family, 1);
    return d;
}

void AVMarker_Destroy(AVMarkerDetector *d)
{
    if (!d) return;
    apriltag_detector_destroy(d->detector);
    tag25h9_destroy(d->family);
    free(d);
}

static int contact_offset(apriltag_detection_t *tag, const float offset[3], float tagWidth,
                          double fx, double fy, double cx, double cy, double out[3])
{
    if (offset[0] == 0 && offset[1] == 0 && offset[2] == 0) return 1;
    if (!isfinite(fx) || !isfinite(fy) || fx <= 0 || fy <= 0) return 0;
    apriltag_detection_info_t info = { tag, tagWidth, fx, fy, cx, cy };
    apriltag_pose_t pose = {0};
    double error = estimate_tag_pose(&info, &pose);
    int valid = isfinite(error) && pose.R && pose.t && MATD_EL(pose.t, 2, 0) > 0;
    if (valid) {
        // AprilTag's local Y points down; local Z points into the printed face.
        for (int r = 0; r < 3; ++r) {
            out[r] = MATD_EL(pose.R, r, 0) * offset[0] - MATD_EL(pose.R, r, 1) * offset[1]
                - MATD_EL(pose.R, r, 2) * offset[2];
            valid = valid && isfinite(out[r]);
        }
    }
    if (pose.R) matd_destroy(pose.R);
    if (pose.t) matd_destroy(pose.t);
    return valid;
}

int AVMarker_Detect(AVMarkerDetector *d, const uint8_t *pixels, int width, int height, int stride,
                    const AVMarkerConfig *c, double time, double fx, double fy, double cx, double cy, AVMarkerPoint points[4])
{
    if (!points) return -1;
    memset(points, 0, sizeof(AVMarkerPoint) * 4);
    if (!d || !pixels || width <= 0 || height <= 0 || stride < width || !AVMarker_ConfigValid(c) || !isfinite(time)) return -1;
    if (width != d->width || height != d->height || time <= d->time || memcmp(c, &d->config, sizeof(*c))) {
        memset(d->tracked, 0, sizeof(d->tracked));
        memset(d->pending, 0, sizeof(d->pending));
    }
    d->width = width; d->height = height; d->config = *c; d->time = time;
    image_u8_t image = { width, height, stride, (uint8_t *)pixels };
    d->detector->quad_decimate = c->quadDecimate;
    zarray_t *tags = apriltag_detector_detect(d->detector, &image);
    int seen[4] = {0};
    MarkerTrack observations[4] = {{0}};
    int corrected[4] = {0};
    float margins[4] = {0};
    for (int i = 0; i < zarray_size(tags); ++i) {
        apriltag_detection_t *tag;
        zarray_get(tags, i, &tag);
        int slot = -1;
        for (int s = 0; s < 4; ++s) if (tag->id == c->ids[s]) slot = s;
        if (slot < 0 || tag->hamming < 0 || tag->hamming > 1 || !isfinite(tag->decision_margin)
            || tag->decision_margin < c->minimumMargin * (2.f / 3.f) || !isfinite(tag->c[0]) || !isfinite(tag->c[1])) continue;
        int good = 1;
        double meanEdge = 0;
        for (int p = 0; p < 4; ++p) {
            int q = (p + 1) % 4;
            double edge = hypot(tag->p[p][0] - tag->p[q][0], tag->p[p][1] - tag->p[q][1]);
            good = good && isfinite(edge) && edge >= c->minimumEdgePixels;
            meanEdge += edge / 4;
        }
        if (!good || tag->c[0] < 0 || tag->c[0] >= width || tag->c[1] < 0 || tag->c[1] >= height) continue;
        AVMarkerPoint *point = &points[slot];
        if (++seen[slot] > 1) { point->valid = 0; continue; }
        observations[slot] = (MarkerTrack){tag->c[0], tag->c[1], meanEdge, time, 1};
        corrected[slot] = tag->hamming;
        margins[slot] = tag->decision_margin;
        point->id = tag->id; point->x = tag->c[0]; point->y = tag->c[1];
        point->confidence = tag->decision_margin / (tag->decision_margin + 25.f);
        const float *offset = slot == 1 ? c->leftContactOffset : c->rightContactOffset;
        point->valid = slot % 2 == 0 || contact_offset(tag, offset, c->tipWidthMetres, fx, fy, cx, cy, point->contactOffset);
    }
    apriltag_detections_destroy(tags);
    int count = 0;
    for (int s = 0; s < 4; ++s) {
        MarkerTrack sample = observations[s], old = d->tracked[s];
        if (!points[s].valid || seen[s] != 1) {
            d->pending[s].valid = 0;
            if (seen[s] > 1) d->tracked[s].valid = 0;
            continue;
        }
        int continuing = nearby(old, sample.x, sample.y, sample.edge, time);
        int strong = margins[s] >= c->minimumMargin;
        int recent = old.valid && time - old.time <= .5;
        int confirmed = nearby(d->pending[s], sample.x, sample.y, sample.edge, time)
            && time - d->pending[s].time <= .2;
        // Weak/corrected readings may continue a nearby track. New corrected IDs and
        // sudden jumps need two strong, spatially consistent observations first.
        int accept = continuing || (strong && ((!recent && !corrected[s]) || confirmed));
        if (accept) { d->tracked[s] = sample; d->pending[s].valid = 0; ++count; }
        else {
            points[s].valid = 0;
            d->pending[s] = sample;
            d->pending[s].valid = strong;
        }
    }
    return count;
}
