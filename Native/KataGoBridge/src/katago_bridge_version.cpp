#include "main.h"

#include <sstream>

std::string Version::getKataGoVersion() {
  return std::string("1.16.5");
}

std::string Version::getKataGoVersionForHelp() {
  return std::string("KataGo v1.16.5");
}

std::string Version::getKataGoVersionFullInfo() {
  std::ostringstream out;
  out << Version::getKataGoVersionForHelp() << std::endl;
  out << "Git revision: " << Version::getGitRevision() << std::endl;
  out << "Compile Time: " << __DATE__ << " " << __TIME__ << std::endl;
#if defined(USE_EIGEN_BACKEND)
  out << "Using Eigen(CPU) backend" << std::endl;
#elif defined(USE_OPENCL_BACKEND)
  out << "Using OpenCL backend" << std::endl;
#else
  out << "Using dummy backend" << std::endl;
#endif
  return out.str();
}

std::string Version::getGitRevision() {
  return std::string("<omitted>");
}

std::string Version::getGitRevisionWithBackend() {
#if defined(USE_EIGEN_BACKEND)
  return std::string("<omitted>-eigen");
#elif defined(USE_OPENCL_BACKEND)
  return std::string("<omitted>-opencl");
#else
  return std::string("<omitted>-dummy");
#endif
}
