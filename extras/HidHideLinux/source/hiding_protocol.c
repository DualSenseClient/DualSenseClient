#include "hiding.h"

#include <errno.h>
#include <limits.h>
#include <string.h>
#include <unistd.h>

static int digit_value(char character, int base)
{
    if (character >= '0' && character <= '9')
    {
        return character - '0';
    }

    if (base == 16 && character >= 'a' && character <= 'f')
    {
        return character - 'a' + 10;
    }

    if (base == 16 && character >= 'A' && character <= 'F')
    {
        return character - 'A' + 10;
    }

    return -1;
}

bool hiding_parse_uint(const char* text, int base, unsigned long max, unsigned long* value,
                       const char** end)
{
    if (text == NULL || value == NULL || (base != 10 && base != 16))
    {
        return false;
    }

    const char* cursor = text;
    unsigned long result = 0;
    bool any = false;
    while (*cursor != '\0')
    {
        int digit = digit_value(*cursor, base);
        if (digit < 0 || digit >= base)
        {
            break;
        }

        if ((unsigned long)digit > max
            || result > (max - (unsigned long)digit) / (unsigned long)base)
        {
            return false;
        }

        result = result * (unsigned long)base + (unsigned long)digit;
        any = true;
        cursor++;
    }

    if (!any)
    {
        return false;
    }

    *value = result;
    if (end != NULL)
    {
        *end = cursor;
    }

    return true;
}

bool hiding_parse_hid_id(const char* text, unsigned int* bus, unsigned int* vendor,
                         unsigned int* product)
{
    if (text == NULL || bus == NULL || vendor == NULL || product == NULL)
    {
        return false;
    }

    const char* cursor = text;
    unsigned long parts[3];
    for (int index = 0; index < 3; index++)
    {
        if (!hiding_parse_uint(cursor, 16, UINT_MAX, &parts[index], &cursor))
        {
            return false;
        }

        if (index < 2)
        {
            if (*cursor != ':')
            {
                return false;
            }

            cursor++;
        }
    }

    if (*cursor != '\0')
    {
        return false;
    }

    *bus = (unsigned int)parts[0];
    *vendor = (unsigned int)parts[1];
    *product = (unsigned int)parts[2];
    return true;
}

bool hiding_parse_uids(const char* text, unsigned int* real, unsigned int* effective)
{
    if (text == NULL || real == NULL || effective == NULL)
    {
        return false;
    }

    const char* cursor = text;
    while (*cursor == ' ' || *cursor == '\t')
    {
        cursor++;
    }

    unsigned long first;
    if (!hiding_parse_uint(cursor, 10, UINT_MAX, &first, &cursor))
    {
        return false;
    }

    if (*cursor != ' ' && *cursor != '\t')
    {
        return false;
    }

    while (*cursor == ' ' || *cursor == '\t')
    {
        cursor++;
    }

    unsigned long second;
    if (!hiding_parse_uint(cursor, 10, UINT_MAX, &second, &cursor))
    {
        return false;
    }

    *real = (unsigned int)first;
    *effective = (unsigned int)second;
    return true;
}

bool hiding_id_valid(const char* id)
{
    if (id == NULL)
    {
        return false;
    }

    size_t length = strlen(id);
    if (length == 0 || length > 64)
    {
        return false;
    }

    if (strcmp(id, ".") == 0 || strcmp(id, "..") == 0)
    {
        return false;
    }

    for (size_t i = 0; i < length; i++)
    {
        char c = id[i];
        bool valid = (c >= 'A' && c <= 'Z')
            || (c >= 'a' && c <= 'z')
            || (c >= '0' && c <= '9')
            || c == ':'
            || c == '.'
            || c == '_'
            || c == '-';
        if (!valid)
        {
            return false;
        }
    }

    return true;
}

bool hiding_parse_hello(const char* line, int* protocol, char* version, size_t version_size)
{
    if (line == NULL || protocol == NULL || version == NULL || version_size == 0)
    {
        return false;
    }

    if (strncmp(line, "OK ", 3) != 0)
    {
        return false;
    }

    const char* cursor = line + 3;
    const char* end = NULL;
    unsigned long parsed;
    if (!hiding_parse_uint(cursor, 10, INT_MAX, &parsed, &end) || *end != ' ')
    {
        return false;
    }

    cursor = end + 1;
    const char* space = strchr(cursor, ' ');
    size_t length = space == NULL ? strlen(cursor) : (size_t)(space - cursor);
    if (length == 0 || length >= version_size)
    {
        return false;
    }

    memcpy(version, cursor, length);
    version[length] = '\0';
    *protocol = (int)parsed;
    return true;
}

bool hiding_parse_status(const char* line, bool* hidden)
{
    if (line == NULL || hidden == NULL)
    {
        return false;
    }

    if (strcmp(line, "HIDDEN") == 0)
    {
        *hidden = true;
        return true;
    }

    if (strcmp(line, "VISIBLE") == 0)
    {
        *hidden = false;
        return true;
    }

    return false;
}

bool hiding_parse_command(const char* line, char* error, size_t error_size)
{
    if (line == NULL || error == NULL || error_size == 0)
    {
        return false;
    }

    if (strcmp(line, "OK") == 0)
    {
        error[0] = '\0';
        return true;
    }

    const char* message = NULL;
    if (strncmp(line, "ERR ", 4) == 0)
    {
        message = line + 4;
    }
    else if (strcmp(line, "ERR") == 0)
    {
        message = "ERR";
    }

    if (message == NULL)
    {
        error[0] = '\0';
        return false;
    }

    size_t length = strlen(message);
    if (length >= error_size)
    {
        length = error_size - 1;
    }

    memcpy(error, message, length);
    error[length] = '\0';
    return false;
}

int hiding_write_all(int fd, const char* data, size_t length)
{
    size_t offset = 0;
    while (offset < length)
    {
        ssize_t written = write(fd, data + offset, length - offset);
        if (written < 0)
        {
            if (errno == EINTR)
            {
                continue;
            }

            return -errno;
        }

        if (written == 0)
        {
            return -EIO;
        }

        offset += (size_t)written;
    }

    return 0;
}