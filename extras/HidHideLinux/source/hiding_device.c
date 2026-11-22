#include "hiding.h"

#include <dirent.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>

#define SONY_VENDOR 0x054C
#define DUALSENSE_PRODUCT 0x0CE6
#define DUALSENSE_EDGE_PRODUCT 0x0DF2

static int copy_string(char* destination, size_t size, const char* source)
{
    int written = snprintf(destination, size, "%s", source);
    if (written < 0 || (size_t)written >= size)
    {
        return -ENAMETOOLONG;
    }

    return 0;
}

static bool read_uevent_value(const char* uevent_path, const char* prefix, char* value, size_t size)
{
    FILE* file = fopen(uevent_path, "r");
    if (file == NULL)
    {
        return false;
    }

    bool found = false;
    char line[HIDING_MAX_PATH];
    size_t prefix_length = strlen(prefix);
    while (fgets(line, sizeof(line), file) != NULL)
    {
        size_t length = strlen(line);
        while (length > 0 && (line[length - 1] == '\n' || line[length - 1] == '\r'))
        {
            line[--length] = '\0';
        }

        if (length >= prefix_length && strncmp(line, prefix, prefix_length) == 0)
        {
            copy_string(value, size, line + prefix_length);
            found = true;
            break;
        }
    }

    fclose(file);
    return found;
}

static int fill_device(const char* devices_path, const char* sysfs_root, const char* entry_name,
                       const char* id, struct hiding_device* out)
{
    char uevent_path[HIDING_MAX_PATH];
    int written = snprintf(uevent_path, sizeof(uevent_path), "%s/%s/uevent", devices_path,
                           entry_name);
    if (written < 0 || (size_t)written >= sizeof(uevent_path))
    {
        return 1;
    }

    char hid_id[HIDING_MAX_ID];
    if (!read_uevent_value(uevent_path, "HID_ID=", hid_id, sizeof(hid_id)))
    {
        return 1;
    }

    unsigned int bus = 0;
    unsigned int vendor = 0;
    unsigned int product = 0;
    if (!hiding_parse_hid_id(hid_id, &bus, &vendor, &product))
    {
        return 1;
    }

    if (vendor != SONY_VENDOR || (product != DUALSENSE_PRODUCT && product != DUALSENSE_EDGE_PRODUCT))
    {
        return 1;
    }

    char uniq[HIDING_MAX_ID];
    if (!read_uevent_value(uevent_path, "HID_UNIQ=", uniq, sizeof(uniq)) || strcmp(uniq, id) != 0)
    {
        return 1;
    }

    char driver[HIDING_MAX_ID];
    bool has_driver = read_uevent_value(uevent_path, "DRIVER=", driver, sizeof(driver));

    memset(out, 0, sizeof(*out));
    out->bus = bus;
    if (copy_string(out->id, sizeof(out->id), id) != 0
        || copy_string(out->device_name, sizeof(out->device_name), entry_name) != 0)
    {
        return -ENAMETOOLONG;
    }

    written = snprintf(out->sysfs_path, sizeof(out->sysfs_path), "%s/%s", devices_path, entry_name);
    if (written < 0 || (size_t)written >= sizeof(out->sysfs_path))
    {
        return -ENAMETOOLONG;
    }

    if (has_driver)
    {
        written = snprintf(out->driver_path, sizeof(out->driver_path), "%s/bus/hid/drivers/%s",
                           sysfs_root, driver);
        if (written < 0 || (size_t)written >= sizeof(out->driver_path))
        {
            return -ENAMETOOLONG;
        }
    }

    out->node_count = 0;
    return 0;
}

int hiding_find_devices(const char* sysfs_root, const char* id, struct hiding_device* out,
                        size_t max_devices, size_t* count)
{
    if (sysfs_root == NULL || out == NULL || count == NULL || max_devices == 0
        || !hiding_id_valid(id))
    {
        return -EINVAL;
    }

    *count = 0;

    char devices_path[HIDING_MAX_PATH];
    int written = snprintf(devices_path, sizeof(devices_path), "%s/bus/hid/devices", sysfs_root);
    if (written < 0 || (size_t)written >= sizeof(devices_path))
    {
        return -ENAMETOOLONG;
    }

    DIR* directory = opendir(devices_path);
    if (directory == NULL)
    {
        return -errno;
    }

    struct dirent* entry;
    while ((entry = readdir(directory)) != NULL)
    {
        if (strcmp(entry->d_name, ".") == 0 || strcmp(entry->d_name, "..") == 0)
        {
            continue;
        }

        if (*count >= max_devices)
        {
            break;
        }

        int result = fill_device(devices_path, sysfs_root, entry->d_name, id, &out[*count]);
        if (result < 0)
        {
            closedir(directory);
            return result;
        }

        if (result == 0)
        {
            (*count)++;
        }
    }

    closedir(directory);
    return *count > 0 ? 0 : -ENODEV;
}

int hiding_find_device(const char* sysfs_root, const char* id, struct hiding_device* out)
{
    size_t count = 0;
    int result = hiding_find_devices(sysfs_root, id, out, 1, &count);
    if (result != 0)
    {
        return result;
    }

    return count == 1 ? 0 : -ENODEV;
}

