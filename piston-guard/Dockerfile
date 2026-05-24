FROM ghcr.io/engineer-man/piston:latest

COPY piston-guard.cpp /tmp/piston-guard.cpp
RUN g++ -O3 /tmp/piston-guard.cpp -o /usr/local/bin/piston-guard && \
  chmod 755 /usr/local/bin/piston-guard && \
  rm /tmp/piston-guard.cpp

RUN mv /usr/local/bin/isolate /usr/local/bin/isolate.real && \
  chmod +s /usr/local/bin/isolate.real

COPY ./piston-guard.sh /usr/local/bin/isolate
RUN chmod 755 /usr/local/bin/isolate
