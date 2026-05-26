#if defined(KATAGO_BRIDGE_ANDROID_OPENCL_DIAGNOSTICS)

#include "main.h"

#include "../neuralnet/nninterface.h"
#include "../neuralnet/nneval.h"
#include "../neuralnet/openclhelpers.h"
#include "../neuralnet/opencltuner.h"

#include <android/log.h>

#include <chrono>
#include <cstdlib>
#include <fstream>
#include <mutex>
#include <sstream>
#include <string>
#include <vector>

extern "C" {
void weiqixn_bridge_android_opencl_diag_set_work_dir(const char* workingDirectory);
void weiqixn_bridge_android_opencl_diag_log(const char* message);
}

namespace {

using Clock = std::chrono::steady_clock;

const Clock::time_point gStartTime = Clock::now();
std::mutex gLogMutex;
std::string gLogPath;

long long elapsedMs() {
  return std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - gStartTime).count();
}

std::string sanitize(const std::string& value, size_t maxLength = 360) {
  std::string result = value;
  for (char& ch : result) {
    if (ch == '\r' || ch == '\n' || ch == '\t') {
      ch = ' ';
    }
  }
  if (result.size() > maxLength) {
    result = result.substr(0, maxLength) + "...";
  }
  return result;
}

std::string getDiagnosticLogPath() {
  const char* explicitPath = std::getenv("WEIQIXN_KATAGO_BRIDGE_DIAGNOSTIC_LOG");
  if (explicitPath != nullptr && explicitPath[0] != '\0') {
    return explicitPath;
  }

  const char* workDir = std::getenv("WEIQIXN_KATAGO_BRIDGE_WORKDIR");
  if (workDir != nullptr && workDir[0] != '\0') {
    std::string path = workDir;
    if (!path.empty() && path.back() != '/') {
      path += "/";
    }
    path += "KataGoData/weiqixn_opencl_stage_diagnostics.log";
    return path;
  }

  return std::string();
}

void appendDiagnosticLine(const std::string& line) {
  std::string value = "[WeiqiXN OpenCL diag +" + std::to_string(elapsedMs()) + "ms] " + line;
  __android_log_print(ANDROID_LOG_INFO, "WeiqiXNKataGoBridge", "%s", value.c_str());

  std::lock_guard<std::mutex> lock(gLogMutex);
  if (gLogPath.empty()) {
    gLogPath = getDiagnosticLogPath();
  }
  if (gLogPath.empty()) {
    return;
  }

  std::ofstream out(gLogPath, std::ios::out | std::ios::app);
  if (out.good()) {
    out << value << "\n";
  }
}

std::string describeGpuIdxs(const std::vector<int>& gpuIdxs) {
  std::ostringstream out;
  out << "[";
  for (size_t i = 0; i < gpuIdxs.size(); i++) {
    if (i > 0) {
      out << ",";
    }
    out << gpuIdxs[i];
  }
  out << "]";
  return out.str();
}

std::string pointerText(const void* value) {
  std::ostringstream out;
  out << value;
  return out.str();
}

std::string platformInfoName(cl_platform_info name) {
  switch (name) {
    case CL_PLATFORM_PROFILE:
      return "CL_PLATFORM_PROFILE";
    case CL_PLATFORM_VERSION:
      return "CL_PLATFORM_VERSION";
    case CL_PLATFORM_NAME:
      return "CL_PLATFORM_NAME";
    case CL_PLATFORM_VENDOR:
      return "CL_PLATFORM_VENDOR";
    case CL_PLATFORM_EXTENSIONS:
      return "CL_PLATFORM_EXTENSIONS";
    default:
      return "CL_PLATFORM_INFO_" + std::to_string(static_cast<unsigned long long>(name));
  }
}

