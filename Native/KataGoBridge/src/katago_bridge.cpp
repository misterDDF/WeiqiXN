#include "main.h"
#include "external/nlohmann_json/json.hpp"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <fstream>
#include <iostream>
#include <memory>
#include <mutex>
#include <sstream>
#include <streambuf>
#include <string>
#include <system_error>
#include <thread>
#include <unordered_map>
#include <vector>
#include <ghc/filesystem.hpp>

using json = nlohmann::json;

#if defined(__ANDROID__) && defined(USE_OPENCL_BACKEND) && defined(KATAGO_BRIDGE_ANDROID_OPENCL_DIAGNOSTICS)
extern "C" {
void weiqixn_bridge_android_opencl_diag_set_work_dir(const char* workingDirectory);
void weiqixn_bridge_android_opencl_diag_log(const char* message);
}
#endif

#if defined(_WIN32)
#define KG_EXPORT extern "C" __declspec(dllexport)
#else
#define KG_EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace {

std::string trimCopy(const std::string& value) {
  size_t begin = value.find_first_not_of(" \t\r\n");
  if (begin == std::string::npos) {
    return std::string();
  }

  size_t end = value.find_last_not_of(" \t\r\n");
  return value.substr(begin, end - begin + 1);
}

std::string trimConfigValue(const std::string& value) {
  std::string trimmed = trimCopy(value);
  if (trimmed.size() >= 2) {
    char begin = trimmed.front();
    char end = trimmed.back();
    if ((begin == '"' && end == '"') || (begin == '\'' && end == '\'')) {
      return trimmed.substr(1, trimmed.size() - 2);
    }
  }

  return trimmed;
}

bool isRuntimePathKey(const std::string& key) {
  return key == "logDir" || key == "logDirDated" || key == "logFile" || key == "homeDataDir" || key == "openclTunerFile";
}

void configureAndroidOpenClIcdEnvironment() {
#if defined(__ANDROID__) && defined(USE_OPENCL_BACKEND)
  // Android vendor libOpenCL.so may itself be an ICD loader. Pointing
  // OCL_ICD_FILENAMES back at libOpenCL.so can make the loader re-enter itself
  // instead of using the device vendor driver, so leave vendor discovery to the
  // system library by default.
  unsetenv("OCL_ICD_FILENAMES");
#endif
}

std::string toConfigPathText(const ghc::filesystem::path& path) {
  std::string value = path.u8string();
  for (char& ch : value) {
    if (ch == '\\') {
      ch = '/';
    }
  }
  return value;
}

std::string quoteConfigValue(const std::string& value) {
  std::string quoted = "\"";
  for (char ch : value) {
    if (ch == '"' || ch == '\\') {
      quoted.push_back('\\');
    }
    quoted.push_back(ch);
  }
  quoted.push_back('"');
  return quoted;
}

bool ensureDirectoryExists(const ghc::filesystem::path& directory, const std::string& description, std::string& error) {
  std::error_code directoryError;
  if (ghc::filesystem::exists(directory, directoryError)) {
    if (directoryError) {
      error = "Could not inspect " + description + ": " + directory.u8string() + ", error: " + directoryError.message();
      return false;
    }

    if (!ghc::filesystem::is_directory(directory, directoryError)) {
      if (directoryError) {
        error = "Could not inspect " + description + ": " + directory.u8string() + ", error: " + directoryError.message();
        return false;
      }

      error = description + " exists but is not a directory: " + directory.u8string();
      return false;
    }

    return true;
  }

  if (directoryError) {
    error = "Could not inspect " + description + ": " + directory.u8string() + ", error: " + directoryError.message();
    return false;
  }

  ghc::filesystem::create_directories(directory, directoryError);
  if (!directoryError) {
    return true;
  }

  std::error_code retryError;
  if (ghc::filesystem::exists(directory, retryError) && ghc::filesystem::is_directory(directory, retryError)) {
    return true;
  }

  error = "Could not create " + description + ": " + directory.u8string() + ", error: " + directoryError.message();
  return false;
}

