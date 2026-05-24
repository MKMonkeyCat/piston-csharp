#!/bin/bash
if [[ "$*" == *"--run"* ]]; then
  before_args=()
  after_args=()
  found_divider=false
  for arg in "$@"; do
    if [ "$found_divider" = true ]; then
      after_args+=("$arg")
    else
      if [[ "$arg" == "--" ]]; then
        found_divider=true
      else
        before_args+=("$arg")
      fi
    fi
  done
  exec /usr/local/bin/isolate.real --dir=/usr/bin --dir=/usr/lib --dir=/lib --dir=/lib64 "${before_args[@]}" -- /usr/local/bin/piston-guard "${after_args[@]}"
else
  exec /usr/local/bin/isolate.real "$@"
fi