std::string deviceInfoName(cl_device_info name) {
  switch (name) {
    case CL_DEVICE_TYPE:
      return "CL_DEVICE_TYPE";
    case CL_DEVICE_NAME:
      return "CL_DEVICE_NAME";
    case CL_DEVICE_VENDOR:
      return "CL_DEVICE_VENDOR";
    case CL_DEVICE_VERSION:
      return "CL_DEVICE_VERSION";
    case CL_DEVICE_EXTENSIONS:
      return "CL_DEVICE_EXTENSIONS";
    default:
      return "CL_DEVICE_INFO_" + std::to_string(static_cast<unsigned long long>(name));
  }
}

class ScopedStage final {
 public:
  explicit ScopedStage(std::string stageName)
      : name(std::move(stageName)),
        startedAt(Clock::now()),
        completed(false) {
    appendDiagnosticLine(name + " begin");
  }

  ~ScopedStage() {
    if (!completed) {
      const long long ms = std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - startedAt).count();
      appendDiagnosticLine(name + " exit without completion after " + std::to_string(ms) + "ms");
    }
  }

  void complete(const std::string& detail = std::string()) {
    completed = true;
    const long long ms = std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - startedAt).count();
    appendDiagnosticLine(name + " end after " + std::to_string(ms) + "ms" + (detail.empty() ? "" : " " + detail));
  }

 private:
  std::string name;
  Clock::time_point startedAt;
  bool completed;
};

}  // namespace

extern "C" void weiqixn_bridge_android_opencl_diag_set_work_dir(const char* workingDirectory) {
  if (workingDirectory != nullptr && workingDirectory[0] != '\0') {
    setenv("WEIQIXN_KATAGO_BRIDGE_WORKDIR", workingDirectory, 1);
  }
  gLogPath.clear();
  appendDiagnosticLine(std::string("diagnostic log initialized, workDir=") + (workingDirectory == nullptr ? "" : sanitize(workingDirectory)));
}

extern "C" void weiqixn_bridge_android_opencl_diag_log(const char* message) {
  appendDiagnosticLine(message == nullptr ? std::string() : sanitize(message, 1000));
}

extern "C" cl_int __real_clGetPlatformIDs(cl_uint num_entries, cl_platform_id* platforms, cl_uint* num_platforms);

extern "C" cl_int __wrap_clGetPlatformIDs(cl_uint num_entries, cl_platform_id* platforms, cl_uint* num_platforms) {
  std::ostringstream detail;
  detail << "num_entries=" << num_entries
         << " platforms=" << pointerText(platforms)
         << " num_platforms=" << pointerText(num_platforms);
  ScopedStage stage("clGetPlatformIDs " + detail.str());
  cl_int result = __real_clGetPlatformIDs(num_entries, platforms, num_platforms);
  std::ostringstream complete;
  complete << "err=" << result;
  if (num_platforms != nullptr) {
    complete << " num_platforms_value=" << *num_platforms;
  }
  stage.complete(complete.str());
  return result;
}

extern "C" cl_int __real_clGetPlatformInfo(
    cl_platform_id platform,
    cl_platform_info param_name,
    size_t param_value_size,
    void* param_value,
    size_t* param_value_size_ret);

extern "C" cl_int __wrap_clGetPlatformInfo(
    cl_platform_id platform,
    cl_platform_info param_name,
    size_t param_value_size,
    void* param_value,
    size_t* param_value_size_ret) {
  std::ostringstream detail;
  detail << "platform=" << pointerText(platform)
         << " param=" << platformInfoName(param_name)
         << " value_size=" << param_value_size
         << " value=" << pointerText(param_value)
         << " size_ret=" << pointerText(param_value_size_ret);
  ScopedStage stage("clGetPlatformInfo " + detail.str());
  cl_int result = __real_clGetPlatformInfo(platform, param_name, param_value_size, param_value, param_value_size_ret);
  std::ostringstream complete;
  complete << "err=" << result;
  if (param_value_size_ret != nullptr) {
    complete << " size_ret_value=" << *param_value_size_ret;
  }
  if (result == CL_SUCCESS && param_value != nullptr && param_value_size > 0 &&
      (param_name == CL_PLATFORM_PROFILE || param_name == CL_PLATFORM_VERSION || param_name == CL_PLATFORM_NAME ||
       param_name == CL_PLATFORM_VENDOR || param_name == CL_PLATFORM_EXTENSIONS)) {
    complete << " value_text=" << sanitize(std::string(static_cast<const char*>(param_value)), 160);
  }
  stage.complete(complete.str());
  return result;
}

