#include "hiding.h"

#include <errno.h>
#include <fcntl.h>
#include <grp.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/xattr.h>
#include <unistd.h>

int hiding_set_permissions(const struct hiding_device* device, const char* group_name)
{
    if (device == NULL || group_name == NULL || device->node_count == 0)
    {
        return -EINVAL;
    }

    struct group* group = getgrnam(group_name);
    if (group == NULL)
    {
        return -ENOENT;
    }

    for (size_t i = 0; i < device->node_count; i++)
    {
        const struct hiding_node* node = &device->nodes[i];
        if (!node->present)
        {
            continue;
        }

        if (chown(node->dev_path, 0, group->gr_gid) != 0)
        {
            return -errno;
        }

        if (chmod(node->dev_path, 0660) != 0)
        {
            return -errno;
        }

        if (removexattr(node->dev_path, "system.posix_acl_access") != 0
            && errno != ENODATA
            && errno != ENOTSUP
            && errno != ENOENT)
        {
            return -errno;
        }
    }

    return 0;
}

int hiding_capture_acls(struct hiding_device* device)
{
    if (device == NULL)
    {
        return -EINVAL;
    }

    for (size_t i = 0; i < device->node_count; i++)
    {
        struct hiding_node* node = &device->nodes[i];
        if (!node->present)
        {
            continue;
        }

        ssize_t size = getxattr(node->dev_path, "system.posix_acl_access", NULL, 0);
        if (size < 0)
        {
            if (errno == ENODATA || errno == ENOTSUP || errno == ENOENT)
            {
                continue;
            }

            return -errno;
        }

        if (size == 0)
        {
            continue;
        }

        if ((size_t)size > sizeof(node->acl))
        {
            return -E2BIG;
        }

        ssize_t read_size = getxattr(node->dev_path, "system.posix_acl_access", node->acl,
                                     sizeof(node->acl));
        if (read_size < 0)
        {
            if (errno == ENODATA || errno == ENOTSUP || errno == ENOENT)
            {
                continue;
            }

            return -errno;
        }

        node->acl_size = (size_t)read_size;
        node->has_acl = read_size > 0;
    }

    return 0;
}

static const struct hiding_node* find_snapshot_node(const struct hiding_device* snapshot,
                                                    const struct hiding_node* current)
{
    if (snapshot == NULL || current == NULL)
    {
        return NULL;
    }

    for (size_t i = 0; i < snapshot->node_count; i++)
    {
        if (snapshot->nodes[i].has_original
            && strcmp(snapshot->nodes[i].dev_path, current->dev_path) == 0)
        {
            return &snapshot->nodes[i];
        }
    }

    for (size_t i = 0; i < snapshot->node_count; i++)
    {
        if (snapshot->nodes[i].has_original
            && snapshot->nodes[i].rdev == current->rdev
            && snapshot->nodes[i].kind == current->kind)
        {
            return &snapshot->nodes[i];
        }
    }

    return NULL;
}

int hiding_restore_permissions(const struct hiding_device* snapshot,
                               const struct hiding_device* current)
{
    if (current == NULL)
    {
        return -EINVAL;
    }

    struct group* input = getgrnam("input");

    for (size_t i = 0; i < current->node_count; i++)
    {
        const struct hiding_node* node = &current->nodes[i];
        if (!node->present)
        {
            continue;
        }

        const struct hiding_node* saved = find_snapshot_node(snapshot, node);
        uid_t uid;
        gid_t gid;
        mode_t mode;

        if (saved != NULL)
        {
            uid = saved->original_uid;
            gid = saved->original_gid;
            mode = saved->original_mode;
        }
        else if (node->kind == HIDING_NODE_HIDRAW)
        {
            uid = 0;
            gid = 0;
            mode = 0660;
        }
        else
        {
            uid = 0;
            gid = input == NULL ? 0 : input->gr_gid;
            mode = 0660;
        }

        if (chown(node->dev_path, uid, gid) != 0 && errno != ENOENT)
        {
            return -errno;
        }

        if (chmod(node->dev_path, mode) != 0 && errno != ENOENT)
        {
            return -errno;
        }

        if (saved != NULL && saved->has_acl)
        {
            if (setxattr(node->dev_path, "system.posix_acl_access", saved->acl, saved->acl_size,
                         0) != 0
                && errno != ENOENT)
            {
                return -errno;
            }
        }
        else if (removexattr(node->dev_path, "system.posix_acl_access") != 0
            && errno != ENODATA
            && errno != ENOTSUP
            && errno != ENOENT)
        {
            return -errno;
        }
    }

    return 0;
}

int hiding_trigger_udev(const struct hiding_device* device)
{
    if (device == NULL)
    {
        return -EINVAL;
    }

    for (size_t i = 0; i < device->node_count; i++)
    {
        const struct hiding_node* node = &device->nodes[i];
        char uevent[HIDING_MAX_PATH];
        int written = snprintf(uevent, sizeof(uevent), "%s/uevent", node->sysfs_path);
        if (written < 0 || (size_t)written >= sizeof(uevent))
        {
            return -ENAMETOOLONG;
        }

        int fd = open(uevent, O_WRONLY | O_CLOEXEC);
        if (fd < 0)
        {
            if (errno == ENOENT)
            {
                continue;
            }

            return -errno;
        }

        const char change[] = "change\n";
        int write_result = hiding_write_all(fd, change, sizeof(change) - 1);
        close(fd);
        if (write_result != 0)
        {
            return write_result;
        }
    }

    return 0;
}