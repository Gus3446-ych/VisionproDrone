#import <Foundation/Foundation.h>
#import <VideoToolbox/VideoToolbox.h>
#import <CoreVideo/CoreVideo.h>
#include <algorithm>
#include <cstring>
#include <mutex>
#include <vector>

namespace {

constexpr int kDefaultWidth = 1280;
constexpr int kDefaultHeight = 720;

std::mutex gMutex;
VTDecompressionSessionRef gSession = nullptr;
CMVideoFormatDescriptionRef gFormatDescription = nullptr;
std::vector<uint8_t> gSps;
std::vector<uint8_t> gPps;
std::vector<uint8_t> gLatestFrame;
int gFrameWidth = kDefaultWidth;
int gFrameHeight = kDefaultHeight;
bool gHasNewFrame = false;
bool gEnabled = false;
int gPacketCount = 0;
int gNalCount = 0;
int gDecodeAttemptCount = 0;
int gDecodedFrameCount = 0;
int gLastDecodeStatus = 0;
int gLastNalType = -1;

static size_t StartCodeLength(const uint8_t* data, size_t offset, size_t size)
{
    if (offset + 3 <= size && data[offset] == 0 && data[offset + 1] == 0 && data[offset + 2] == 1)
        return 3;

    if (offset + 4 <= size && data[offset] == 0 && data[offset + 1] == 0 && data[offset + 2] == 0 && data[offset + 3] == 1)
        return 4;

    return 0;
}

static std::vector<std::pair<size_t, size_t>> FindNalRanges(const uint8_t* data, size_t size)
{
    std::vector<std::pair<size_t, size_t>> ranges;
    size_t cursor = 0;

    while (cursor < size) {
        size_t startCode = 0;
        while (cursor < size) {
            startCode = StartCodeLength(data, cursor, size);
            if (startCode != 0)
                break;
            cursor++;
        }

        if (cursor >= size)
            break;

        size_t nalStart = cursor + startCode;
        cursor = nalStart;

        while (cursor < size && StartCodeLength(data, cursor, size) == 0)
            cursor++;

        if (cursor > nalStart)
            ranges.emplace_back(nalStart, cursor - nalStart);
    }

    return ranges;
}

static void ReleaseDecoder()
{
    if (gSession) {
        VTDecompressionSessionInvalidate(gSession);
        CFRelease(gSession);
        gSession = nullptr;
    }

    if (gFormatDescription) {
        CFRelease(gFormatDescription);
        gFormatDescription = nullptr;
    }
}

static void DecompressionOutputCallback(
    void*,
    void*,
    OSStatus status,
    VTDecodeInfoFlags,
    CVImageBufferRef imageBuffer,
    CMTime,
    CMTime)
{
    if (status != noErr || imageBuffer == nullptr)
    {
        std::lock_guard<std::mutex> lock(gMutex);
        gLastDecodeStatus = static_cast<int>(status);
        return;
    }

    CVPixelBufferRef pixelBuffer = (CVPixelBufferRef)imageBuffer;
    CVPixelBufferLockBaseAddress(pixelBuffer, kCVPixelBufferLock_ReadOnly);

    const size_t width = CVPixelBufferGetWidth(pixelBuffer);
    const size_t height = CVPixelBufferGetHeight(pixelBuffer);
    const size_t sourceStride = CVPixelBufferGetBytesPerRow(pixelBuffer);
    const uint8_t* source = static_cast<const uint8_t*>(CVPixelBufferGetBaseAddress(pixelBuffer));

    if (source != nullptr && width > 0 && height > 0) {
        std::vector<uint8_t> rgba(width * height * 4);

        for (size_t y = 0; y < height; y++) {
            const uint8_t* src = source + (y * sourceStride);
            uint8_t* dst = rgba.data() + ((height - 1 - y) * width * 4);

            for (size_t x = 0; x < width; x++) {
                // VideoToolbox gives BGRA. Unity TextureFormat.RGBA32 expects RGBA byte order here.
                dst[x * 4 + 0] = src[x * 4 + 2];
                dst[x * 4 + 1] = src[x * 4 + 1];
                dst[x * 4 + 2] = src[x * 4 + 0];
                dst[x * 4 + 3] = src[x * 4 + 3];
            }
        }

        std::lock_guard<std::mutex> lock(gMutex);
        gLatestFrame.swap(rgba);
        gFrameWidth = static_cast<int>(width);
        gFrameHeight = static_cast<int>(height);
        gHasNewFrame = true;
        gDecodedFrameCount++;
        gLastDecodeStatus = 0;
    }

    CVPixelBufferUnlockBaseAddress(pixelBuffer, kCVPixelBufferLock_ReadOnly);
}

static bool EnsureDecoder()
{
    if (gSession != nullptr)
        return true;

    if (gSps.empty() || gPps.empty())
        return false;

    const uint8_t* parameterSetPointers[] = { gSps.data(), gPps.data() };
    const size_t parameterSetSizes[] = { gSps.size(), gPps.size() };

    OSStatus status = CMVideoFormatDescriptionCreateFromH264ParameterSets(
        kCFAllocatorDefault,
        2,
        parameterSetPointers,
        parameterSetSizes,
        4,
        &gFormatDescription);

    if (status != noErr)
        return false;

    NSDictionary* attributes = @{
        (id)kCVPixelBufferPixelFormatTypeKey: @(kCVPixelFormatType_32BGRA),
        (id)kCVPixelBufferMetalCompatibilityKey: @YES
    };

    VTDecompressionOutputCallbackRecord callback = {};
    callback.decompressionOutputCallback = DecompressionOutputCallback;

    status = VTDecompressionSessionCreate(
        kCFAllocatorDefault,
        gFormatDescription,
        nullptr,
        (__bridge CFDictionaryRef)attributes,
        &callback,
        &gSession);

    if (status != noErr) {
        ReleaseDecoder();
        return false;
    }

    return true;
}

static void DecodeNal(const uint8_t* nal, size_t nalSize)
{
    if (nal == nullptr || nalSize < 1)
        return;

    const uint8_t nalType = nal[0] & 0x1f;
    {
        std::lock_guard<std::mutex> lock(gMutex);
        gNalCount++;
        gLastNalType = static_cast<int>(nalType);
    }

    if (nalType == 7) {
        std::lock_guard<std::mutex> lock(gMutex);
        if (gSps != std::vector<uint8_t>(nal, nal + nalSize)) {
            gSps.assign(nal, nal + nalSize);
            ReleaseDecoder();
        }
        return;
    }

    if (nalType == 8) {
        std::lock_guard<std::mutex> lock(gMutex);
        if (gPps != std::vector<uint8_t>(nal, nal + nalSize)) {
            gPps.assign(nal, nal + nalSize);
            ReleaseDecoder();
        }
        return;
    }

    {
        std::lock_guard<std::mutex> lock(gMutex);
        if (!EnsureDecoder())
            return;
    }

    std::vector<uint8_t> sample(nalSize + 4);
    const uint32_t bigEndianSize = CFSwapInt32HostToBig(static_cast<uint32_t>(nalSize));
    memcpy(sample.data(), &bigEndianSize, 4);
    memcpy(sample.data() + 4, nal, nalSize);

    CMBlockBufferRef blockBuffer = nullptr;
    OSStatus status = CMBlockBufferCreateWithMemoryBlock(
        kCFAllocatorDefault,
        nullptr,
        sample.size(),
        kCFAllocatorDefault,
        nullptr,
        0,
        sample.size(),
        0,
        &blockBuffer);

    if (status != kCMBlockBufferNoErr)
        return;

    status = CMBlockBufferReplaceDataBytes(sample.data(), blockBuffer, 0, sample.size());
    if (status != kCMBlockBufferNoErr) {
        CFRelease(blockBuffer);
        return;
    }

    CMSampleBufferRef sampleBuffer = nullptr;
    const size_t sampleSize = sample.size();
    {
        std::lock_guard<std::mutex> lock(gMutex);
        if (gFormatDescription == nullptr) {
            CFRelease(blockBuffer);
            return;
        }

        status = CMSampleBufferCreateReady(
            kCFAllocatorDefault,
            blockBuffer,
            gFormatDescription,
            1,
            0,
            nullptr,
            1,
            &sampleSize,
            &sampleBuffer);
    }

    CFRelease(blockBuffer);

    if (status != noErr || sampleBuffer == nullptr)
        return;

    VTDecompressionSessionRef session = nullptr;
    {
        std::lock_guard<std::mutex> lock(gMutex);
        session = gSession;
        if (session)
            CFRetain(session);
    }

    if (session) {
        VTDecodeFrameFlags flags = 0;
        VTDecodeInfoFlags infoFlags = 0;
        status = VTDecompressionSessionDecodeFrame(session, sampleBuffer, flags, nullptr, &infoFlags);
        VTDecompressionSessionWaitForAsynchronousFrames(session);
        {
            std::lock_guard<std::mutex> lock(gMutex);
            gDecodeAttemptCount++;
            gLastDecodeStatus = static_cast<int>(status);
        }
        CFRelease(session);
    }

    CFRelease(sampleBuffer);
}

static void PutAnnexBData(const uint8_t* data, int size)
{
    if (!gEnabled || data == nullptr || size <= 0)
        return;

    {
        std::lock_guard<std::mutex> lock(gMutex);
        gPacketCount++;
    }

    const auto ranges = FindNalRanges(data, static_cast<size_t>(size));
    if (ranges.empty()) {
        DecodeNal(data, static_cast<size_t>(size));
        return;
    }

    for (const auto& range : ranges)
        DecodeNal(data + range.first, range.second);
}

} // namespace