bool tryBuildRuntimePathOverrideConfig(
    const std::string& configPath,
    const std::string& workingDirectory,
    std::string& overrideConfigPath,
    std::string& error) {
  overrideConfigPath.clear();
  error.clear();

  if (configPath.empty() || workingDirectory.empty()) {
    return true;
  }

  std::ifstream configFile(configPath);
  if (!configFile.good()) {
    error = "Could not read KataGo config for runtime path normalization: " + configPath;
    return false;
  }

  std::vector<std::pair<std::string, std::string>> overrides;
  std::vector<std::string> originalLines;
  std::string line;
  while (std::getline(configFile, line)) {
    std::string configLine = line;
    size_t commentIndex = configLine.find('#');
    if (commentIndex != std::string::npos) {
      configLine = configLine.substr(0, commentIndex);
    }

    size_t equalsIndex = configLine.find('=');
    if (equalsIndex == std::string::npos) {
      continue;
    }

    std::string key = trimCopy(configLine.substr(0, equalsIndex));
    std::string value = trimConfigValue(configLine.substr(equalsIndex + 1));
    if (!isRuntimePathKey(key) || value.empty()) {
      originalLines.push_back(line);
      continue;
    }

    ghc::filesystem::path valuePath = ghc::filesystem::u8path(value);
    if (valuePath.is_absolute()) {
      originalLines.push_back(line);
      continue;
    }

    ghc::filesystem::path absolutePath = ghc::filesystem::u8path(workingDirectory) / valuePath;
    overrides.push_back(std::make_pair(key, toConfigPathText(absolutePath)));
    originalLines.push_back("# WeiqiXN bridge replaced relative runtime path: " + line);
  }

  if (overrides.empty()) {
    return true;
  }

  ghc::filesystem::path overrideDirectory = ghc::filesystem::u8path(workingDirectory);
  if (!ensureDirectoryExists(overrideDirectory, "KataGo bridge runtime config directory", error)) {
    return false;
  }

  ghc::filesystem::path overridePath = overrideDirectory / "weiqixn_bridge_resolved_config.cfg";
  std::ofstream overrideFile(overridePath.u8string(), std::ios::out | std::ios::trunc);
  if (!overrideFile.good()) {
    error = "Could not write KataGo bridge runtime config: " + overridePath.u8string();
    return false;
  }

  overrideFile << "# Generated by WeiqiXN native KataGo bridge. Do not edit.\n";
  overrideFile << "# Original config: " << toConfigPathText(ghc::filesystem::u8path(configPath)) << "\n";
  for (const std::string& originalLine : originalLines) {
    overrideFile << originalLine << "\n";
  }
  overrideFile << "\n# WeiqiXN runtime path overrides.\n";
  for (const auto& kv : overrides) {
    overrideFile << kv.first << " = " << quoteConfigValue(kv.second) << "\n";
  }
  overrideFile.close();
  if (!overrideFile.good()) {
    error = "Could not flush KataGo bridge runtime config: " + overridePath.u8string();
    return false;
  }

  overrideConfigPath = overridePath.u8string();
  return true;
}

class BlockingInputStreamBuf final : public std::streambuf {
 public:
  void pushLine(const std::string& line) {
    {
      std::lock_guard<std::mutex> lock(mutex);
      lines.push_back(line + "\n");
    }
    ready.notify_one();
  }

  void stop() {
    {
      std::lock_guard<std::mutex> lock(mutex);
      stopped = true;
    }
    ready.notify_all();
  }

 protected:
  int_type underflow() override {
    std::unique_lock<std::mutex> lock(mutex);
    ready.wait(lock, [this]() { return stopped || !lines.empty(); });
    if (lines.empty()) {
      return traits_type::eof();
    }

    current = std::move(lines.front());
    lines.pop_front();
    char* begin = &current[0];
    setg(begin, begin, begin + current.size());
    return traits_type::to_int_type(*gptr());
  }

 private:
  std::mutex mutex;
  std::condition_variable ready;
  std::deque<std::string> lines;
  std::string current;
  bool stopped = false;
};

