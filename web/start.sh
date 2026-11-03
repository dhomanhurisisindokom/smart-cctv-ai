#!/bin/sh
# xsp4 stops on EOF from stdin, so feed it an endless one. Then compile every Razor view once
# (/Warmup) so the first visitor does not wait for Mono's view compiler.
tail -f /dev/null | xsp4 --port 8080 --address 0.0.0.0 --nonstop &
XSP=$!
for i in $(seq 1 120); do
  if curl -fsS -m 300 http://127.0.0.1:8080/Warmup >/dev/null 2>&1; then echo "Views warmed up"; break; fi
  sleep 1
done
wait "$XSP"
