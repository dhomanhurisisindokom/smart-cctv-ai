#!/bin/sh
# Run on a machine with internet after building web and sqlserver images:
#   (cd sqlserver && docker compose build) && (cd web && docker compose build) && sh deploy/make-bundle.sh
# Produces dist/ with everything the offline server needs besides the images it already has
# (mcr.microsoft.com/mssql/server:2022-latest and the AI image fs-smartcctv-pep-demo-live:bundle-gpu).
set -eu
cd "$(dirname "$0")/.."
mkdir -p dist
docker save smartcctv-mini-web:latest smartcctv-mini-migrate:latest | gzip > dist/mini-images.tar.gz
git archive --format=tar.gz --prefix=smart-cctv-mini/ -o dist/smart-cctv-mini-src.tar.gz HEAD
( cd dist && sha256sum mini-images.tar.gz smart-cctv-mini-src.tar.gz > SHA256SUMS )
ls -lh dist