class LineOutputStreamBuf final : public std::streambuf {
 public:
  bool waitPopLine(std::string& line, int timeoutMs) {
    std::unique_lock<std::mutex> lock(mutex);
    bool hasLine = ready.wait_for(
        lock,
        std::chrono::milliseconds(timeoutMs),
        [this]() { return stopped || !lines.empty(); });
    if (!hasLine || lines.empty()) {
      return false;
    }

    line = std::move(lines.front());
    lines.pop_front();
    return true;
  }

  void stop() {
    {
      std::lock_guard<std::mutex> lock(mutex);
      flushLineNoLock();
      stopped = true;
    }
    ready.notify_all();
  }

 protected:
  int_type overflow(int_type ch) override {
    if (traits_type::eq_int_type(ch, traits_type::eof())) {
      return traits_type::not_eof(ch);
    }

    appendChar(static_cast<char>(ch));
    return ch;
  }

  std::streamsize xsputn(const char* s, std::streamsize count) override {
    for (std::streamsize i = 0; i < count; i++) {
      appendChar(s[i]);
    }
    return count;
  }

  int sync() override {
    std::lock_guard<std::mutex> lock(mutex);
    flushLineNoLock();
    return 0;
  }

 private:
  void appendChar(char ch) {
    std::lock_guard<std::mutex> lock(mutex);
    if (ch == '\n') {
      flushLineNoLock();
      return;
    }

    if (ch != '\r') {
      pending.push_back(ch);
    }
  }

  void flushLineNoLock() {
    if (pending.empty()) {
      return;
    }

    lines.push_back(std::move(pending));
    pending.clear();
    ready.notify_one();
  }

  std::mutex mutex;
  std::condition_variable ready;
  std::deque<std::string> lines;
  std::string pending;
  bool stopped = false;
};

class KataGoBridgeEngine final {
  struct PendingAnalysis {
    std::condition_variable completedCv;
    std::vector<std::string> responseJsons;
    std::string error;
    int expectedResponseCount = 1;
    bool completed = false;
    bool failed = false;
  };

 public:
  ~KataGoBridgeEngine() {
    stop();
  }

  bool start(const char* configPath, const char* modelPath, const char* humanSlModelPath, const char* workingDirectory, std::string& error) {
    std::lock_guard<std::mutex> lock(stateMutex);
    if (started) {
      error = "KataGo bridge engine is already started.";
      return false;
    }

    if (configPath == nullptr || configPath[0] == '\0') {
      error = "Config path is empty.";
      return false;
    }
    if (modelPath == nullptr || modelPath[0] == '\0') {
      error = "Model path is empty.";
      return false;
    }

#if defined(__ANDROID__) && defined(USE_OPENCL_BACKEND) && defined(KATAGO_BRIDGE_ANDROID_OPENCL_DIAGNOSTICS)
    weiqixn_bridge_android_opencl_diag_set_work_dir(workingDirectory);
    weiqixn_bridge_android_opencl_diag_log(std::string("kg_create_engine config=").append(configPath).append(" model=").append(modelPath).c_str());
#endif

    configureAndroidOpenClIcdEnvironment();

    workDir = workingDirectory == nullptr ? std::string() : std::string(workingDirectory);
    std::string runtimeOverrideConfigPath;
    if (!tryBuildRuntimePathOverrideConfig(configPath, workDir, runtimeOverrideConfigPath, error)) {
      return false;
    }

    args.clear();
    args.push_back("analysis");
    args.push_back("-config");
    args.push_back(runtimeOverrideConfigPath.empty() ? std::string(configPath) : runtimeOverrideConfigPath);
    args.push_back("-model");
    args.push_back(modelPath);
    if (humanSlModelPath != nullptr && humanSlModelPath[0] != '\0') {
      args.push_back("-human-model");
      args.push_back(humanSlModelPath);
    }
    args.push_back("-quit-without-waiting");

    input = std::make_unique<BlockingInputStreamBuf>();
    output = std::make_unique<LineOutputStreamBuf>();
    stopped.store(false);
    exitCode.store(-1);

    worker = std::thread([this]() { runAnalysisThread(); });
    started = true;
    return true;
  }

