#include <climits>
#include <cstddef>
#include <vector>

#include "ooz/dep/ooz/stdafx.h"
#include "ooz/dep/ooz/compress.h"

int Kraken_Decompress(const byte *src, size_t src_len, byte *dst, size_t dst_len);
int CompressBlock_Kraken(
    uint8 *src_in,
    uint8 *dst_in,
    int src_size,
    int level,
    const CompressOptions *compressopts,
    uint8 *src_window_base,
    LRMCascade *lrm);

#if defined(_WIN32)
#define STALKER_OOZ_EXPORT __declspec(dllexport)
#else
#define STALKER_OOZ_EXPORT __attribute__((visibility("default")))
#endif

extern "C" STALKER_OOZ_EXPORT int stalker_kraken_decompress(
    const uint8 *source,
    int source_length,
    uint8 *target,
    int target_length) {
    if (source == nullptr || target == nullptr || source_length <= 0 || target_length <= 0) {
        return -1;
    }

    return Kraken_Decompress(source, static_cast<size_t>(source_length), target, static_cast<size_t>(target_length));
}

extern "C" STALKER_OOZ_EXPORT int stalker_kraken_compress(
    uint8 *source,
    int source_length,
    int level,
    uint8 *target,
    int target_capacity) {
    if (source == nullptr || target == nullptr || source_length <= 0 || target_capacity <= 0) {
        return -1;
    }

    const size_t block_count = (static_cast<size_t>(source_length) + 0x3FFFFU) / 0x40000U;
    const size_t required_capacity = static_cast<size_t>(source_length) + 1024U + 512U * block_count;
    if (required_capacity > static_cast<size_t>(target_capacity)) {
        return -1;
    }

    return CompressBlock_Kraken(
        source,
        target,
        source_length,
        level,
        nullptr,
        nullptr,
        nullptr);
}
