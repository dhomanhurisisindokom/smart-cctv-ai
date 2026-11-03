#!/bin/sh
# Creates sqlserver/.env, web/.env and fastapi/.env with matching, freshly generated secrets.
# Existing .env files are never overwritten. Run once from the repo root: sh deploy/init-env.sh
# Optional: CAMERA_ENV=/path/to/old/.env copies CCTV_1_* / CCTV_2_* lines into fastapi/.env.
set -eu
cd "$(dirname "$0")/.."
for f in sqlserver/.env web/.env fastapi/.env; do
  if [ -e "$f" ]; then echo "$f sudah ada; tidak diubah. Hapus dulu bila ingin membuat ulang."; exit 1; fi
done
rand() { openssl rand -hex "$1"; }
SA="Sa-$(rand 12)!9a"
ADMIN="Admin-$(rand 6)!7b"
INGEST=$(rand 32)
STREAM=$(rand 32)
umask 077
printf 'MSSQL_SA_PASSWORD=%s\nMINI_DB=smartcctv_mini\nMINI_ADMIN_USERNAME=admin\nMINI_ADMIN_PASSWORD=%s\n' "$SA" "$ADMIN" > sqlserver/.env
printf 'MSSQL_SA_PASSWORD=%s\nMINI_DB=smartcctv_mini\nMINI_INGEST_TOKEN=%s\nMINI_STREAM_KEY=%s\n' "$SA" "$INGEST" "$STREAM" > web/.env
printf 'MINI_INGEST_TOKEN=%s\nMINI_STREAM_KEY=%s\n' "$INGEST" "$STREAM" > fastapi/.env
if [ -n "${CAMERA_ENV:-}" ]; then
  grep -E '^CCTV_[12]_(URL|NAME|AI_ENABLED|APD_MASK|APD_PENUTUP|VEHICLE_PROFILE)=' "$CAMERA_ENV" >> fastapi/.env || true
else
  cat >> fastapi/.env <<'EOF'
# Isi kamera di sini (dipakai sekali saat instalasi pertama) atau tambahkan lewat menu Pengaturan.
# CCTV_1_URL='rtsp://user:password@ip:554/Streaming/Channels/101'
# CCTV_1_NAME='CCTV 1'
EOF
fi
echo "Selesai. Login web pertama: admin / $ADMIN"
echo "(tersimpan juga di sqlserver/.env; ganti setelah login lewat Manajemen User)"
