#include "hiding.h"
#include "hiding_markers.h"

#include <dirent.h>
#include <errno.h>
#include <grp.h>
#include <limits.h>
#include <poll.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/time.h>
#include <sys/un.h>
#include <time.h>
#include <unistd.h>

#define HIDING_HELPER_VERSION "0.1.0"
#define HIDING_GROUP "dualsenseclient"
#define HIDING_MAX_CLIENTS 8
#define HIDING_MAX_CLIENT_IDS 8
#define HIDING_MAX_REFS 32
#define HIDING_READ_BUFFER 4096
#define HIDING_GRACE_SECONDS 5

struct client_state
{
    int fd;
    pid_t pid;
    uid_t uid;
    char buffer[HIDING_READ_BUFFER];
    size_t length;
    char hidden[HIDING_MAX_CLIENT_IDS][HIDING_MAX_ID];
    size_t hidden_count;
};

struct hidden_ref
{
    char id[HIDING_MAX_ID];
    int count;
    struct hiding_snapshot snapshot;
};

static struct client_state clients[HIDING_MAX_CLIENTS];
static struct hidden_ref references[HIDING_MAX_REFS];
static struct hiding_device scratch_devices[HIDING_MAX_DEVICES];
static struct hiding_snapshot stale_snapshot;
static size_t reference_count;
static volatile sig_atomic_t stop_requested;
static const char* run_dir;
static const char* sysfs_root;
static const char* dev_root;
static char direct_socket_path[HIDING_MAX_PATH];
static bool direct_socket_bound;
static time_t last_client_time;

static struct hidden_ref* find_reference(const char* id);

static void handle_signal(int signal_number)
{
    (void)signal_number;
    stop_requested = 1;
}

static bool send_all(int fd, const char* data, size_t length)
{
    return hiding_write_all(fd, data, length) == 0;
}

static bool send_line(int fd, const char* line)
{
    return send_all(fd, line, strlen(line)) && send_all(fd, "\n", 1);
}

static bool send_error(struct client_state* client, const char* code, const char* message)
{
    char response[512];
    int written = snprintf(response, sizeof(response), "ERR %s %s", code, message);
    if (written < 0 || (size_t)written >= sizeof(response))
    {
        return send_line(client->fd, "ERR EIO response too long");
    }

    return send_line(client->fd, response);
}

static bool client_has_hidden(const struct client_state* client, const char* id)
{
    for (size_t i = 0; i < client->hidden_count; i++)
    {
        if (strcmp(client->hidden[i], id) == 0)
        {
            return true;
        }
    }

    return false;
}

static int track_client_hidden(struct client_state* client, const char* id)
{
    if (client_has_hidden(client, id))
    {
        return 0;
    }

    if (client->hidden_count >= HIDING_MAX_CLIENT_IDS)
    {
        return -ENOSPC;
    }

    memcpy(client->hidden[client->hidden_count], id, strlen(id) + 1);
    client->hidden_count++;
    return 0;
}

static bool untrack_client_hidden(struct client_state* client, const char* id)
{
    size_t client_index = 0;
    for (; client_index < client->hidden_count; client_index++)
    {
        if (strcmp(client->hidden[client_index], id) == 0)
        {
            break;
        }
    }

    if (client_index == client->hidden_count)
    {
        return false;
    }

    for (size_t i = client_index + 1; i < client->hidden_count; i++)
    {
        memcpy(client->hidden[i - 1], client->hidden[i], strlen(client->hidden[i]) + 1);
    }

    client->hidden_count--;
    return true;
}

static struct hidden_ref* find_reference(const char* id)
{
    for (size_t i = 0; i < reference_count; i++)
    {
        if (strcmp(references[i].id, id) == 0)
        {
            return &references[i];
        }
    }

    return NULL;
}

static struct hidden_ref* create_reference(const char* id)
{
    if (reference_count >= HIDING_MAX_REFS)
    {
        return NULL;
    }

    struct hidden_ref* reference = &references[reference_count++];
    memset(reference, 0, sizeof(*reference));
    memcpy(reference->id, id, strlen(id) + 1);
    reference->snapshot.magic = HIDING_SNAPSHOT_MAGIC;
    reference->snapshot.version = HIDING_SNAPSHOT_VERSION;
    return reference;
}