  bool analyze(const char* requestJson, int timeoutMs, std::string& responseJson, std::string& error) {
    std::string responsesJson;
    if (!analyzeInternal(requestJson, timeoutMs, 1, responsesJson, error)) {
      return false;
    }

    try {
      json responses = json::parse(responsesJson);
      if (!responses.is_array() || responses.empty()) {
        error = "KataGo bridge analyze returned no responses.";
        return false;
      }

      responseJson = responses[0].dump();
      return true;
    }
    catch (const std::exception& ex) {
      error = std::string("Could not parse KataGo bridge analyze response array: ") + ex.what();
      return false;
    }
  }

  bool analyzeMany(const char* requestJson, int timeoutMs, std::string& responsesJson, std::string& error) {
    int expectedResponseCount = 1;
    try {
      json request = json::parse(requestJson == nullptr ? "" : requestJson);
      expectedResponseCount = resolveExpectedResponseCount(request);
    }
    catch (const std::exception&) {
      expectedResponseCount = 1;
    }

    return analyzeInternal(requestJson, timeoutMs, expectedResponseCount, responsesJson, error);
  }

  bool analyzeInternal(const char* requestJson, int timeoutMs, int expectedResponseCount, std::string& responsesJson, std::string& error) {
    if (requestJson == nullptr || requestJson[0] == '\0') {
      error = "Request JSON is empty.";
      return false;
    }

    std::string requestId;
    try {
      json request = json::parse(requestJson);
      if (!request.contains("id") || !request["id"].is_string()) {
        error = "Request JSON must contain a string id field.";
        return false;
      }
      requestId = request["id"].get<std::string>();
    }
    catch (const std::exception& ex) {
      error = std::string("Could not parse request JSON: ") + ex.what();
      return false;
    }

    {
      std::lock_guard<std::mutex> lock(stateMutex);
      if (!started || stopped.load()) {
        error = "KataGo bridge engine is not running.";
        return false;
      }
    }

    const int safeTimeoutMs = timeoutMs <= 0 ? 45000 : timeoutMs;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(safeTimeoutMs);

    auto pending = std::make_shared<PendingAnalysis>();
    pending->expectedResponseCount = expectedResponseCount <= 0 ? 1 : expectedResponseCount;
    {
      std::lock_guard<std::mutex> lock(pendingMutex);
      if (pendingAnalyses.find(requestId) != pendingAnalyses.end()) {
        error = "Request id is already pending: " + requestId;
        return false;
      }

      pendingAnalyses[requestId] = pending;
    }

    input->pushLine(requestJson);

    while (std::chrono::steady_clock::now() < deadline) {
      {
        std::unique_lock<std::mutex> lock(pendingMutex);
        if (pending->failed) {
          error = pending->error;
          pendingAnalyses.erase(requestId);
          return false;
        }
        if (pending->completed) {
          responsesJson = buildResponsesArrayJson(pending->responseJsons);
          pendingAnalyses.erase(requestId);
          return true;
        }
      }

      int waitMs = millisecondsUntil(deadline);
      waitMs = waitMs <= 0 ? 1 : (waitMs > 100 ? 100 : waitMs);

      if (!tryDrainOutputLine(waitMs, error)) {
        if (stopped.load()) {
          if (error.empty()) {
            error = "KataGo bridge engine stopped before returning a result.";
          }
          std::string diagnostics = buildDiagnosticsText();
          if (!diagnostics.empty()) {
            error += " Recent KataGo output: " + diagnostics;
          }
          removePendingAnalysis(requestId);
          return false;
        }
      }

      {
        std::unique_lock<std::mutex> lock(pendingMutex);
        if (!pending->completed) {
          pending->completedCv.wait_for(lock, std::chrono::milliseconds(1));
        }
        if (pending->failed) {
          error = pending->error;
          pendingAnalyses.erase(requestId);
          return false;
        }
        if (pending->completed) {
          responsesJson = buildResponsesArrayJson(pending->responseJsons);
          pendingAnalyses.erase(requestId);
          return true;
        }
      }
    }

    removePendingAnalysis(requestId);
    error = "KataGo bridge analyze timed out.";
    return false;
  }

