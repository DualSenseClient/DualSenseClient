#include "hiding.h"

#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <linux/hidraw.h>
#include <linux/input.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <unistd.h>

#ifndef HIDIOCREVOKE
#define HIDIOCREVOKE _IOW('H', 0x0D, int)
#endif

static bool process_uses_uid(pid_t pid, uid_t uid)
{
    char path[64];
    int written = snprintf(path, sizeof(path), "/proc/%d/status", (int)pid);
    if (written < 0 || (size_t)written >= sizeof(path))
    {
        return false;
    }

    FILE* file = fopen(path, "r");
    if (file == NULL)
    {
        return false;
    }

    bool matches = false;
    char line[256];
    while (fgets(line, sizeof(line), file) != NULL)
    {
        if (strncmp(line, "Uid:", 4) != 0)
        {
            continue;
        }

        unsigned int real = 0;
        unsigned int effective = 0;
        if (hiding_parse_uids(line + 4, &real, &effective))
        {
            (void)real;
            matches = effective == (unsigned int)uid;
        }

        break;
    }

    fclose(file);
    return matches;
}

static bool node_kind_for_rdev(const struct hiding_device* device, dev_t rdev,
                               enum hiding_node_kind* kind)
{
    for (size_t i = 0; i < device->node_count; i++)
    {
        const struct hiding_node* node = &device->nodes[i];
        if (node->present && node->rdev == rdev)
        {
            *kind = node->kind;
            return true;
        }
    }

    return false;
}

static bool revoke_descriptor(int fd, enum hiding_node_kind kind)
{
    int result;
    if (kind == HIDING_NODE_HIDRAW)
    {
        result = ioctl(fd, HIDIOCREVOKE, 0);
    }
    else if (kind == HIDING_NODE_EVDEV)
    {
        result = ioctl(fd, EVIOCREVOKE, 0);
    }
    else
    {
        return false;
    }

    if (result == 0 || errno == ENODEV)
    {
        return true;
    }

    return false;
}

int hiding_revoke_handles(const struct hiding_device* device, pid_t allowed_pid,
                          uid_t allowed_uid, bool* needs_rebind)
{
    if (device == NULL || needs_rebind == NULL)
    {
        return -EINVAL;
    }

    *needs_rebind = false;

    DIR* processes = opendir("/proc");
    if (processes == NULL)
    {
        return -errno;
    }

    struct dirent* process_entry;
    while ((process_entry = readdir(processes)) != NULL)
    {
        const char* end = NULL;
        unsigned long parsed_pid;
        if (!hiding_parse_uint(process_entry->d_name, 10, INT_MAX, &parsed_pid, &end)
            || *end != '\0' || parsed_pid == 0)
        {
            continue;
        }

        pid_t pid = (pid_t)parsed_pid;
        if (pid == getpid() || pid == allowed_pid || !process_uses_uid(pid, allowed_uid))
        {
            continue;
        }

        char fd_directory_path[64];
        int written = snprintf(fd_directory_path, sizeof(fd_directory_path), "/proc/%d/fd", (int)pid);
        if (written < 0 || (size_t)written >= sizeof(fd_directory_path))
        {
            continue;
        }

        DIR* descriptors = opendir(fd_directory_path);
        if (descriptors == NULL)
        {
            continue;
        }

        struct dirent* descriptor_entry;
        while ((descriptor_entry = readdir(descriptors)) != NULL)
        {
            const char* fd_end = NULL;
            unsigned long parsed_fd;
            if (!hiding_parse_uint(descriptor_entry->d_name, 10, INT_MAX, &parsed_fd, &fd_end)
                || *fd_end != '\0')
            {
                continue;
            }

            char fd_path[HIDING_MAX_PATH];
            written = snprintf(fd_path, sizeof(fd_path), "%s/%s", fd_directory_path, descriptor_entry->d_name);
            if (written < 0 || (size_t)written >= sizeof(fd_path))
            {
                continue;
            }

            struct stat status;
            if (stat(fd_path, &status) != 0 || !S_ISCHR(status.st_mode))
            {
                continue;
            }

            enum hiding_node_kind kind;
            if (!node_kind_for_rdev(device, status.st_rdev, &kind))
            {
                continue;
            }

            if (kind == HIDING_NODE_OTHER)
            {
                *needs_rebind = true;
                continue;
            }

            int pidfd = (int)syscall(SYS_pidfd_open, pid, 0);
            if (pidfd < 0)
            {
                *needs_rebind = true;
                continue;
            }

            if (!process_uses_uid(pid, allowed_uid))
            {
                *needs_rebind = true;
                close(pidfd);
                continue;
            }

#ifdef SYS_pidfd_getfd
            int duplicate = (int)syscall(SYS_pidfd_getfd, pidfd, (int)parsed_fd, 0);
            if (duplicate < 0)
            {
                *needs_rebind = true;
                close(pidfd);
                continue;
            }
#else
            int duplicate = -1;
            *needs_rebind = true;
#endif

            if (duplicate >= 0)
            {
                struct stat duplicate_status;
                if (fstat(duplicate, &duplicate_status) != 0
                    || !S_ISCHR(duplicate_status.st_mode)
                    || duplicate_status.st_rdev != status.st_rdev)
                {
                    *needs_rebind = true;
                    close(duplicate);
                    close(pidfd);
                    continue;
                }

                if (!revoke_descriptor(duplicate, kind))
                {
                    *needs_rebind = true;
                }

                close(duplicate);
            }

            close(pidfd);
        }

        closedir(descriptors);
    }

    closedir(processes);
    if (*needs_rebind)
    {
        fprintf(stderr, "dualsense-hiding-helper: some controller handles need the rebind fallback\n");
    }

    return 0;
}

static int write_driver_command(const char* path, const char* device_name)
{
    int fd = open(path, O_WRONLY | O_CLOEXEC);
    if (fd < 0)
    {
        return -errno;
    }

    int result = hiding_write_all(fd, device_name, strlen(device_name));
    if (result == 0)
    {
        result = hiding_write_all(fd, "\n", 1);
    }

    close(fd);
    return result;
}

int hiding_rebind_driver(const struct hiding_device* device)
{
    if (device == NULL || device->driver_path[0] == '\0' || device->device_name[0] == '\0')
    {
        return -EINVAL;
    }

    char unbind[HIDING_MAX_PATH];
    char bind[HIDING_MAX_PATH];
    int written = snprintf(unbind, sizeof(unbind), "%s/unbind", device->driver_path);
    if (written < 0 || (size_t)written >= sizeof(unbind))
    {
        return -ENAMETOOLONG;
    }

    written = snprintf(bind, sizeof(bind), "%s/bind", device->driver_path);
    if (written < 0 || (size_t)written >= sizeof(bind))
    {
        return -ENAMETOOLONG;
    }

    int result = write_driver_command(unbind, device->device_name);
    if (result != 0)
    {
        fprintf(stderr, "dualsense-hiding-helper: unbind %s failed: %s\n",
                device->device_name, strerror(-result));
        return result;
    }

    for (int attempt = 0; attempt < 10; attempt++)
    {
        usleep(200000);
        result = write_driver_command(bind, device->device_name);
        if (result == 0)
        {
            return 0;
        }
    }

    fprintf(stderr, "dualsense-hiding-helper: bind %s failed after retries: %s\n",
            device->device_name, strerror(-result));
    return result;
}