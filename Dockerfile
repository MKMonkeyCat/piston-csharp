FROM ghcr.io/engineer-man/piston:latest

COPY piston-guard.cpp /tmp/piston-guard.cpp
RUN g++ -O3 /tmp/piston-guard.cpp -o /usr/local/bin/piston-guard && \
  chmod 755 /usr/local/bin/piston-guard && \
  rm /tmp/piston-guard.cpp

RUN mv /usr/local/bin/isolate /usr/local/bin/isolate.real && \
  chmod +s /usr/local/bin/isolate.real

RUN echo '#!/bin/bash\n\
  if [[ "$*" == *"--run"* ]]; then\n\
  before_args=()\n\
  after_args=()\n\
  found_divider=false\n\
  for arg in "$@"; do\n\
  if [ "$found_divider" = true ]; then\n\
  after_args+=("$arg")\n\
  else\n\
  if [[ "$arg" == "--" ]]; then\n\
  found_divider=true\n\
  else\n\
  before_args+=("$arg")\n\
  fi\n\
  fi\n\
  done\n\
  exec /usr/local/bin/isolate.real --dir=/usr/bin --dir=/usr/lib --dir=/lib --dir=/lib64 "${before_args[@]}" -- /usr/local/bin/piston-guard "${after_args[@]}"\n\
  else\n\
  exec /usr/local/bin/isolate.real "$@"\n\
  fi' > /usr/local/bin/isolate && chmod 755 /usr/local/bin/isolate
