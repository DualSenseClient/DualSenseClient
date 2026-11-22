#include "hiding.h"

#include <assert.h>
#include <errno.h>
#include <ftw.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/xattr.h>
#include <unistd.h>

static struct hiding_snapshot snapshot;
static struct hiding_snapshot loaded_snapshot;

struct test_acl_entry
{
    uint16_t tag;
    uint16_t permissions;
    uint32_t id;
} __attribute__((packed));

struct test_acl
{
    uint32_t version;
    struct test_acl_entry entries[5];
} __attribute__((packed));

static void make_dir(const char* path)
{
    if (mkdir(path, 0700) != 0 && errno != EEXIST)
    {
        perror(path);
        exit(1);
    }
}

static void make_path(const char* path)
{
    char buffer[HIDING_MAX_PATH];
    int written = snprintf(buffer, sizeof(buffer), "%s", path);
    assert(written > 0 && (size_t)written < sizeof(buffer));

    for (char* cursor = buffer + 1; *cursor != '\0'; cursor++)
    {
        if (*cursor == '/')
        {
            *cursor = '\0';
            make_dir(buffer);
            *cursor = '/';
        }
    }

    make_dir(buffer);
}

static void write_file(const char* path, const char* content)
{
    FILE* file = fopen(path, "w");
    assert(file != NULL);
    fputs(content, file);
    fclose(file);
}

static int remove_entry(const char* path, const struct stat* status, int flag, struct FTW* ftw)
{
    (void)status;
    (void)flag;
    (void)ftw;
    return remove(path);
}

static void remove_tree(const char* path)
{
    nftw(path, remove_entry, 32, FTW_DEPTH | FTW_PHYS);
}

static bool has_node(const struct hiding_device* device, const char* path)
{
    for (size_t i = 0; i < device->node_count; i++)
    {
        if (strcmp(device->nodes[i].dev_path, path) == 0)
        {
            return true;
        }
    }

    return false;
}