extern "C" cl_int __real_clGetDeviceIDs(
    cl_platform_id platform,
    cl_device_type device_type,
    cl_uint num_entries,
    cl_device_id* devices,
    cl_uint* num_devices);

extern "C" cl_int __wrap_clGetDeviceIDs(
    cl_platform_id platform,
    cl_device_type device_type,
    cl_uint num_entries,
    cl_device_id* devices,
    cl_uint* num_devices) {
  std::ostringstream detail;
  detail << "platform=" << pointerText(platform)
         << " device_type=" << static_cast<unsigned long long>(device_type)
         << " num_entries=" << num_entries
         << " devices=" << pointerText(devices)
         << " num_devices=" << pointerText(num_devices);
  ScopedStage stage("clGetDeviceIDs " + detail.str());
  cl_int result = __real_clGetDeviceIDs(platform, device_type, num_entries, devices, num_devices);
  bool retriedGpuOnly = false;
  cl_int firstResult = result;
  if (result == CL_INVALID_DEVICE_TYPE && device_type == (CL_DEVICE_TYPE_CPU | CL_DEVICE_TYPE_GPU | CL_DEVICE_TYPE_ACCELERATOR)) {
    appendDiagnosticLine("clGetDeviceIDs returned CL_INVALID_DEVICE_TYPE for combined CPU|GPU|ACCELERATOR query; retrying CL_DEVICE_TYPE_GPU");
    if (num_devices != nullptr) {
      *num_devices = 0;
    }
    result = __real_clGetDeviceIDs(platform, CL_DEVICE_TYPE_GPU, num_entries, devices, num_devices);
    retriedGpuOnly = true;
  }
  std::ostringstream complete;
  complete << "err=" << result;
  if (retriedGpuOnly) {
    complete << " first_err=" << firstResult << " retry_device_type=CL_DEVICE_TYPE_GPU";
  }
  if (num_devices != nullptr) {
    complete << " num_devices_value=" << *num_devices;
  }
  stage.complete(complete.str());
  return result;
}

extern "C" cl_int __real_clGetDeviceInfo(
    cl_device_id device,
    cl_device_info param_name,
    size_t param_value_size,
    void* param_value,
    size_t* param_value_size_ret);

extern "C" cl_int __wrap_clGetDeviceInfo(
    cl_device_id device,
    cl_device_info param_name,
    size_t param_value_size,
    void* param_value,
    size_t* param_value_size_ret) {
  std::ostringstream detail;
  detail << "device=" << pointerText(device)
         << " param=" << deviceInfoName(param_name)
         << " value_size=" << param_value_size
         << " value=" << pointerText(param_value)
         << " size_ret=" << pointerText(param_value_size_ret);
  ScopedStage stage("clGetDeviceInfo " + detail.str());
  cl_int result = __real_clGetDeviceInfo(device, param_name, param_value_size, param_value, param_value_size_ret);
  std::ostringstream complete;
  complete << "err=" << result;
  if (param_value_size_ret != nullptr) {
    complete << " size_ret_value=" << *param_value_size_ret;
  }
  if (result == CL_SUCCESS && param_value != nullptr && param_value_size > 0 &&
      (param_name == CL_DEVICE_NAME || param_name == CL_DEVICE_VENDOR || param_name == CL_DEVICE_VERSION ||
       param_name == CL_DEVICE_EXTENSIONS)) {
    complete << " value_text=" << sanitize(std::string(static_cast<const char*>(param_value)), 160);
  }
  stage.complete(complete.str());
  return result;
}

extern "C" LoadedModel* __real__ZN9NeuralNet13loadModelFileERKNSt6__ndk112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEES8_(
    const std::string& file,
    const std::string& expectedSha256);