static void drop_reference(struct hidden_ref* reference)
{
    for (size_t i = 0; i < reference_count; i++)
    {
        if (&references[i] != reference)
        {
            continue;
        }

        for (size_t j = i + 1; j < reference_count; j++)
        {
            references[j - 1] = references[j];
        }

        reference_count--;
        return;
    }
}

static const struct hiding_device* find_snapshot_device(const struct hiding_snapshot* snapshot,
                                                        const struct hiding_device* current)
{
    if (snapshot == NULL)
    {
        return NULL;
    }

    for (size_t i = 0; i < snapshot->device_count; i++)
    {
        if (strcmp(snapshot->devices[i].device_name, current->device_name) == 0)
        {
            return &snapshot->devices[i];
        }
    }

    for (size_t i = 0; i < snapshot->device_count; i++)
    {
        for (size_t j = 0; j < snapshot->devices[i].node_count; j++)
        {
            const struct hiding_node* saved = &snapshot->devices[i].nodes[j];
            if (!saved->has_original)
            {
                continue;
            }

            for (size_t k = 0; k < current->node_count; k++)
            {
                const struct hiding_node* node = &current->nodes[k];
                if (node->present && node->rdev == saved->rdev && node->kind == saved->kind)
                {
                    return &snapshot->devices[i];
                }
            }
        }
    }

    const struct hiding_device* bus_match = NULL;
    size_t bus_matches = 0;
    for (size_t i = 0; i < snapshot->device_count; i++)
    {
        if (snapshot->devices[i].bus == current->bus)
        {
            bus_match = &snapshot->devices[i];
            bus_matches++;
        }
    }

    if (bus_matches == 1)
    {
        return bus_match;
    }

    if (snapshot->device_count == 1)
    {
        return &snapshot->devices[0];
    }

    return NULL;
}

static void restore_hidden_device(const char* id, const struct hiding_snapshot* snapshot)
{
    size_t device_count = 0;
    int result = hiding_find_devices(sysfs_root, id, scratch_devices, HIDING_MAX_DEVICES,
                                     &device_count);
    if (result == -ENODEV)
    {
        hiding_marker_clear(run_dir, id);
        return;
    }

    if (result != 0)
    {
        fprintf(stderr, "dualsense-hiding-helper: could not find %s for restore: %s\n", id,
                strerror(-result));
        return;
    }

    bool failed = false;
    for (size_t i = 0; i < device_count; i++)
    {
        if (hiding_collect_nodes(dev_root, sysfs_root, &scratch_devices[i]) != 0)
        {
            fprintf(stderr, "dualsense-hiding-helper: could not enumerate %s nodes for restore\n",
                    id);
            failed = true;
            continue;
        }

        const struct hiding_device* saved = find_snapshot_device(snapshot, &scratch_devices[i]);
        int restore_result = hiding_restore_permissions(saved, &scratch_devices[i]);
        if (restore_result != 0)
        {
            fprintf(stderr, "dualsense-hiding-helper: could not restore %s: %s\n", id,
                    strerror(-restore_result));
            failed = true;
        }
    }

    if (failed)
    {
        return;
    }

    // Clear the marker before triggering udev; otherwise the hiding rule sees it
    // and re-applies the hidden group.
    if (hiding_marker_clear(run_dir, id) != 0)
    {
        fprintf(stderr, "dualsense-hiding-helper: could not clear marker for %s\n", id);
        return;
    }

    for (size_t i = 0; i < device_count; i++)
    {
        if (hiding_trigger_udev(&scratch_devices[i]) != 0)
        {
            fprintf(stderr, "dualsense-hiding-helper: could not trigger udev for %s after restore\n",
                    id);
        }
    }
}

static void remove_client_hidden(struct client_state* client, const char* id)
{
    if (!untrack_client_hidden(client, id))
    {
        // Another client owns this hide. Never let one client clear another's marker.
        return;
    }

    struct hidden_ref* reference = find_reference(id);
    if (reference == NULL)
    {
        return;
    }

    reference->count--;
    if (reference->count > 0)
    {
        return;
    }

    restore_hidden_device(id, &reference->snapshot);
    drop_reference(reference);
}

