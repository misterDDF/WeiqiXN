#include "main.h"
#include "external/nlohmann_json/json.hpp"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstring>
#include <deque>
#include <iostream>
#include <memory>
#include <mutex>
#include <sstream>
#include <streambuf>
#include <string>
#include <thread>
#include <vector>

using json = nlohmann::json;

#if defined(_WIN32)
#define KG_EXPORT extern "C" __declspec(dllexport)
#else
#define KG_EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace {

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
 public:
  ~KataGoBridgeEngine() {
    stop();
  }

  bool start(const char* configPath, const char* modelPath, const char* workingDirectory, std::string& error) {
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

    args.clear();
    args.push_back("analysis");
    args.push_back("-config");
    args.push_back(configPath);
    args.push_back("-model");
    args.push_back(modelPath);
    args.push_back("-quit-without-waiting");

    input = std::make_unique<BlockingInputStreamBuf>();
    output = std::make_unique<LineOutputStreamBuf>();
    stopped.store(false);
    exitCode.store(-1);
    workDir = workingDirectory == nullptr ? std::string() : std::string(workingDirectory);

    worker = std::thread([this]() { runAnalysisThread(); });
    started = true;
    return true;
  }

  bool analyze(const char* requestJson, int timeoutMs, std::string& responseJson, std::string& error) {
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
    input->pushLine(requestJson);

    while (std::chrono::steady_clock::now() < deadline) {
      int waitMs = static_cast<int>(std::chrono::duration_cast<std::chrono::milliseconds>(deadline - std::chrono::steady_clock::now()).count());
      waitMs = waitMs <= 0 ? 1 : (waitMs > 100 ? 100 : waitMs);

      std::string line;
      if (!output->waitPopLine(line, waitMs)) {
        if (stopped.load()) {
          error = "KataGo bridge engine stopped before returning a result.";
          return false;
        }
        continue;
      }

      if (line.empty() || line[0] != '{') {
        continue;
      }

      try {
        json response = json::parse(line);
        if (!response.contains("id") || !response["id"].is_string() || response["id"].get<std::string>() != requestId) {
          continue;
        }

        if (response.contains("isDuringSearch") && response["isDuringSearch"].is_boolean() && response["isDuringSearch"].get<bool>()) {
          continue;
        }

        responseJson = line;
        return true;
      }
      catch (const std::exception&) {
        continue;
      }
    }

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

    std::lock_guard<std::mutex> lock(stateMutex);
    started = false;
    input.reset();
    output.reset();
  }

 private:
  void runAnalysisThread() {
    std::streambuf* oldCin = nullptr;
    std::streambuf* oldCout = nullptr;

    {
      std::lock_guard<std::mutex> lock(globalStreamMutex);
      oldCin = std::cin.rdbuf(input.get());
      oldCout = std::cout.rdbuf(output.get());
    }

    int result = 1;
    try {
      (void)workDir;
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
    }

    exitCode.store(result);
    stopped.store(true);
    if (output) {
      output->stop();
    }
  }

  static std::mutex globalStreamMutex;

  std::mutex stateMutex;
  std::unique_ptr<BlockingInputStreamBuf> input;
  std::unique_ptr<LineOutputStreamBuf> output;
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
  if (!engine->start(configPath, modelPath, workingDirectory, error)) {
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

KG_EXPORT void kg_free_string(char* value) {
  delete[] value;
}

KG_EXPORT void kg_destroy_engine(void* enginePtr) {
  auto* engine = static_cast<KataGoBridgeEngine*>(enginePtr);
  delete engine;
}
