#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <new>
#include <vector>

#include <jxl/cms.h>
#include <jxl/decode.h>
#include <jxl/encode.h>
#include <jxl/resizable_parallel_runner.h>

struct ImageInfo {
    uint32_t width;
    uint32_t height;
    uint32_t animated;
    uint32_t loops;
    float peakNits; // Zero for SDR. HDR pixels are linear and relative to this peak.
};

struct Decoder {
    JxlDecoder* decoder = JxlDecoderCreate(nullptr);
    void* runner = JxlResizableParallelRunnerCreate(nullptr);
    JxlBasicInfo info{};
    JxlPixelFormat format{4, JXL_TYPE_UINT8, JXL_NATIVE_ENDIAN, 0};

    ~Decoder() {
        if (decoder) JxlDecoderDestroy(decoder);
        if (runner) JxlResizableParallelRunnerDestroy(runner);
    }
};

static JxlTransferFunction GetTransfer(Decoder& state, const JxlCmsInterface& cms) {
    JxlColorEncoding color{};
    if (JxlDecoderGetColorAsEncodedProfile(state.decoder, JXL_COLOR_PROFILE_TARGET_ORIGINAL,
                                           &color) == JXL_DEC_SUCCESS) {
        return color.transfer_function;
    }

    size_t iccSize = 0;
    if (JxlDecoderGetICCProfileSize(state.decoder, JXL_COLOR_PROFILE_TARGET_ORIGINAL,
                                    &iccSize) == JXL_DEC_SUCCESS && iccSize > 0) {
        std::vector<uint8_t> icc(iccSize);
        JXL_BOOL cmyk = JXL_FALSE;
        if (JxlDecoderGetColorAsICCProfile(state.decoder, JXL_COLOR_PROFILE_TARGET_ORIGINAL,
                                          icc.data(), icc.size()) == JXL_DEC_SUCCESS &&
            cms.set_fields_from_icc(cms.set_fields_data, icc.data(), icc.size(), &color, &cmyk)) {
            return color.transfer_function;
        }
    }
    return JXL_TRANSFER_FUNCTION_UNKNOWN;
}

static bool Open(Decoder& state, const uint8_t* bytes, size_t size, ImageInfo& result) {
    auto* decoder = state.decoder;
    const auto& cms = *JxlGetDefaultCms();
    if (!decoder || !state.runner ||
        JxlDecoderSetParallelRunner(decoder, JxlResizableParallelRunner, state.runner) != JXL_DEC_SUCCESS ||
        JxlDecoderSetCms(decoder, cms) != JXL_DEC_SUCCESS ||
        JxlDecoderSetUnpremultiplyAlpha(decoder, JXL_TRUE) != JXL_DEC_SUCCESS ||
        JxlDecoderSubscribeEvents(decoder, JXL_DEC_BASIC_INFO | JXL_DEC_COLOR_ENCODING |
                                          JXL_DEC_FRAME | JXL_DEC_FULL_IMAGE) != JXL_DEC_SUCCESS ||
        JxlDecoderSetInput(decoder, bytes, size) != JXL_DEC_SUCCESS) {
        return false;
    }
    JxlDecoderCloseInput(decoder);
    if (JxlDecoderProcessInput(decoder) != JXL_DEC_BASIC_INFO ||
        JxlDecoderGetBasicInfo(decoder, &state.info) != JXL_DEC_SUCCESS) {
        return false;
    }
    const auto& info = state.info;
    JxlResizableParallelRunnerSetThreads(state.runner, std::min<size_t>(
        JxlResizableParallelRunnerSuggestThreads(info.xsize, info.ysize), 8));

    if (JxlDecoderProcessInput(decoder) != JXL_DEC_COLOR_ENCODING) return false;
    const auto transfer = GetTransfer(state, cms);
    const bool hdr = transfer == JXL_TRANSFER_FUNCTION_PQ ||
                     transfer == JXL_TRANSFER_FUNCTION_HLG ||
                     info.intensity_target > 255.0f;
    const bool gray = info.num_color_channels == 1;
    JxlColorEncoding output{};
    if (hdr) {
        state.format.data_type = JXL_TYPE_FLOAT;
        JxlColorEncodingSetToLinearSRGB(&output, gray);
        if (transfer == JXL_TRANSFER_FUNCTION_HLG && !info.uses_original_profile) {
            // XYB decoding enables the HLG OOTF when the requested peak differs.
            // Request nearly the same peak to retain the source's reference display.
            if (JxlDecoderSetDesiredIntensityTarget(decoder, info.intensity_target * 0.9999f) != JXL_DEC_SUCCESS)
                return false;
        }
    } else {
        JxlColorEncodingSetToSRGB(&output, gray);
    }
    if (JxlDecoderSetOutputColorProfile(decoder, &output, nullptr, 0) != JXL_DEC_SUCCESS)
        return false;

    result = {info.xsize, info.ysize, static_cast<uint32_t>(info.have_animation),
              info.animation.num_loops, hdr ? info.intensity_target : 0.0f};
    return true;
}

extern "C" __declspec(dllexport)
Decoder* UviewerJxlOpen(const uint8_t* bytes, size_t size, ImageInfo* info) {
    try {
        auto state = std::make_unique<Decoder>();
        if (Open(*state, bytes, size, *info)) return state.release();
    } catch (...) {
        // Native allocation failures must not cross the managed boundary.
    }
    return nullptr;
}

extern "C" __declspec(dllexport)
void UviewerJxlClose(Decoder* state) {
    delete state;
}

static int ReadFrame(Decoder& state, void* pixels, size_t size, uint32_t* duration,
                     double* millisecondsPerTick, int* isLast) {
    auto* decoder = state.decoder;
    const auto status = JxlDecoderProcessInput(decoder);
    if (status == JXL_DEC_SUCCESS) return 0;
    if (status != JXL_DEC_FRAME) return -1;

    JxlFrameHeader frame{};
    if (JxlDecoderGetFrameHeader(decoder, &frame) != JXL_DEC_SUCCESS ||
        JxlDecoderProcessInput(decoder) != JXL_DEC_NEED_IMAGE_OUT_BUFFER ||
        JxlDecoderSetImageOutBuffer(decoder, &state.format, pixels, size) != JXL_DEC_SUCCESS ||
        JxlDecoderProcessInput(decoder) != JXL_DEC_FULL_IMAGE) {
        return -1;
    }

    const auto& timing = state.info.animation;
    *duration = frame.duration;
    *millisecondsPerTick = timing.tps_numerator == 0 ? 0.0 :
        1000.0 * timing.tps_denominator / timing.tps_numerator;
    *isLast = frame.is_last;
    return 1;
}

// Uses libjxl's default coalescing, including crop, blending and orientation.
extern "C" __declspec(dllexport)
int UviewerJxlReadFrame(Decoder* state, void* pixels, size_t size, uint32_t* duration,
                       double* millisecondsPerTick, int* isLast) {
    try {
        return ReadFrame(*state, pixels, size, duration, millisecondsPerTick, isLast);
    } catch (...) {
        return -1;
    }
}
