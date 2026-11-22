#include <errno.h>
#include <fcntl.h>
#include <stdbool.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#ifndef HIDING_LAUNCH_CONFIG
#define HIDING_LAUNCH_CONFIG "/etc/dualsenseclient/app-path"
#endif

#define TARGET_MAX 4096

static bool config_is_trusted(int fd)
{
    struct stat status;
    if (fstat(fd, &status) != 0 || !S_ISREG(status.st_mode))
    {
        fprintf(stderr, "launch: %s is not a regular file\n", HIDING_LAUNCH_CONFIG);
        return false;
    }

#ifndef HIDING_LAUNCH_TEST
    if (status.st_uid != 0 || (status.st_mode & (S_IWGRP | S_IWOTH)) != 0)
    {
        fprintf(stderr, "launch: %s must be root-owned and not group/other writable\n",
                HIDING_LAUNCH_CONFIG);
        return false;
    }
#else
    (void)status;
#endif

    return true;
}

static bool read_target(char* target, size_t size)
{
    int fd = open(HIDING_LAUNCH_CONFIG, O_RDONLY | O_CLOEXEC | O_NOFOLLOW);
    if (fd < 0)
    {
        fprintf(stderr, "launch: cannot open %s: %s; rerun the hiding installer with --app-path\n",
                HIDING_LAUNCH_CONFIG, strerror(errno));
        return false;
    }

    if (!config_is_trusted(fd))
    {
        close(fd);
        return false;
    }

    FILE* config = fdopen(fd, "r");
    if (config == NULL)
    {
        fprintf(stderr, "launch: cannot read %s: %s\n", HIDING_LAUNCH_CONFIG, strerror(errno));
        close(fd);
        return false;
    }

    if (fgets(target, (int)size, config) == NULL)
    {
        fclose(config);
        fprintf(stderr, "launch: %s is empty; rerun the hiding installer with --app-path\n",
                HIDING_LAUNCH_CONFIG);
        return false;
    }

    fclose(config);

    size_t length = strlen(target);
    while (length > 0 && (target[length - 1] == '\n' || target[length - 1] == '\r'))
    {
        target[--length] = '\0';
    }

    if (length == 0)
    {
        fprintf(stderr, "launch: %s is empty; rerun the hiding installer with --app-path\n",
                HIDING_LAUNCH_CONFIG);
        return false;
    }

    if (length == size - 1 && target[length - 1] != '\0')
    {
        fprintf(stderr, "launch: %s is too long\n", HIDING_LAUNCH_CONFIG);
        return false;
    }

    if (target[0] != '/')
    {
        fprintf(stderr, "launch: %s must contain an absolute path; rerun the hiding installer\n",
                HIDING_LAUNCH_CONFIG);
        return false;
    }

    return true;
}

static bool parent_directories_are_trusted(const char* target)
{
    char path[TARGET_MAX];
    int written = snprintf(path, sizeof(path), "%s", target);
    if (written < 0 || (size_t)written >= sizeof(path))
    {
        fprintf(stderr, "launch: target path is too long\n");
        return false;
    }

    for (;;)
    {
        char* slash = strrchr(path, '/');
        if (slash == NULL || slash == path)
        {
            return true;
        }

        *slash = '\0';

        struct stat status;
        if (stat(path, &status) != 0)
        {
            fprintf(stderr, "launch: cannot inspect %s: %s\n", path, strerror(errno));
            return false;
        }

        if (!S_ISDIR(status.st_mode))
        {
            fprintf(stderr, "launch: %s is not a directory\n", path);
            return false;
        }

        if ((status.st_mode & (S_IWGRP | S_IWOTH)) != 0 && (status.st_mode & S_ISVTX) == 0)
        {
            fprintf(stderr, "launch: %s is writable by group or other\n", path);
            return false;
        }
    }
}

static bool target_is_trusted(const char* target)
{
    struct stat status;
    if (lstat(target, &status) != 0)
    {
        fprintf(stderr, "launch: cannot inspect %s: %s\n", target, strerror(errno));
        return false;
    }

    if (!S_ISREG(status.st_mode))
    {
        fprintf(stderr, "launch: %s is not a regular file\n", target);
        return false;
    }

    if ((status.st_mode & (S_IXUSR | S_IXGRP | S_IXOTH)) == 0)
    {
        fprintf(stderr, "launch: %s is not executable\n", target);
        return false;
    }

    if ((status.st_mode & (S_IWGRP | S_IWOTH)) != 0)
    {
        fprintf(stderr, "launch: %s is writable by group or other\n", target);
        return false;
    }

    return parent_directories_are_trusted(target);
}

int main(int argc, char** argv)
{
    (void)argv;

    if (argc != 1)
    {
        fprintf(stderr, "usage: launch\n");
        return 2;
    }

    char target[TARGET_MAX];
    if (!read_target(target, sizeof(target)) || !target_is_trusted(target))
    {
        return 2;
    }

    if (chdir("/") != 0)
    {
        perror("launch: chdir");
        return 2;
    }

    char* const application_argv[] = {target, NULL};
    execv(target, application_argv);
    perror("launch");
    return 127;
}