extern "C" void UnityPluginEnable()
{
    std::lock_guard<std::mutex> lock(gMutex);
    gEnabled = true;
}

extern "C" void UnityPluginDisable()
{
    std::lock_guard<std::mutex> lock(gMutex);
    gEnabled = false;
    ReleaseDecoder();
    gSps.clear();
    gPps.clear();
    gLatestFrame.clear();
    gHasNewFrame = false;
    gPacketCount = 0;
    gNalCount = 0;
    gDecodeAttemptCount = 0;
    gDecodedFrameCount = 0;
    gLastDecodeStatus = 0;
    gLastNalType = -1;
}

extern "C" void SetTextureFromUnity(void*, int w, int h)
{
    std::lock_guard<std::mutex> lock(gMutex);
    gFrameWidth = w > 0 ? w : kDefaultWidth;
    gFrameHeight = h > 0 ? h : kDefaultHeight;
}

extern "C" void PutVideoDataFromUnity(const uint8_t* data, int size)
{
    PutAnnexBData(data, size);
}

extern "C" void* GetRenderEventFunc()
{
    return nullptr;
}

extern "C" int TelloVideoDecoderTryGetFrame(void* rgbaBuffer, int bufferSize, int* width, int* height)
{
    if (rgbaBuffer == nullptr || width == nullptr || height == nullptr)
        return 0;

    std::lock_guard<std::mutex> lock(gMutex);

    *width = gFrameWidth;
    *height = gFrameHeight;

    if (!gHasNewFrame || gLatestFrame.empty())
        return 0;

    if (bufferSize < static_cast<int>(gLatestFrame.size()))
        return 0;

    memcpy(rgbaBuffer, gLatestFrame.data(), gLatestFrame.size());
    gHasNewFrame = false;
    return 1;
}

extern "C" void TelloVideoDecoderGetStatus(
    int* packetCount,
    int* nalCount,
    int* decodeAttemptCount,
    int* decodedFrameCount,
    int* lastDecodeStatus,
    int* lastNalType,
    int* hasSps,
    int* hasPps)
{
    std::lock_guard<std::mutex> lock(gMutex);

    if (packetCount)
        *packetCount = gPacketCount;

    if (nalCount)
        *nalCount = gNalCount;

    if (decodeAttemptCount)
        *decodeAttemptCount = gDecodeAttemptCount;

    if (decodedFrameCount)
        *decodedFrameCount = gDecodedFrameCount;

    if (lastDecodeStatus)
        *lastDecodeStatus = gLastDecodeStatus;

    if (lastNalType)
        *lastNalType = gLastNalType;

    if (hasSps)
        *hasSps = gSps.empty() ? 0 : 1;

    if (hasPps)
        *hasPps = gPps.empty() ? 0 : 1;
}