static int add_node(struct hiding_device* device, const char* dev_path, const char* sysfs_path,
                    enum hiding_node_kind kind)
{
    if (device->node_count >= HIDING_MAX_NODES)
    {
        return -ENOSPC;
    }

    struct hiding_node* node = &device->nodes[device->node_count];
    memset(node, 0, sizeof(*node));

    if (copy_string(node->dev_path, sizeof(node->dev_path), dev_path) != 0
        || copy_string(node->sysfs_path, sizeof(node->sysfs_path), sysfs_path) != 0)
    {
        return -ENAMETOOLONG;
    }

    struct stat status;
    if (stat(dev_path, &status) == 0)
    {
        node->present = true;
        node->rdev = status.st_rdev;
        node->has_original = true;
        node->original_uid = status.st_uid;
        node->original_gid = status.st_gid;
        node->original_mode = status.st_mode & 07777;
    }

    node->kind = kind;
    device->node_count++;
    return 0;
}

static int collect_hidraw(struct hiding_device* device, const char* dev_root)
{
    char directory_path[HIDING_MAX_PATH];
    int written = snprintf(directory_path, sizeof(directory_path), "%s/hidraw", device->sysfs_path);
    if (written < 0 || (size_t)written >= sizeof(directory_path))
    {
        return -ENAMETOOLONG;
    }

    DIR* directory = opendir(directory_path);
    if (directory == NULL)
    {
        return errno == ENOENT ? 0 : -errno;
    }

    int result = 0;
    struct dirent* entry;
    while ((entry = readdir(directory)) != NULL)
    {
        if (strncmp(entry->d_name, "hidraw", 6) != 0)
        {
            continue;
        }

        char sysfs_path[HIDING_MAX_PATH];
        char dev_path[HIDING_MAX_PATH];
        written = snprintf(sysfs_path, sizeof(sysfs_path), "%s/%s", directory_path, entry->d_name);
        if (written < 0 || (size_t)written >= sizeof(sysfs_path))
        {
            result = -ENAMETOOLONG;
            break;
        }

        written = snprintf(dev_path, sizeof(dev_path), "%s/%s", dev_root, entry->d_name);
        if (written < 0 || (size_t)written >= sizeof(dev_path))
        {
            result = -ENAMETOOLONG;
            break;
        }

        result = add_node(device, dev_path, sysfs_path, HIDING_NODE_HIDRAW);
        if (result != 0)
        {
            break;
        }
    }

    closedir(directory);
    return result;
}

static int collect_input(struct hiding_device* device, const char* dev_root)
{
    char input_path[HIDING_MAX_PATH];
    int written = snprintf(input_path, sizeof(input_path), "%s/input", device->sysfs_path);
    if (written < 0 || (size_t)written >= sizeof(input_path))
    {
        return -ENAMETOOLONG;
    }

    DIR* input_directory = opendir(input_path);
    if (input_directory == NULL)
    {
        return errno == ENOENT ? 0 : -errno;
    }

    int result = 0;
    struct dirent* input_entry;
    while ((input_entry = readdir(input_directory)) != NULL)
    {
        if (strncmp(input_entry->d_name, "input", 5) != 0)
        {
            continue;
        }

        char child_path[HIDING_MAX_PATH];
        written = snprintf(child_path, sizeof(child_path), "%s/%s", input_path, input_entry->d_name);
        if (written < 0 || (size_t)written >= sizeof(child_path))
        {
            result = -ENAMETOOLONG;
            break;
        }

        DIR* child_directory = opendir(child_path);
        if (child_directory == NULL)
        {
            continue;
        }

        struct dirent* child_entry;
        while ((child_entry = readdir(child_directory)) != NULL)
        {
            enum hiding_node_kind kind;
            if (strncmp(child_entry->d_name, "event", 5) == 0)
            {
                kind = HIDING_NODE_EVDEV;
            }
            else if (strncmp(child_entry->d_name, "js", 2) == 0
                || strncmp(child_entry->d_name, "mouse", 5) == 0)
            {
                kind = HIDING_NODE_OTHER;
            }
            else
            {
                continue;
            }

            char sysfs_path[HIDING_MAX_PATH];
            char dev_path[HIDING_MAX_PATH];
            written = snprintf(sysfs_path, sizeof(sysfs_path), "%s/%s", child_path, child_entry->d_name);
            if (written < 0 || (size_t)written >= sizeof(sysfs_path))
            {
                result = -ENAMETOOLONG;
                break;
            }

            written = snprintf(dev_path, sizeof(dev_path), "%s/input/%s", dev_root, child_entry->d_name);
            if (written < 0 || (size_t)written >= sizeof(dev_path))
            {
                result = -ENAMETOOLONG;
                break;
            }

            result = add_node(device, dev_path, sysfs_path, kind);
            if (result != 0)
            {
                break;
            }
        }

        closedir(child_directory);
        if (result != 0)
        {
            break;
        }
    }

    closedir(input_directory);
    return result;
}

int hiding_collect_nodes(const char* dev_root, const char* sysfs_root, struct hiding_device* device)
{
    if (dev_root == NULL || device == NULL)
    {
        return -EINVAL;
    }

    (void)sysfs_root;
    device->node_count = 0;

    int result = collect_hidraw(device, dev_root);
    if (result != 0)
    {
        return result;
    }

    return collect_input(device, dev_root);
}