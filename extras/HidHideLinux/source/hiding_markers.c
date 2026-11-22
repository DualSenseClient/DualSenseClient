#include "hiding_markers.h"

#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

static int marker_path(char* destination, size_t size, const char* run_dir, const char* id)
{
    if (run_dir == NULL || !hiding_id_valid(id))
    {
        return -EINVAL;
    }

    int written = snprintf(destination, size, "%s/hidden/%s", run_dir, id);
    if (written < 0 || (size_t)written >= size)
    {
        return -ENAMETOOLONG;
    }

    return 0;
}

static int temp_path(char* destination, size_t size, const char* run_dir, const char* id)
{
    if (run_dir == NULL || !hiding_id_valid(id))
    {
        return -EINVAL;
    }

    int written = snprintf(destination, size, "%s/tmp/%s", run_dir, id);
    if (written < 0 || (size_t)written >= size)
    {
        return -ENAMETOOLONG;
    }

    return 0;
}

static int ensure_directory(const char* path, mode_t mode)
{
    if (mkdir(path, mode) != 0 && errno != EEXIST)
    {
        return -errno;
    }

    struct stat status;
    if (lstat(path, &status) != 0)
    {
        return -errno;
    }

    if (!S_ISDIR(status.st_mode))
    {
        return -ENOTDIR;
    }

    if (status.st_uid != geteuid())
    {
        return -EACCES;
    }

    if ((status.st_mode & (S_IWGRP | S_IWOTH)) != 0)
    {
        return -EPERM;
    }

    return 0;
}

static int read_all(int fd, void* data, size_t length)
{
    size_t offset = 0;
    while (offset < length)
    {
        ssize_t count = read(fd, (char*)data + offset, length - offset);
        if (count < 0)
        {
            if (errno == EINTR)
            {
                continue;
            }

            return -errno;
        }

        if (count == 0)
        {
            return -EIO;
        }

        offset += (size_t)count;
    }

    return 0;
}

int hiding_marker_set(const char* run_dir, const char* id, const struct hiding_snapshot* snapshot)
{
    if (run_dir == NULL || snapshot == NULL || snapshot->magic != HIDING_SNAPSHOT_MAGIC
        || snapshot->version != HIDING_SNAPSHOT_VERSION
        || snapshot->device_count > HIDING_MAX_DEVICES)
    {
        return -EINVAL;
    }

    char hidden_dir[HIDING_MAX_PATH];
    char temporary_dir[HIDING_MAX_PATH];
    char path[HIDING_MAX_PATH];
    char temporary[HIDING_MAX_PATH];

    int written = snprintf(hidden_dir, sizeof(hidden_dir), "%s/hidden", run_dir);
    if (written < 0 || (size_t)written >= sizeof(hidden_dir))
    {
        return -ENAMETOOLONG;
    }

    written = snprintf(temporary_dir, sizeof(temporary_dir), "%s/tmp", run_dir);
    if (written < 0 || (size_t)written >= sizeof(temporary_dir))
    {
        return -ENAMETOOLONG;
    }

    int result = ensure_directory(run_dir, 0755);
    if (result != 0)
    {
        return result;
    }

    result = ensure_directory(hidden_dir, 0700);
    if (result != 0)
    {
        return result;
    }

    result = ensure_directory(temporary_dir, 0700);
    if (result != 0)
    {
        return result;
    }

    int path_result = marker_path(path, sizeof(path), run_dir, id);
    if (path_result != 0)
    {
        return path_result;
    }

    path_result = temp_path(temporary, sizeof(temporary), run_dir, id);
    if (path_result != 0)
    {
        return path_result;
    }

    int fd = open(temporary, O_CREAT | O_TRUNC | O_WRONLY | O_CLOEXEC | O_NOFOLLOW | O_NONBLOCK,
                  0600);
    if (fd < 0)
    {
        return -errno;
    }

    result = hiding_write_all(fd, (const char*)snapshot, sizeof(*snapshot));
    if (result == 0 && fsync(fd) != 0)
    {
        result = -errno;
    }

    if (close(fd) != 0 && result == 0)
    {
        result = -errno;
    }

    if (result != 0)
    {
        unlink(temporary);
        return result;
    }

    if (rename(temporary, path) != 0)
    {
        int error = errno;
        unlink(temporary);
        return -error;
    }

    return 0;
}

int hiding_marker_load(const char* run_dir, const char* id, struct hiding_snapshot* snapshot)
{
    if (run_dir == NULL || snapshot == NULL || !hiding_id_valid(id))
    {
        return -EINVAL;
    }

    char path[HIDING_MAX_PATH];
    int path_result = marker_path(path, sizeof(path), run_dir, id);
    if (path_result != 0)
    {
        return path_result;
    }

    int fd = open(path, O_RDONLY | O_CLOEXEC | O_NOFOLLOW | O_NONBLOCK);
    if (fd < 0)
    {
        return errno == ENOENT ? -ENODATA : -errno;
    }

    struct stat status;
    if (fstat(fd, &status) != 0 || !S_ISREG(status.st_mode))
    {
        close(fd);
        return -EINVAL;
    }

    if (status.st_size == 0)
    {
        close(fd);
        return -ENODATA;
    }

    if (status.st_size != (off_t)sizeof(*snapshot))
    {
        close(fd);
        return -EINVAL;
    }

    int result = read_all(fd, snapshot, sizeof(*snapshot));
    close(fd);
    if (result != 0)
    {
        return result;
    }

    if (snapshot->magic != HIDING_SNAPSHOT_MAGIC
        || snapshot->version != HIDING_SNAPSHOT_VERSION
        || snapshot->device_count > HIDING_MAX_DEVICES)
    {
        return -EINVAL;
    }

    return 0;
}

int hiding_marker_clear(const char* run_dir, const char* id)
{
    char path[HIDING_MAX_PATH];
    int path_result = marker_path(path, sizeof(path), run_dir, id);
    if (path_result != 0)
    {
        return path_result;
    }

    if (unlink(path) != 0 && errno != ENOENT)
    {
        return -errno;
    }

    char temporary[HIDING_MAX_PATH];
    path_result = temp_path(temporary, sizeof(temporary), run_dir, id);
    if (path_result == 0)
    {
        unlink(temporary);
    }

    return 0;
}

bool hiding_marker_exists(const char* run_dir, const char* id)
{
    char path[HIDING_MAX_PATH];
    if (marker_path(path, sizeof(path), run_dir, id) != 0)
    {
        return false;
    }

    struct stat status;
    return lstat(path, &status) == 0 && S_ISREG(status.st_mode);
}