#if defined(__unix__) || defined(__APPLE__)

#define _GNU_SOURCE
#include <unistd.h>
#include <stdlib.h>
#include <stdio.h>
#include <sys/types.h>
#include <fcntl.h>
#include <errno.h>
#include <sys/mman.h>
#include <assert.h>
#include <time.h>

#include "rvn.h"
#include "status_codes.h"

PRIVATE int64_t
_nearest_size_to_page_size(int64_t orig_size, int64_t sys_page_size)
{
    int64_t mod = orig_size % sys_page_size;
    if (mod == 0)
    {
        return orig_size;
    }
    return ((orig_size / sys_page_size) + 1) * sys_page_size;
}

PRIVATE int64_t
_pwrite(int32_t fd, void *buffer, uint64_t count, uint64_t offset, int32_t *detailed_error_code)
{
    uint64_t actually_written = 0;
    int64_t cifs_retries = 3;
    do
    {
        int64_t result = pwrite(fd, buffer, count - actually_written, offset + actually_written);
        if (result < 0) /* we assume zero cannot be returned at any case as defined in POSIX */
        {
            if (errno == EINVAL && _sync_directory_allowed(fd) == SYNC_DIR_NOT_ALLOWED && --cifs_retries > 0)
            {
                /* cifs/nfs mount can sometimes fail on EINVAL after file creation
                lets give it few retries with short pauses between them - RavenDB-11954 */
                struct timespec ts;
                ts.tv_sec = 0;
                ts.tv_nsec = 100000000L * cifs_retries; /* 100mSec * retries..*/
                nanosleep(&ts, NULL);
                continue; /* retry cifs */
            }
            *detailed_error_code = errno;
            if (cifs_retries != 3)
                return FAIL_PWRITE_WITH_RETRIES;
            return FAIL_PWRITE;
        }
        actually_written += result;
    } while (actually_written < (int64_t)count);

    return SUCCESS;
}

PRIVATE int32_t
_allocate_file_space(int32_t fd, int64_t size, int32_t *detailed_error_code)
{
    int32_t result;
    int32_t retries;
    for (retries = 0; retries < 1024; retries++)
    {
#ifndef __APPLE__
        result = posix_fallocate64(fd, 0, size);
#else
        result = EINVAL;
#endif
        switch (result)
        {
        case EINVAL:
        case EFBIG: /* can occure on >4GB allocation on fs such as ntfs-3g, W95 FAT32, etc.*/
            /* fallocate is not supported, we'll use lseek instead */
            {
                char b = 0;
                int64_t rc = _pwrite(fd, &b, 1UL, (uint64_t)size - 1UL, detailed_error_code);
                if (rc != SUCCESS)
                    *detailed_error_code = errno;
                return rc;
            }
            break;
        case EINTR:
            *detailed_error_code = errno;
            continue; /* retry */

        case SUCCESS:
            return SUCCESS;

        default:
            *detailed_error_code = errno;
            return result;
        }
    }

    return result; /* return EINTR */
}

PRIVATE int32_t
_ensure_path_exists(const char* path)
{
    /* TODO: implement */
    return SUCCESS;   
}

EXPORT int32_t
rvn_dispose_handle(const char *filepath, void *handle, int32_t delete_on_close, int32_t *detailed_error_code)
{
    int32_t rc = SUCCESS;

    /* the following in two lines to avoid compilation warning */
    int32_t fd = (int)(long)handle;

    if (fd != -1)
    {
        if (delete_on_close == DELETE_ON_CLOSE_YES)
        {
            int32_t unlink_rc = unlink(filepath);
            if (unlink_rc != 0)
            {
                /* record the error and continue to close */
                rc = FAIL_UNLINK;
                *detailed_error_code = errno;
            }
        }

        int32_t close_rc = close(fd);
        if (close_rc != 0)
        {
            if (rc == 0) /* if unlink failed - return unlink's error */
            {
                rc = FAIL_CLOSE;
                *detailed_error_code = errno;
            }
        }
        return rc;
    }

    return FAIL_INVALID_HANDLE;
}

#endif