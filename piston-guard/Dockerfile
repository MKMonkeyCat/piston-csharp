FROM rust:1.93 AS builder

RUN rustup target add x86_64-unknown-linux-musl

WORKDIR /usr/src/piston-guard

COPY . .
RUN cargo build --release --target x86_64-unknown-linux-musl

FROM ghcr.io/engineer-man/piston:latest

RUN mv /usr/local/bin/isolate /usr/local/bin/isolate.real && \
  chmod +s /usr/local/bin/isolate.real

COPY --from=builder /usr/src/piston-guard/target/x86_64-unknown-linux-musl/release/piston-guard /usr/local/bin/piston-guard
COPY ./piston-guard.sh /usr/local/bin/isolate

RUN chmod 755 /usr/local/bin/piston-guard /usr/local/bin/isolate
