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
printf 'MSSQL_SA_PASSWORD=%s\nDB_NAME=smartcctv_mini\nMINI_ADMIN_USERNAME=admin\nMINI_ADMIN_PASSWORD=%s\n' "$SA" "$ADMIN" > sqlserver/.env
printf 'DB_HOST=sqlserver\nDB_PORT=1433\nDB_NAME=smartcctv_mini\nDB_USER=sa\nDB_PASSWORD=%s\nMINI_INGEST_TOKEN=%s\nMINI_STREAM_KEY=%s\n' "$SA" "$INGEST" "$STREAM" > web/.env
cat >> web/.env <<'EOF'
# Foto bukti: db | file. Ke NAS: buka komentar 3 baris COMPOSE_* dan isi NAS_* (lihat .env.example).
EVIDENCE_STORAGE=db
# COMPOSE_PATH_SEPARATOR=:
# COMPOSE_FILE=docker-compose.yml:docker-compose.nas.yml
# NAS_SHARE=//r2folder.pep.pertamina.com/share/BS/IT/IT_DAMA/_All/14_PROJECT/CCTV AI/CCTV_EVIDENCE
# NAS_ADDR=10.12.2.90
# NAS_DOMAIN=PERTAMINA
# NAS_USER=
# NAS_PASSWORD=
EOF
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