extern "C" LoadedModel* __wrap__ZN9NeuralNet13loadModelFileERKNSt6__ndk112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEES8_(
    const std::string& file,
    const std::string& expectedSha256) {
  (void)expectedSha256;
  ScopedStage stage("NeuralNet::loadModelFile file=" + sanitize(file));
  LoadedModel* result = __real__ZN9NeuralNet13loadModelFileERKNSt6__ndk112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEES8_(file, expectedSha256);
  stage.complete(std::string("result=") + (result == nullptr ? "null" : "ok"));
  return result;
}

extern "C" ComputeContext* __real__ZN9NeuralNet20createComputeContextERKNSt6__ndk16vectorIiNS0_9allocatorIiEEEEP6LoggeriiRKNS0_12basic_stringIcNS0_11char_traitsIcEENS2_IcEEEESF_b9enabled_tSG_PK11LoadedModel(
    const std::vector<int>& gpuIdxs,
    Logger* logger,
    int nnXLen,
    int nnYLen,
    const std::string& openCLTunerFile,
    const std::string& homeDataDirOverride,
    bool openCLReTunePerBoardSize,
    enabled_t useFP16Mode,
    enabled_t useNHWCMode,
    const LoadedModel* loadedModel);

extern "C" ComputeContext* __wrap__ZN9NeuralNet20createComputeContextERKNSt6__ndk16vectorIiNS0_9allocatorIiEEEEP6LoggeriiRKNS0_12basic_stringIcNS0_11char_traitsIcEENS2_IcEEEESF_b9enabled_tSG_PK11LoadedModel(
    const std::vector<int>& gpuIdxs,
    Logger* logger,
    int nnXLen,
    int nnYLen,
    const std::string& openCLTunerFile,
    const std::string& homeDataDirOverride,
    bool openCLReTunePerBoardSize,
    enabled_t useFP16Mode,
    enabled_t useNHWCMode,
    const LoadedModel* loadedModel) {
  std::ostringstream detail;
  detail << "gpuIdxs=" << describeGpuIdxs(gpuIdxs)
         << " nn=" << nnXLen << "x" << nnYLen
         << " tunerFile=" << sanitize(openCLTunerFile)
         << " homeDataDir=" << sanitize(homeDataDirOverride)
         << " retunePerBoardSize=" << (openCLReTunePerBoardSize ? "true" : "false")
         << " useFP16=" << useFP16Mode.toString()
         << " useNHWC=" << useNHWCMode.toString();
  ScopedStage stage("NeuralNet::createComputeContext " + detail.str());
  ComputeContext* result = __real__ZN9NeuralNet20createComputeContextERKNSt6__ndk16vectorIiNS0_9allocatorIiEEEEP6LoggeriiRKNS0_12basic_stringIcNS0_11char_traitsIcEENS2_IcEEEESF_b9enabled_tSG_PK11LoadedModel(
      gpuIdxs, logger, nnXLen, nnYLen, openCLTunerFile, homeDataDirOverride, openCLReTunePerBoardSize, useFP16Mode, useNHWCMode, loadedModel);
  stage.complete(std::string("result=") + (result == nullptr ? "null" : "ok"));
  return result;
}

extern "C" std::vector<DeviceInfo> __real__ZN10DeviceInfo25getAllDeviceInfosOnSystemEP6Logger(Logger* logger);

extern "C" std::vector<DeviceInfo> __wrap__ZN10DeviceInfo25getAllDeviceInfosOnSystemEP6Logger(Logger* logger) {
  ScopedStage stage("DeviceInfo::getAllDeviceInfosOnSystem");
  std::vector<DeviceInfo> result = __real__ZN10DeviceInfo25getAllDeviceInfosOnSystemEP6Logger(logger);
  std::ostringstream detail;
  detail << "count=" << result.size();
  for (const DeviceInfo& device : result) {
    detail << " [idx=" << device.gpuIdx
           << " name=" << sanitize(device.name, 80)
           << " vendor=" << sanitize(device.vendor, 80)
           << " platform=" << sanitize(device.platformDesc, 120)
           << " version=" << sanitize(device.openCLVersion, 80)
           << "]";
  }
  stage.complete(detail.str());
  return result;
}

