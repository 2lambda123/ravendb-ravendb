#if defined(__APPLE__)

#include <sys/param.h>
#include <sys/mount.h>

#elif define(__unix__)

#include <sys/statfs.h>

#endif