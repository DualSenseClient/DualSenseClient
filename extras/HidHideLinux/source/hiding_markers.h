#ifndef DUAL_SENSE_HIDING_MARKERS_H
#define DUAL_SENSE_HIDING_MARKERS_H

#include "hiding.h"

int hiding_marker_set(const char* run_dir, const char* id, const struct hiding_snapshot* snapshot);
int hiding_marker_load(const char* run_dir, const char* id, struct hiding_snapshot* snapshot);
int hiding_marker_clear(const char* run_dir, const char* id);
bool hiding_marker_exists(const char* run_dir, const char* id);

#endif