  void stop() {
    {
      std::lock_guard<std::mutex> lock(stateMutex);
      if (!started) {
        return;
      }
      stopped.store(true);
      if (input) {
        input->stop();
      }
    }

    if (worker.joinable()) {
      worker.join();
    }

    if (output) {
      output->stop();
    }

    {
      std::lock_guard<std::mutex> lock(pendingMutex);
      for (auto& item : pendingAnalyses) {
        if (item.second) {
          item.second->failed = true;
          item.second->error = "KataGo bridge engine stopped before returning a result.";
          item.second->completedCv.notify_all();
        }
      }
      pendingAnalyses.clear();
    }

    std::lock_guard<std::mutex> lock(stateMutex);
    started = false;
    input.reset();
    output.reset();
  }

 private:
  static int resolveExpectedResponseCount(const json& request) {
    if (request.contains("analyzeTurns") && request["analyzeTurns"].is_array()) {
      int count = static_cast<int>(request["analyzeTurns"].size());
      return count <= 0 ? 1 : count;
    }

    return 1;
  }

  static std::string buildResponsesArrayJson(const std::vector<std::string>& responseJsons) {
    json responses = json::array();
    for (const std::string& responseJson : responseJsons) {
      try {
        responses.push_back(json::parse(responseJson));
      }
      catch (const std::exception&) {
      }
    }

    return responses.dump();
  }

  static int millisecondsUntil(const std::chrono::steady_clock::time_point& deadline) {
    return static_cast<int>(std::chrono::duration_cast<std::chrono::milliseconds>(deadline - std::chrono::steady_clock::now()).count());
  }

  bool tryDrainOutputLine(int waitMs, std::string& error) {
    std::unique_lock<std::mutex> drainLock(outputDrainMutex, std::try_to_lock);
    if (!drainLock.owns_lock()) {
      return true;
    }

    std::string line;
    if (!output->waitPopLine(line, waitMs)) {
      if (stopped.load()) {
        error = "KataGo bridge engine stopped before returning a result.";
        return false;
      }

      return true;
    }

    dispatchOutputLine(line, error);
    return true;
  }

  void dispatchOutputLine(const std::string& line, std::string& error) {
    if (line.empty() || line[0] != '{') {
      rememberDiagnosticLine(line);
      return;
    }

    try {
      json response = json::parse(line);
      if (!response.contains("id") || !response["id"].is_string()) {
        rememberDiagnosticLine(line);
        return;
      }

      if (response.contains("isDuringSearch") && response["isDuringSearch"].is_boolean() && response["isDuringSearch"].get<bool>()) {
        return;
      }

      std::string responseId = response["id"].get<std::string>();
      std::lock_guard<std::mutex> lock(pendingMutex);
      auto pending = pendingAnalyses.find(responseId);
      if (pending == pendingAnalyses.end() || !pending->second) {
        return;
      }

      pending->second->responseJsons.push_back(line);
      if (response.contains("error") || static_cast<int>(pending->second->responseJsons.size()) >= pending->second->expectedResponseCount) {
        pending->second->completed = true;
        pending->second->completedCv.notify_all();
      }
    }
    catch (const std::exception& ex) {
      if (error.empty()) {
        error = std::string("Could not parse KataGo output line: ") + ex.what();
      }
      rememberDiagnosticLine(line);
    }
  }

  void removePendingAnalysis(const std::string& requestId) {
    std::lock_guard<std::mutex> lock(pendingMutex);
    pendingAnalyses.erase(requestId);
  }

