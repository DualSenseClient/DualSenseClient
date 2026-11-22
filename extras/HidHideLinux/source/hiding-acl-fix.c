#include <errno.h>
#include <stdio.h>
#include <sys/xattr.h>

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        return 2;
    }

    if (removexattr(argv[1], "system.posix_acl_access") != 0
        && errno != ENODATA
        && errno != ENOTSUP
        && errno != ENOENT)
    {
        perror("dualsense-hiding-acl-fix");
        return 1;
    }

    return 0;
}