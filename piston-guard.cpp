#include <iostream>
#include <vector>
#include <fstream>
#include <unistd.h>
#include <sys/wait.h>
// #include <cstring>

using namespace std;

static string read_file(const string &path) {
  ifstream ifs(path, ios::in | ios::binary);
  if (!ifs.is_open()) return "";

  return string((istreambuf_iterator<char>(ifs)), istreambuf_iterator<char>());
}

static void write_all(int fd, const char *buf, size_t len) {
  size_t written = 0;
  while (written < len) {
    ssize_t r = write(fd, buf + written, len - written);
    if (r < 0) {
      if (errno == EINTR) continue;
      return;
    }
    if (r == 0) return;
    written += (size_t)r;
  }
}

int main(int argc, char *argv[]) {
  if (argc < 2) return 1;

  const string checker_path = ".checker.py";
  const string checker_code = read_file(checker_path);

  unlink(checker_path.c_str());

  pid_t user_pid = fork();
  if (user_pid == 0) {
    vector<char *> args;
    args.push_back(argv[1]);
    for (int i = 2; i < argc; i++) {
      args.push_back(argv[i]);
    }
    args.push_back(nullptr);

    std::cout << "Executing: " << args[0];
    for (size_t i = 1; i < args.size() - 1; i++) {
      std::cout << " " << args[i];
    }
    std::cout << std::endl;
    execv("/bin/bash", args.data());

    // cerr << "exec failed: " << strerror(errno) << endl;
    _exit(127);
  }

  int status = 0;
  while (waitpid(user_pid, &status, 0) == -1 && errno == EINTR);

  if (!checker_code.empty()) {
    int pipefd[2];  // pipefd[0] for reading, pipefd[1] for writing
    if (pipe(pipefd) == 0) {
      pid_t chk = fork();
      if (chk == 0) {
        dup2(pipefd[0], STDIN_FILENO);
        close(pipefd[0]);
        close(pipefd[1]);

        char *const env[] = {(char *)"PATH=/usr/bin:/bin", (char *)"HOME=/tmp", (char *)"PYTHONPATH=", nullptr};

        execle("/usr/bin/python3", "python3", "-u", "-", nullptr, env);

        _exit(127);
      }

      close(pipefd[0]);
      write_all(pipefd[1], checker_code.data(), checker_code.size());
      close(pipefd[1]);
      waitpid(chk, nullptr, 0);
    }
  }

  if (WIFEXITED(status)) return WEXITSTATUS(status);
  if (WIFSIGNALED(status)) return 128 + WTERMSIG(status);

  return 1;
}
