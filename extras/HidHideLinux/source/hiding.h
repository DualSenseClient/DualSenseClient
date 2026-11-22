#ifndef DUAL_SENSE_HIDING_H
#define DUAL_SENSE_HIDING_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <sys/types.h>

#define HIDING_MAX_ID 128
#define HIDING_MAX_PATH 4096
#define HIDING_MAX_NODES 32
#define HIDING_MAX_ACL 512
#define HIDING_MAX_DEVICES 4
#define HIDING_SNAPSHOT_MAGIC 0x48494445u
#define HIDING_SNAPSHOT_VERSION 2u

enum hiding_node_kind
{
    HIDING_NODE_HIDRAW,
    HIDING_NODE_EVDEV,
    HIDING_NODE_OTHER
};

struct hiding_node
{
    char dev_path[HIDING_MAX_PATH];
    char sysfs_path[HIDING_MAX_PATH];
    dev_t rdev;
    bool present;
    bool has_original;
    bool has_acl;
    uid_t original_uid;
    gid_t original_gid;
    mode_t original_mode;
    size_t acl_size;
    unsigned char acl[HIDING_MAX_ACL];
    enum hiding_node_kind kind;
};

struct hiding_device
{
    char id[HIDING_MAX_ID];
    unsigned int bus;
    char sysfs_path[HIDING_MAX_PATH];
    char device_name[HIDING_MAX_ID];
    char driver_path[HIDING_MAX_PATH];
    struct hiding_node nodes[HIDING_MAX_NODES];
    size_t node_count;
};

struct hiding_snapshot
{
    uint32_t magic;
    uint32_t version;
    size_t device_count;
    struct hiding_device devices[HIDING_MAX_DEVICES];
};

bool hiding_id_valid(const char* id);
bool hiding_parse_uint(const char* text, int base, unsigned long max, unsigned long* value,
                       const char** end);
bool hiding_parse_hid_id(const char* text, unsigned int* bus, unsigned int* vendor,
                         unsigned int* product);
bool hiding_parse_uids(const char* text, unsigned int* real, unsigned int* effective);
bool hiding_parse_hello(const char* line, int* protocol, char* version, size_t version_size);
bool hiding_parse_status(const char* line, bool* hidden);
bool hiding_parse_command(const char* line, char* error, size_t error_size);
int hiding_write_all(int fd, const char* data, size_t length);

int hiding_find_device(const char* sysfs_root, const char* id, struct hiding_device* out);
int hiding_find_devices(const char* sysfs_root, const char* id, struct hiding_device* out,
                        size_t max_devices, size_t* count);
int hiding_collect_nodes(const char* dev_root, const char* sysfs_root, struct hiding_device* dev);
int hiding_capture_acls(struct hiding_device* dev);

int hiding_marker_set(const char* run_dir, const char* id, const struct hiding_snapshot* snapshot);
int hiding_marker_load(const char* run_dir, const char* id, struct hiding_snapshot* snapshot);
int hiding_marker_clear(const char* run_dir, const char* id);
bool hiding_marker_exists(const char* run_dir, const char* id);

int hiding_set_permissions(const struct hiding_device* dev, const char* group_name);
int hiding_restore_permissions(const struct hiding_device* snapshot, const struct hiding_device* current);
int hiding_trigger_udev(const struct hiding_device* dev);
int hiding_revoke_handles(const struct hiding_device* dev, pid_t allowed_pid,
                          uid_t allowed_uid, bool* needs_rebind);
int hiding_rebind_driver(const struct hiding_device* dev);

#endif