extern "C" OpenCLTuneParams __real__ZN11OpenCLTuner14loadOrAutoTuneENSt6__ndk112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEERKS6_S8_iP6Loggerbii9enabled_tSB_SB_SB_NS_18ModelInfoForTuningEb(
    std::string openCLTunerFile,
    const std::string& homeDataDirOverride,
    const std::string& gpuName,
    int gpuIdxForTuning,
    Logger* logger,
    bool openCLReTunePerBoardSize,
    int nnXLen,
    int nnYLen,
    enabled_t testFP16Mode,
    enabled_t testFP16StorageMode,
    enabled_t testFP16ComputeMode,
    enabled_t testFP16TensorCoresMode,
    OpenCLTuner::ModelInfoForTuning modelInfo,
    bool full);

extern "C" OpenCLTuneParams __wrap__ZN11OpenCLTuner14loadOrAutoTuneENSt6__ndk112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEERKS6_S8_iP6Loggerbii9enabled_tSB_SB_SB_NS_18ModelInfoForTuningEb(
    std::string openCLTunerFile,
    const std::string& homeDataDirOverride,
    const std::string& gpuName,
    int gpuIdxForTuning,
    Logger* logger,
    bool openCLReTunePerBoardSize,
    int nnXLen,
    int nnYLen,
    enabled_t testFP16Mode,
    enabled_t testFP16StorageMode,
    enabled_t testFP16ComputeMode,
    enabled_t testFP16TensorCoresMode,
    OpenCLTuner::ModelInfoForTuning modelInfo,
    bool full) {
  std::ostringstream detail;
  detail << "file=" << sanitize(openCLTunerFile)
         << " homeDataDir=" << sanitize(homeDataDirOverride)
         << " gpuName=" << sanitize(gpuName)
         << " gpuIdx=" << gpuIdxForTuning
         << " nn=" << nnXLen << "x" << nnYLen
         << " trunkC=" << modelInfo.trunkNumChannels
         << " modelVersion=" << modelInfo.modelVersion
         << " retunePerBoardSize=" << (openCLReTunePerBoardSize ? "true" : "false")
         << " full=" << (full ? "true" : "false")
         << " fp16=" << testFP16Mode.toString()
         << " fp16Storage=" << testFP16StorageMode.toString()
         << " fp16Compute=" << testFP16ComputeMode.toString()
         << " fp16TensorCores=" << testFP16TensorCoresMode.toString();
  ScopedStage stage("OpenCLTuner::loadOrAutoTune " + detail.str());
  OpenCLTuneParams result = __real__ZN11OpenCLTuner14loadOrAutoTuneENSt6__ndk112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEERKS6_S8_iP6Loggerbii9enabled_tSB_SB_SB_NS_18ModelInfoForTuningEb(
      openCLTunerFile,
      homeDataDirOverride,
      gpuName,
      gpuIdxForTuning,
      logger,
      openCLReTunePerBoardSize,
      nnXLen,
      nnYLen,
      testFP16Mode,
      testFP16StorageMode,
      testFP16ComputeMode,
      testFP16TensorCoresMode,
      modelInfo,
      full);
  stage.complete(
      std::string("valid=") + (result.isValid() ? "true" : "false") +
      " shouldUseFP16Storage=" + (result.shouldUseFP16Storage ? "true" : "false") +
      " shouldUseFP16Compute=" + (result.shouldUseFP16Compute ? "true" : "false") +
      " shouldUseFP16TensorCores=" + (result.shouldUseFP16TensorCores ? "true" : "false"));
  return result;
}