  void runAnalysisThread() {
    std::streambuf* oldCin = nullptr;
    std::streambuf* oldCout = nullptr;
    std::streambuf* oldCerr = nullptr;

    {
      std::lock_guard<std::mutex> lock(globalStreamMutex);
      oldCin = std::cin.rdbuf(input.get());
      oldCout = std::cout.rdbuf(output.get());
      oldCerr = std::cerr.rdbuf(output.get());
    }

#if defined(__ANDROID__) && defined(USE_OPENCL_BACKEND) && defined(KATAGO_BRIDGE_ANDROID_OPENCL_DIAGNOSTICS)
    weiqixn_bridge_android_opencl_diag_log("analysis thread entering MainCmds::analysis");
#endif
    int result = 1;
    try {
      result = MainCmds::analysis(args);
    }
    catch (const std::exception& ex) {
      std::cerr << "KataGo bridge uncaught exception: " << ex.what() << std::endl;
    }
    catch (...) {
      std::cerr << "KataGo bridge uncaught non-standard exception." << std::endl;
    }

    {
      std::lock_guard<std::mutex> lock(globalStreamMutex);
      std::cin.rdbuf(oldCin);
      std::cout.rdbuf(oldCout);
      std::cerr.rdbuf(oldCerr);
    }

#if defined(__ANDROID__) && defined(USE_OPENCL_BACKEND) && defined(KATAGO_BRIDGE_ANDROID_OPENCL_DIAGNOSTICS)
    weiqixn_bridge_android_opencl_diag_log(std::string("analysis thread exited result=").append(std::to_string(result)).c_str());
#endif
    exitCode.store(result);
    stopped.store(true);
    if (output) {
      output->stop();
    }

    std::lock_guard<std::mutex> lock(pendingMutex);
    for (auto& item : pendingAnalyses) {
      if (item.second) {
        item.second->failed = true;
        item.second->error = "KataGo bridge engine stopped before returning a result.";
        item.second->completedCv.notify_all();
      }
    }
  }

  void rememberDiagnosticLine(const std::string& line) {
    std::string value = trimCopy(line);
    if (value.empty()) {
      return;
    }

    const size_t maxLineLength = 500;
    if (value.size() > maxLineLength) {
      value = value.substr(0, maxLineLength) + "...";
    }

    std::lock_guard<std::mutex> lock(diagnosticMutex);
    const size_t maxLineCount = 12;
    while (recentDiagnostics.size() >= maxLineCount) {
      recentDiagnostics.pop_front();
    }
    recentDiagnostics.push_back(value);
  }

  std::string buildDiagnosticsText() {
    std::lock_guard<std::mutex> lock(diagnosticMutex);
    std::string result;
    for (const std::string& line : recentDiagnostics) {
      if (!result.empty()) {
        result += " | ";
      }
      result += line;
    }

    return result;
  }

  static std::mutex globalStreamMutex;

  std::mutex stateMutex;
  std::mutex diagnosticMutex;
  std::mutex pendingMutex;
  std::mutex outputDrainMutex;
  std::unique_ptr<BlockingInputStreamBuf> input;
  std::unique_ptr<LineOutputStreamBuf> output;
  std::unordered_map<std::string, std::shared_ptr<PendingAnalysis>> pendingAnalyses;
  std::deque<std::string> recentDiagnostics;
  std::thread worker;
  std::vector<std::string> args;
  std::string workDir;
  std::atomic<bool> stopped{false};
  std::atomic<int> exitCode{-1};
  bool started = false;
};

std::mutex KataGoBridgeEngine::globalStreamMutex;

void writeError(char* buffer, int bufferSize, const std::string& message) {
  if (buffer == nullptr || bufferSize <= 0) {
    return;
  }

  int copyCount = static_cast<int>(message.size());
  if (copyCount >= bufferSize) {
    copyCount = bufferSize - 1;
  }
  if (copyCount > 0) {
    std::memcpy(buffer, message.data(), static_cast<size_t>(copyCount));
  }
  buffer[copyCount] = '\0';
}

char* duplicateCString(const std::string& value) {
  char* result = new char[value.size() + 1];
  std::memcpy(result, value.c_str(), value.size() + 1);
  return result;
}

}  // namespace