static void restore_orphan_markers(void)
{
    char hidden_dir[HIDING_MAX_PATH];
    int written = snprintf(hidden_dir, sizeof(hidden_dir), "%s/hidden", run_dir);
    if (written < 0 || (size_t)written >= sizeof(hidden_dir))
    {
        return;
    }

    DIR* directory = opendir(hidden_dir);
    if (directory == NULL)
    {
        return;
    }

    struct dirent* entry;
    while ((entry = readdir(directory)) != NULL)
    {
        if (!hiding_id_valid(entry->d_name) || find_reference(entry->d_name) != NULL)
        {
            continue;
        }

        int loaded = hiding_marker_load(run_dir, entry->d_name, &stale_snapshot);
        restore_hidden_device(entry->d_name, loaded == 0 ? &stale_snapshot : NULL);
    }

    closedir(directory);
}

static void clear_all_hides(void)
{
    for (size_t i = 0; i < reference_count; i++)
    {
        restore_hidden_device(references[i].id, &references[i].snapshot);
    }

    restore_orphan_markers();
    reference_count = 0;
}

static void disconnect_client(size_t index)
{
    struct client_state* client = &clients[index];
    if (client->fd < 0)
    {
        return;
    }

    while (client->hidden_count > 0)
    {
        remove_client_hidden(client, client->hidden[0]);
    }

    close(client->fd);
    memset(client, 0, sizeof(*client));
    client->fd = -1;
    last_client_time = time(NULL);
}

static size_t client_count(void)
{
    size_t count = 0;
    for (size_t i = 0; i < HIDING_MAX_CLIENTS; i++)
    {
        if (clients[i].fd >= 0)
        {
            count++;
        }
    }

    return count;
}

static bool list_markers(int fd)
{
    char hidden_dir[HIDING_MAX_PATH];
    int written = snprintf(hidden_dir, sizeof(hidden_dir), "%s/hidden", run_dir);
    if (written < 0 || (size_t)written >= sizeof(hidden_dir))
    {
        return send_line(fd, "ERR ENAMETOOLONG marker path");
    }

    DIR* directory = opendir(hidden_dir);
    if (directory != NULL)
    {
        struct dirent* entry;
        while ((entry = readdir(directory)) != NULL)
        {
            if (!hiding_id_valid(entry->d_name))
            {
                continue;
            }

            char response[HIDING_MAX_ID + 16];
            written = snprintf(response, sizeof(response), "HIDDEN %s", entry->d_name);
            if (written < 0 || (size_t)written >= sizeof(response) || !send_line(fd, response))
            {
                closedir(directory);
                return false;
            }
        }

        closedir(directory);
    }

    return send_line(fd, "END");
}