extern "C" void __real__ZN11NNEvaluatorC1ERKNSt6__ndk112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEES8_S8_P6LoggeriiibbiibS8_S8_b9enabled_tSB_iRKNS0_6vectorIiNS4_IiEEEES8_bi(
    NNEvaluator* self,
    const std::string& modelName,
    const std::string& modelFileName,
    const std::string& expectedSha256,
    Logger* logger,
    int maxBatchSize,
    int nnXLen,
    int nnYLen,
    bool requireExactNNLen,
    bool inputsUseNHWC,
    int nnCacheSizePowerOfTwo,
    int nnMutexPoolSizePowerofTwo,
    bool debugSkipNeuralNet,
    const std::string& openCLTunerFile,
    const std::string& homeDataDirOverride,
    bool openCLReTunePerBoardSize,
    enabled_t useFP16Mode,
    enabled_t useNHWCMode,
    int numThreads,
    const std::vector<int>& gpuIdxByServerThread,
    const std::string& randSeed,
    bool doRandomize,
    int defaultSymmetry);

extern "C" void __wrap__ZN11NNEvaluatorC1ERKNSt6__ndk112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEES8_S8_P6LoggeriiibbiibS8_S8_b9enabled_tSB_iRKNS0_6vectorIiNS4_IiEEEES8_bi(
    NNEvaluator* self,
    const std::string& modelName,
    const std::string& modelFileName,
    const std::string& expectedSha256,
    Logger* logger,
    int maxBatchSize,
    int nnXLen,
    int nnYLen,
    bool requireExactNNLen,
    bool inputsUseNHWC,
    int nnCacheSizePowerOfTwo,
    int nnMutexPoolSizePowerofTwo,
    bool debugSkipNeuralNet,
    const std::string& openCLTunerFile,
    const std::string& homeDataDirOverride,
    bool openCLReTunePerBoardSize,
    enabled_t useFP16Mode,
    enabled_t useNHWCMode,
    int numThreads,
    const std::vector<int>& gpuIdxByServerThread,
    const std::string& randSeed,
    bool doRandomize,
    int defaultSymmetry) {
  std::ostringstream detail;
  detail << "modelName=" << sanitize(modelName)
         << " modelFile=" << sanitize(modelFileName)
         << " maxBatch=" << maxBatchSize
         << " nn=" << nnXLen << "x" << nnYLen
         << " exact=" << (requireExactNNLen ? "true" : "false")
         << " inputsUseNHWC=" << (inputsUseNHWC ? "true" : "false")
         << " cachePow=" << nnCacheSizePowerOfTwo
         << " mutexPoolPow=" << nnMutexPoolSizePowerofTwo
         << " skipNN=" << (debugSkipNeuralNet ? "true" : "false")
         << " tunerFile=" << sanitize(openCLTunerFile)
         << " homeDataDir=" << sanitize(homeDataDirOverride)
         << " retunePerBoardSize=" << (openCLReTunePerBoardSize ? "true" : "false")
         << " useFP16=" << useFP16Mode.toString()
         << " useNHWC=" << useNHWCMode.toString()
         << " numThreads=" << numThreads
         << " gpuIdxByThread=" << describeGpuIdxs(gpuIdxByServerThread)
         << " randomize=" << (doRandomize ? "true" : "false")
         << " defaultSymmetry=" << defaultSymmetry;
  (void)randSeed;
  ScopedStage stage("NNEvaluator::NNEvaluator " + detail.str());
  __real__ZN11NNEvaluatorC1ERKNSt6__ndk112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEES8_S8_P6LoggeriiibbiibS8_S8_b9enabled_tSB_iRKNS0_6vectorIiNS4_IiEEEES8_bi(
      self,
      modelName,
      modelFileName,
      expectedSha256,
      logger,
      maxBatchSize,
      nnXLen,
      nnYLen,
      requireExactNNLen,
      inputsUseNHWC,
      nnCacheSizePowerOfTwo,
      nnMutexPoolSizePowerofTwo,
      debugSkipNeuralNet,
      openCLTunerFile,
      homeDataDirOverride,
      openCLReTunePerBoardSize,
      useFP16Mode,
      useNHWCMode,
      numThreads,
      gpuIdxByServerThread,
      randSeed,
      doRandomize,
      defaultSymmetry);
  stage.complete("constructed");
}

#endif