KG_EXPORT int kg_create_engine(
    const char* configPath,
    const char* modelPath,
    const char* workingDirectory,
    void** outEngine,
    char* errorBuffer,
    int errorBufferSize) {
  if (outEngine == nullptr) {
    writeError(errorBuffer, errorBufferSize, "outEngine is null.");
    return 0;
  }

  auto engine = std::make_unique<KataGoBridgeEngine>();
  std::string error;
  if (!engine->start(configPath, modelPath, nullptr, workingDirectory, error)) {
    writeError(errorBuffer, errorBufferSize, error);
    return 0;
  }

  *outEngine = engine.release();
  writeError(errorBuffer, errorBufferSize, "");
  return 1;
}

KG_EXPORT int kg_create_engine_with_human_model(
    const char* configPath,
    const char* modelPath,
    const char* humanSlModelPath,
    const char* workingDirectory,
    void** outEngine,
    char* errorBuffer,
    int errorBufferSize) {
  if (outEngine == nullptr) {
    writeError(errorBuffer, errorBufferSize, "outEngine is null.");
    return 0;
  }

  auto engine = std::make_unique<KataGoBridgeEngine>();
  std::string error;
  if (!engine->start(configPath, modelPath, humanSlModelPath, workingDirectory, error)) {
    writeError(errorBuffer, errorBufferSize, error);
    return 0;
  }

  *outEngine = engine.release();
  writeError(errorBuffer, errorBufferSize, "");
  return 1;
}

KG_EXPORT int kg_analyze(
    void* enginePtr,
    const char* requestJson,
    int timeoutMs,
    char** outResponseJson,
    char* errorBuffer,
    int errorBufferSize) {
  if (outResponseJson == nullptr) {
    writeError(errorBuffer, errorBufferSize, "outResponseJson is null.");
    return 0;
  }
  *outResponseJson = nullptr;

  auto* engine = static_cast<KataGoBridgeEngine*>(enginePtr);
  if (engine == nullptr) {
    writeError(errorBuffer, errorBufferSize, "engine is null.");
    return 0;
  }

  std::string response;
  std::string error;
  if (!engine->analyze(requestJson, timeoutMs, response, error)) {
    writeError(errorBuffer, errorBufferSize, error);
    return 0;
  }

  *outResponseJson = duplicateCString(response);
  writeError(errorBuffer, errorBufferSize, "");
  return 1;
}

KG_EXPORT int kg_analyze_many(
    void* enginePtr,
    const char* requestJson,
    int timeoutMs,
    char** outResponsesJson,
    char* errorBuffer,
    int errorBufferSize) {
  if (outResponsesJson == nullptr) {
    writeError(errorBuffer, errorBufferSize, "outResponsesJson is null.");
    return 0;
  }
  *outResponsesJson = nullptr;

  auto* engine = static_cast<KataGoBridgeEngine*>(enginePtr);
  if (engine == nullptr) {
    writeError(errorBuffer, errorBufferSize, "engine is null.");
    return 0;
  }

  std::string responses;
  std::string error;
  if (!engine->analyzeMany(requestJson, timeoutMs, responses, error)) {
    writeError(errorBuffer, errorBufferSize, error);
    return 0;
  }

  *outResponsesJson = duplicateCString(responses);
  writeError(errorBuffer, errorBufferSize, "");
  return 1;
}

KG_EXPORT void kg_free_string(char* value) {
  delete[] value;
}

KG_EXPORT void kg_destroy_engine(void* enginePtr) {
  auto* engine = static_cast<KataGoBridgeEngine*>(enginePtr);
  delete engine;
}

#if defined(USE_EIGEN_BACKEND)
KG_EXPORT const char* kg_get_bridge_backend() {
  return "eigen";
}
#elif defined(USE_OPENCL_BACKEND)
KG_EXPORT const char* kg_get_bridge_backend() {
  return "opencl";
}
#else
KG_EXPORT const char* kg_get_bridge_backend() {
  return "dummy";
}
#endif

KG_EXPORT int kg_supports_concurrent_analyze() {
  return 1;
}

KG_EXPORT int kg_supports_analyze_many() {
  return 1;
}