static bool handle_line(struct client_state* client, const char* line)
{
    if (line[0] == '\0')
    {
        return true;
    }

    if (strcmp(line, "HELLO 1") == 0)
    {
        char response[128];
        int written = snprintf(response, sizeof(response), "OK 1 %s revoke=hidraw,evdev fallback=rebind",
                               HIDING_HELPER_VERSION);
        return written > 0 && (size_t)written < sizeof(response) && send_line(client->fd, response);
    }

    if (strncmp(line, "HELLO", 5) == 0)
    {
        return send_error(client, "EPROTO", "unsupported protocol");
    }

    if (strncmp(line, "STATUS ", 7) == 0)
    {
        const char* id = line + 7;
        if (!hiding_id_valid(id))
        {
            return send_error(client, "EINVAL", "invalid controller id");
        }

        struct hiding_device device;
        if (hiding_find_device(sysfs_root, id, &device) != 0)
        {
            return send_error(client, "ENODEV", "controller not found");
        }

        return send_line(client->fd, hiding_marker_exists(run_dir, id) ? "HIDDEN" : "VISIBLE");
    }

    if (strncmp(line, "HIDE ", 5) == 0)
    {
        const char* id = line + 5;
        if (!hiding_id_valid(id))
        {
            return send_error(client, "EINVAL", "invalid controller id");
        }

        if (client_has_hidden(client, id))
        {
            return send_line(client->fd, "OK");
        }

        struct hidden_ref* reference = find_reference(id);
        if (reference != NULL)
        {
            if (track_client_hidden(client, id) != 0)
            {
                return send_error(client, "EIO", "could not track hide");
            }

            reference->count++;
            return send_line(client->fd, "OK");
        }

        if (hiding_marker_exists(run_dir, id))
        {
            // A marker without a reference is a failed startup restore. Do not
            // snapshot the already-hidden permissions as the new original state.
            restore_hidden_device(id, NULL);
            if (hiding_marker_exists(run_dir, id))
            {
                return send_error(client, "EIO", "stale hidden state could not be restored");
            }
        }

        reference = create_reference(id);
        if (reference == NULL)
        {
            return send_error(client, "ENOSPC", "too many hidden controllers");
        }

        size_t device_count = 0;
        int find_result = hiding_find_devices(sysfs_root, id, reference->snapshot.devices,
                                              HIDING_MAX_DEVICES, &device_count);
        if (find_result != 0)
        {
            drop_reference(reference);
            return send_error(client, "ENODEV", "controller not found");
        }

        reference->snapshot.device_count = device_count;

        for (size_t i = 0; i < device_count; i++)
        {
            if (hiding_collect_nodes(dev_root, sysfs_root, &reference->snapshot.devices[i]) != 0)
            {
                drop_reference(reference);
                return send_error(client, "ENODEV", "could not enumerate controller nodes");
            }

            if (hiding_capture_acls(&reference->snapshot.devices[i]) != 0)
            {
                drop_reference(reference);
                return send_error(client, "EIO", "could not capture controller permissions");
            }
        }

        if (track_client_hidden(client, id) != 0)
        {
            drop_reference(reference);
            return send_error(client, "EIO", "could not track hide");
        }

        if (hiding_marker_set(run_dir, id, &reference->snapshot) != 0)
        {
            untrack_client_hidden(client, id);
            drop_reference(reference);
            return send_error(client, "EIO", "could not write hide marker");
        }

        reference->count = 1;

        for (size_t i = 0; i < device_count; i++)
        {
            struct hiding_device* device = &reference->snapshot.devices[i];
            if (hiding_set_permissions(device, HIDING_GROUP) != 0)
            {
                remove_client_hidden(client, id);
                return send_error(client, "EACCES", "could not change controller permissions");
            }

            if (hiding_trigger_udev(device) != 0)
            {
                remove_client_hidden(client, id);
                return send_error(client, "EIO", "could not trigger udev");
            }

            // Udev may re-apply the seat ACL while processing the change event. Remove it
            // once more after the event so the hidden node stays group-only.
            if (hiding_set_permissions(device, HIDING_GROUP) != 0)
            {
                remove_client_hidden(client, id);
                return send_error(client, "EACCES", "could not finalize controller permissions");
            }

            bool needs_rebind = getenv("HIDING_FORCE_REBIND") != NULL;
            if (!needs_rebind
                && hiding_revoke_handles(device, client->pid, client->uid, &needs_rebind) != 0)
            {
                needs_rebind = true;
            }

            if (needs_rebind)
            {
                fprintf(stderr, "dualsense-hiding-helper: rebinding %s to revoke existing handles\n",
                        id);
                if (hiding_rebind_driver(device) != 0)
                {
                    remove_client_hidden(client, id);
                    return send_error(client, "EIO",
                                      "could not revoke existing controller handles");
                }
            }
        }

        fprintf(stderr, "dualsense-hiding-helper: hid %s\n", id);
        return send_line(client->fd, "OK");
    }

    if (strncmp(line, "UNHIDE ", 7) == 0)
    {
        const char* id = line + 7;
        if (!hiding_id_valid(id))
        {
            return send_error(client, "EINVAL", "invalid controller id");
        }

        remove_client_hidden(client, id);
        fprintf(stderr, "dualsense-hiding-helper: unhid %s\n", id);
        return send_line(client->fd, "OK");
    }

    if (strcmp(line, "LIST") == 0)
    {
        return list_markers(client->fd);
    }

    if (strcmp(line, "QUIT") == 0)
    {
        send_line(client->fd, "OK");
        return false;
    }

    return send_error(client, "EINVAL", "unknown command");
}

static bool process_client_input(size_t index)
{
    struct client_state* client = &clients[index];
    if (client->fd < 0)
    {
        return true;
    }

    ssize_t received = recv(client->fd, client->buffer + client->length,
                            sizeof(client->buffer) - client->length, 0);
    if (received == 0)
    {
        disconnect_client(index);
        return false;
    }

    if (received < 0)
    {
        if (errno == EINTR)
        {
            return true;
        }

        disconnect_client(index);
        return false;
    }

    client->length += (size_t)received;

    size_t start = 0;
    for (size_t i = 0; i < client->length; i++)
    {
        if (client->buffer[i] != '\n')
        {
            continue;
        }

        client->buffer[i] = '\0';
        if (i > 0 && client->buffer[i - 1] == '\r')
        {
            client->buffer[i - 1] = '\0';
        }

        bool keep = handle_line(client, client->buffer + start);
        start = i + 1;
        if (!keep)
        {
            disconnect_client(index);
            return false;
        }
    }

    if (start > 0)
    {
        memmove(client->buffer, client->buffer + start, client->length - start);
        client->length -= start;
    }

    if (client->length == sizeof(client->buffer))
    {
        send_error(client, "E2BIG", "request too long");
        disconnect_client(index);
        return false;
    }

    return true;
}

