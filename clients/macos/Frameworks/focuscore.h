#ifndef FOCUSCORE_H
#define FOCUSCORE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Returns a version string for the FocusCore library.
const char* focuscore_version(void);

// Opens the FocusCore database at the given path.
// Returns 0 on success, non-zero error code on failure.
int32_t focuscore_open(const char* db_path);

// Closes the FocusCore database.
// Returns 0 on success, non-zero error code on failure.
int32_t focuscore_close(void);

// Quick-adds a task with the given title.
// Returns 0 on success, non-zero error code on failure.
int32_t focuscore_quick_add(const char* title);

#ifdef __cplusplus
}
#endif

#endif // FOCUSCORE_H