int main(void)
{
    assert(hiding_id_valid("d0:bc:c1:17:55:63"));
    assert(hiding_id_valid("abc_DEF-1.2"));
    assert(!hiding_id_valid(""));
    assert(!hiding_id_valid("."));
    assert(!hiding_id_valid(".."));
    assert(!hiding_id_valid("has space"));
    assert(!hiding_id_valid("a/b"));

    unsigned long parsed = 0;
    const char* end = NULL;
    assert(hiding_parse_uint("123", 10, 1000, &parsed, &end));
    assert(parsed == 123);
    assert(*end == '\0');
    assert(hiding_parse_uint("ff", 16, 255, &parsed, &end));
    assert(parsed == 255);
    assert(hiding_parse_uint("10x", 10, 1000, &parsed, &end));
    assert(parsed == 10);
    assert(*end == 'x');
    assert(!hiding_parse_uint("", 10, 1000, &parsed, &end));
    assert(!hiding_parse_uint("100", 10, 99, &parsed, &end));
    assert(hiding_parse_uint("fg", 16, 1000, &parsed, &end));
    assert(parsed == 15);
    assert(*end == 'g');
    assert(!hiding_parse_uint("gg", 16, 1000, &parsed, &end));

    unsigned int bus = 0;
    unsigned int vendor = 0;
    unsigned int product = 0;
    assert(hiding_parse_hid_id("0003:054C:0CE6", &bus, &vendor, &product));
    assert(bus == 0x0003);
    assert(vendor == 0x054C);
    assert(product == 0x0CE6);
    assert(!hiding_parse_hid_id("0003:054C", &bus, &vendor, &product));
    assert(!hiding_parse_hid_id("0003:054C:0CE6.0001", &bus, &vendor, &product));
    assert(!hiding_parse_hid_id("0003:054C:", &bus, &vendor, &product));

    unsigned int real = 0;
    unsigned int effective = 0;
    assert(hiding_parse_uids("\t1000\t1001\t1002\t1003", &real, &effective));
    assert(real == 1000);
    assert(effective == 1001);
    assert(!hiding_parse_uids("1000", &real, &effective));
    assert(!hiding_parse_uids("1000 x", &real, &effective));

    int protocol = 0;
    char version[32];
    assert(hiding_parse_hello("OK 1 1.0.0 revoke=hidraw", &protocol, version, sizeof(version)));
    assert(protocol == 1);
    assert(strcmp(version, "1.0.0") == 0);
    assert(!hiding_parse_hello("ERR EINVAL bad", &protocol, version, sizeof(version)));
    assert(!hiding_parse_hello("OK one 1.0.0", &protocol, version, sizeof(version)));

    bool hidden = false;
    assert(hiding_parse_status("HIDDEN", &hidden));
    assert(hidden);
    assert(hiding_parse_status("VISIBLE", &hidden));
    assert(!hidden);
    assert(!hiding_parse_status("OK", &hidden));

    char error[64];
    assert(hiding_parse_command("OK", error, sizeof(error)));
    assert(error[0] == '\0');
    assert(!hiding_parse_command("ERR EACCES denied", error, sizeof(error)));
    assert(strstr(error, "EACCES") != NULL);

    char temp[] = "/tmp/dualsense-hiding-native-XXXXXX";
    char* root = mkdtemp(temp);
    assert(root != NULL);

    char devices[HIDING_MAX_PATH];
    char first[HIDING_MAX_PATH];
    char second[HIDING_MAX_PATH];
    char third[HIDING_MAX_PATH];
    char wrong[HIDING_MAX_PATH];
    snprintf(devices, sizeof(devices), "%s/bus/hid/devices", root);
    snprintf(first, sizeof(first), "%s/0003:054C:0CE6.0001", devices);
    snprintf(second, sizeof(second), "%s/0003:054C:0CE6.0002", devices);
    snprintf(third, sizeof(third), "%s/0003:054C:0CE6.0003", devices);
    snprintf(wrong, sizeof(wrong), "%s/0003:00001234:00005678", devices);
    make_path(first);
    make_path(second);
    make_path(third);
    make_path(wrong);

    char uevent[HIDING_MAX_PATH];
    snprintf(uevent, sizeof(uevent), "%s/uevent", first);
    write_file(uevent,
               "HID_ID=0003:0000054C:00000CE6\n"
               "HID_UNIQ=d0:bc:c1:17:55:63\n"
               "DRIVER=playstation\n");
    snprintf(uevent, sizeof(uevent), "%s/uevent", second);
    write_file(uevent,
               "HID_ID=0003:0000054C:00000CE6\n"
               "HID_UNIQ=11:22:33:44:55:66\n"
               "DRIVER=playstation\n");
    snprintf(uevent, sizeof(uevent), "%s/uevent", third);
    write_file(uevent,
               "HID_ID=0003:0000054C:00000CE6\n"
               "HID_UNIQ=d0:bc:c1:17:55:63\n"
               "DRIVER=playstation\n");
    snprintf(uevent, sizeof(uevent), "%s/uevent", wrong);
    write_file(uevent,
               "HID_ID=0003:00001234:00005678\n"
               "HID_UNIQ=bad:device\n"
               "DRIVER=other\n");

    struct hiding_device device;
    assert(hiding_find_device(root, "d0:bc:c1:17:55:63", &device) == 0);
    assert(strcmp(device.id, "d0:bc:c1:17:55:63") == 0);
    assert(device.device_name[0] != '\0');
    assert(strstr(device.driver_path, "playstation") != NULL);

    struct hiding_device other;
    assert(hiding_find_device(root, "11:22:33:44:55:66", &other) == 0);
    assert(strcmp(other.id, "11:22:33:44:55:66") == 0);
    assert(strstr(other.device_name, "0002") != NULL);

    assert(hiding_find_device(root, "bad:device", &device) == -ENODEV);
    assert(hiding_find_device(root, "not:found", &device) == -ENODEV);
    assert(hiding_find_device(root, "bad id", &device) == -EINVAL);
    assert(hiding_find_device(root, "..", &device) == -EINVAL);

    assert(hiding_find_device(root, "d0:bc:c1:17:55:63", &device) == 0);

    struct hiding_device matches[HIDING_MAX_DEVICES];
    size_t match_count = 0;
    assert(hiding_find_devices(root, "d0:bc:c1:17:55:63", matches, HIDING_MAX_DEVICES,
                               &match_count) == 0);
    assert(match_count == 2);
    bool saw_first = false;
    bool saw_third = false;
    for (size_t i = 0; i < match_count; i++)
    {
        if (strstr(matches[i].device_name, "0001") != NULL)
        {
            saw_first = true;
        }
        if (strstr(matches[i].device_name, "0003") != NULL)
        {
            saw_third = true;
        }
    }
    assert(saw_first && saw_third);

    char hidraw[HIDING_MAX_PATH];
    char input[HIDING_MAX_PATH];
    char event[HIDING_MAX_PATH];
    char js[HIDING_MAX_PATH];
    char mouse[HIDING_MAX_PATH];
    snprintf(hidraw, sizeof(hidraw), "%s/hidraw/hidraw2", device.sysfs_path);
    snprintf(input, sizeof(input), "%s/input/input12", device.sysfs_path);
    snprintf(event, sizeof(event), "%s/event8", input);
    snprintf(js, sizeof(js), "%s/js0", input);
    snprintf(mouse, sizeof(mouse), "%s/mouse2", input);
    make_path(hidraw);
    make_path(event);
    make_path(js);
    make_path(mouse);

    char dev_root[HIDING_MAX_PATH];
    char dev_hidraw[HIDING_MAX_PATH];
    char dev_input[HIDING_MAX_PATH];
    char dev_event[HIDING_MAX_PATH];
    char dev_js[HIDING_MAX_PATH];
    char dev_mouse[HIDING_MAX_PATH];
    snprintf(dev_root, sizeof(dev_root), "%s/dev", root);
    snprintf(dev_hidraw, sizeof(dev_hidraw), "%s/hidraw2", dev_root);
    snprintf(dev_input, sizeof(dev_input), "%s/input", dev_root);
    snprintf(dev_event, sizeof(dev_event), "%s/event8", dev_input);
    snprintf(dev_js, sizeof(dev_js), "%s/js0", dev_input);
    snprintf(dev_mouse, sizeof(dev_mouse), "%s/mouse2", dev_input);
    make_path(dev_input);
    write_file(dev_hidraw, "");
    write_file(dev_event, "");
    write_file(dev_js, "");
    write_file(dev_mouse, "");

    assert(hiding_collect_nodes(dev_root, root, &device) == 0);
    assert(device.node_count == 4);
    assert(has_node(&device, dev_hidraw));
    assert(has_node(&device, dev_event));
    assert(has_node(&device, dev_js));
    assert(has_node(&device, dev_mouse));

    struct test_acl acl;
    memset(&acl, 0, sizeof(acl));
    acl.version = 0x0002;
    acl.entries[0].tag = 0x01;
    acl.entries[0].permissions = 6;
    acl.entries[0].id = 0xFFFFFFFFu;
    acl.entries[1].tag = 0x02;
    acl.entries[1].permissions = 4;
    acl.entries[1].id = 1234;
    acl.entries[2].tag = 0x04;
    acl.entries[2].permissions = 4;
    acl.entries[2].id = 0xFFFFFFFFu;
    acl.entries[3].tag = 0x10;
    acl.entries[3].permissions = 4;
    acl.entries[3].id = 0xFFFFFFFFu;
    acl.entries[4].tag = 0x20;
    acl.entries[4].permissions = 0;
    acl.entries[4].id = 0xFFFFFFFFu;

    bool acl_supported = setxattr(dev_hidraw, "system.posix_acl_access", &acl, sizeof(acl), 0) == 0;
    if (acl_supported)
    {
        assert(hiding_capture_acls(&device) == 0);
        bool found_acl = false;
        for (size_t i = 0; i < device.node_count; i++)
        {
            if (strcmp(device.nodes[i].dev_path, dev_hidraw) == 0)
            {
                assert(device.nodes[i].has_acl);
                assert(device.nodes[i].acl_size == sizeof(acl));
                assert(memcmp(device.nodes[i].acl, &acl, sizeof(acl)) == 0);
                found_acl = true;
            }
        }
        assert(found_acl);

        assert(removexattr(dev_hidraw, "system.posix_acl_access") == 0);
        assert(hiding_restore_permissions(&device, &device) == 0);
        unsigned char restored[sizeof(acl)];
        ssize_t restored_size = getxattr(dev_hidraw, "system.posix_acl_access", restored,
                                         sizeof(restored));
        assert(restored_size == (ssize_t)sizeof(acl));
        assert(memcmp(restored, &acl, sizeof(acl)) == 0);
    }

    char run_dir[HIDING_MAX_PATH];
    snprintf(run_dir, sizeof(run_dir), "%s/run", root);

    snapshot.magic = HIDING_SNAPSHOT_MAGIC;
    snapshot.version = HIDING_SNAPSHOT_VERSION;
    snapshot.device_count = 1;
    snprintf(snapshot.devices[0].device_name, sizeof(snapshot.devices[0].device_name),
             "0003:054C:0CE6.0001");
    snapshot.devices[0].node_count = 1;
    snapshot.devices[0].nodes[0].has_original = true;
    snapshot.devices[0].nodes[0].original_uid = getuid();
    snapshot.devices[0].nodes[0].original_gid = getgid();
    snapshot.devices[0].nodes[0].original_mode = 0660;

    assert(hiding_marker_set(run_dir, "d0:bc:c1:17:55:63", &snapshot) == 0);
    assert(hiding_marker_exists(run_dir, "d0:bc:c1:17:55:63"));
    assert(hiding_marker_load(run_dir, "d0:bc:c1:17:55:63", &loaded_snapshot) == 0);
    assert(loaded_snapshot.magic == HIDING_SNAPSHOT_MAGIC);
    assert(loaded_snapshot.version == HIDING_SNAPSHOT_VERSION);
    assert(loaded_snapshot.device_count == 1);
    assert(strcmp(loaded_snapshot.devices[0].device_name, "0003:054C:0CE6.0001") == 0);
    assert(hiding_marker_clear(run_dir, "d0:bc:c1:17:55:63") == 0);
    assert(!hiding_marker_exists(run_dir, "d0:bc:c1:17:55:63"));

    char legacy_marker[HIDING_MAX_PATH];
    snprintf(legacy_marker, sizeof(legacy_marker), "%s/hidden/11:22:33:44:55:66", run_dir);
    write_file(legacy_marker, "");
    assert(hiding_marker_load(run_dir, "11:22:33:44:55:66", &loaded_snapshot) == -ENODATA);
    assert(hiding_marker_clear(run_dir, "11:22:33:44:55:66") == 0);

    remove_tree(root);
    return 0;
}