static void accept_client(int listener)
{
    for (size_t i = 0; i < HIDING_MAX_CLIENTS; i++)
    {
        if (clients[i].fd >= 0)
        {
            continue;
        }

        int fd = accept4(listener, NULL, NULL, SOCK_CLOEXEC);
        if (fd < 0)
        {
            return;
        }

        struct timeval send_timeout;
        send_timeout.tv_sec = 5;
        send_timeout.tv_usec = 0;
        if (setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &send_timeout, sizeof(send_timeout)) != 0)
        {
            perror("dualsense-hiding-helper: setsockopt(SO_SNDTIMEO)");
            close(fd);
            return;
        }

        memset(&clients[i], 0, sizeof(clients[i]));
        clients[i].fd = fd;

        struct ucred credentials;
        socklen_t credentials_length = sizeof(credentials);
        if (getsockopt(fd, SOL_SOCKET, SO_PEERCRED, &credentials, &credentials_length) != 0)
        {
            close(fd);
            clients[i].fd = -1;
            return;
        }

        clients[i].pid = credentials.pid;
        clients[i].uid = credentials.uid;
        last_client_time = time(NULL);
        return;
    }

    int rejected = accept4(listener, NULL, NULL, SOCK_CLOEXEC);
    if (rejected >= 0)
    {
        close(rejected);
    }
}

static int create_listener(void)
{
    const char* pid_text = getenv("LISTEN_PID");
    const char* fds_text = getenv("LISTEN_FDS");
    unsigned long listen_pid = 0;
    unsigned long listen_fds = 0;
    const char* end = NULL;
    if (pid_text != NULL && fds_text != NULL
        && hiding_parse_uint(pid_text, 10, INT_MAX, &listen_pid, &end) && *end == '\0'
        && hiding_parse_uint(fds_text, 10, INT_MAX, &listen_fds, &end) && *end == '\0'
        && listen_pid == (unsigned long)getpid() && listen_fds >= 1)
    {
        return 3;
    }

    const char* socket_path = getenv("HIDING_SOCKET");
    if (socket_path == NULL || socket_path[0] == '\0')
    {
        fprintf(stderr, "dualsense-hiding-helper: socket activation did not provide fd 3; "
                "set HIDING_SOCKET only when running the helper directly\n");
        errno = EINVAL;
        return -1;
    }

    int written = snprintf(direct_socket_path, sizeof(direct_socket_path), "%s", socket_path);
    if (written < 0 || (size_t)written >= sizeof(direct_socket_path))
    {
        errno = ENAMETOOLONG;
        return -1;
    }

    char parent[HIDING_MAX_PATH];
    written = snprintf(parent, sizeof(parent), "%s", direct_socket_path);
    if (written < 0 || (size_t)written >= sizeof(parent))
    {
        errno = ENAMETOOLONG;
        return -1;
    }

    char* slash = strrchr(parent, '/');
    if (slash != NULL)
    {
        *slash = '\0';
        if (parent[0] != '\0' && mkdir(parent, 0755) != 0 && errno != EEXIST)
        {
            return -1;
        }
    }

    unlink(direct_socket_path);

    int listener = socket(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0);
    if (listener < 0)
    {
        return -1;
    }

    struct sockaddr_un address;
    memset(&address, 0, sizeof(address));
    address.sun_family = AF_UNIX;
    written = snprintf(address.sun_path, sizeof(address.sun_path), "%s", direct_socket_path);
    if (written < 0 || (size_t)written >= sizeof(address.sun_path))
    {
        close(listener);
        errno = ENAMETOOLONG;
        return -1;
    }

    if (bind(listener, (struct sockaddr*)&address, sizeof(address)) != 0
        || listen(listener, 8) != 0)
    {
        close(listener);
        return -1;
    }

    if (chmod(direct_socket_path, 0660) != 0)
    {
        int error = errno;
        close(listener);
        unlink(direct_socket_path);
        errno = error;
        return -1;
    }

    if (geteuid() == 0)
    {
        struct group* group = getgrnam("dualsenseclient");
        if (group != NULL && chown(direct_socket_path, 0, group->gr_gid) != 0)
        {
            int error = errno;
            close(listener);
            unlink(direct_socket_path);
            errno = error;
            return -1;
        }
    }

    direct_socket_bound = true;
    return listener;
}

static void restore_stale_hides(void)
{
    char hidden_dir[HIDING_MAX_PATH];
    int written = snprintf(hidden_dir, sizeof(hidden_dir), "%s/hidden", run_dir);
    if (written < 0 || (size_t)written >= sizeof(hidden_dir))
    {
        return;
    }

    DIR* directory = opendir(hidden_dir);
    if (directory != NULL)
    {
        struct dirent* entry;
        while ((entry = readdir(directory)) != NULL)
        {
            if (!hiding_id_valid(entry->d_name))
            {
                continue;
            }

            int loaded = hiding_marker_load(run_dir, entry->d_name, &stale_snapshot);
            restore_hidden_device(entry->d_name, loaded == 0 ? &stale_snapshot : NULL);
        }

        closedir(directory);
    }
}

static void cleanup(int listener)
{
    for (size_t i = 0; i < HIDING_MAX_CLIENTS; i++)
    {
        if (clients[i].fd >= 0)
        {
            close(clients[i].fd);
            clients[i].fd = -1;
        }
    }

    clear_all_hides();

    if (listener >= 0)
    {
        close(listener);
    }

    if (direct_socket_bound)
    {
        unlink(direct_socket_path);
    }
}

int main(void)
{
    signal(SIGPIPE, SIG_IGN);
    signal(SIGTERM, handle_signal);
    signal(SIGINT, handle_signal);

    run_dir = getenv("HIDING_RUN_DIR");
    if (run_dir == NULL || run_dir[0] == '\0')
    {
        run_dir = "/run/dualsenseclient";
    }

    sysfs_root = getenv("HIDING_SYSFS_ROOT");
    if (sysfs_root == NULL || sysfs_root[0] == '\0')
    {
        sysfs_root = "/sys";
    }

    dev_root = getenv("HIDING_DEV_ROOT");
    if (dev_root == NULL || dev_root[0] == '\0')
    {
        dev_root = "/dev";
    }

    for (size_t i = 0; i < HIDING_MAX_CLIENTS; i++)
    {
        clients[i].fd = -1;
    }

    restore_stale_hides();

    int listener = create_listener();
    if (listener < 0)
    {
        fprintf(stderr, "dualsense-hiding-helper: could not create socket: %s\n", strerror(errno));
        return 1;
    }

    last_client_time = time(NULL);

    while (!stop_requested)
    {
        struct pollfd descriptors[HIDING_MAX_CLIENTS + 1];
        int client_map[HIDING_MAX_CLIENTS + 1];
        nfds_t descriptor_count = 1;
        descriptors[0].fd = listener;
        descriptors[0].events = POLLIN;
        descriptors[0].revents = 0;
        client_map[0] = -1;

        for (size_t i = 0; i < HIDING_MAX_CLIENTS; i++)
        {
            if (clients[i].fd < 0)
            {
                continue;
            }

            descriptors[descriptor_count].fd = clients[i].fd;
            descriptors[descriptor_count].events = POLLIN;
            descriptors[descriptor_count].revents = 0;
            client_map[descriptor_count] = (int)i;
            descriptor_count++;
        }

        int ready = poll(descriptors, descriptor_count, 250);
        if (ready < 0)
        {
            if (errno == EINTR)
            {
                continue;
            }

            break;
        }

        if ((descriptors[0].revents & POLLIN) != 0)
        {
            accept_client(listener);
        }

        for (nfds_t i = 1; i < descriptor_count; i++)
        {
            if (descriptors[i].revents == 0)
            {
                continue;
            }

            process_client_input((size_t)client_map[i]);
        }

        if (client_count() == 0 && time(NULL) - last_client_time >= HIDING_GRACE_SECONDS)
        {
            break;
        }
    }

    cleanup(listener);
    return 0;
}