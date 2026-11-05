# Smart CCTV Mini

Deteksi **APD (v2)** dan **vehicle violation** pada 2 kamera, dengan aplikasi web sesuai mockup:
dashboard, halaman APD/Vehicle, kasus, laporan, pengaturan kamera, dan manajemen user.

Tiga bagian, masing-masing di folder sendiri dengan `docker-compose.yml` sendiri:

| Folder | Isi | Port |
|---|---|---|
| `sqlserver/` | SQL Server 2022 + migration skema (Alembic) | 127.0.0.1:14330 |
| `web/` | Web ASP.NET MVC 5 (.NET Framework 4.8, berjalan di Mono). Satu-satunya yang mengakses database | 8091 |
| `fastapi/` | Layanan AI: inferensi + stream video MJPEG | 8090 |

Desain lengkap ada di [ARSITEKTUR.md](ARSITEKTUR.md).

## Jalankan (satu per satu, urut)

Sekali saja, buat `.env` ketiga folder dengan rahasia yang saling cocok:

```bash
sh deploy/init-env.sh
```

Skrip mencetak password admin web awal. Kamera bisa langsung diisi di `fastapi/.env`
(`CCTV_1_URL`, ...), atau ditambahkan nanti lewat menu **Pengaturan**.

```bash
# 1. Database + migration (membuat jaringan Docker `smartcctv_mini`)
cd sqlserver && docker compose up -d && cd ..

# 2. Web
cd web && docker compose up -d && cd ..

# 3. AI. GPU (server A100):
cd fastapi && docker compose -f docker-compose.yml -f docker-compose.gpu.yml up -d --build && cd ..
#    tanpa GPU (CPU):
# cd fastapi && docker compose up -d --build && cd ..
```

Buka `http://<ip-server>:8091` dan login sebagai `admin`. Model AI siap ±1–2 menit setelah
layanan `fastapi` menyala.

Cek hasil migration: `docker logs smartcctv_mini_migrate`. Baris terakhir harus "Akun admin awal
dibuat" (instalasi pertama) atau "Users sudah ada".

## Server A100 (offline)

Server tidak punya internet. Image `web` dan `migrate` di-build di laptop, lalu dikirim:

```bash
# di laptop
(cd sqlserver && docker compose build) && (cd web && docker compose build)
sh deploy/make-bundle.sh          # -> dist/mini-images.tar.gz + dist/smart-cctv-mini-src.tar.gz
```

Di server: `sudo docker load -i mini-images.tar.gz`, ekstrak source, lalu jalankan tiga langkah di atas.
Image SQL Server (`mcr.microsoft.com/mssql/server:2022-latest`) dan image AI
(`fs-smartcctv-pep-demo-live:bundle-gpu`) sudah ada di server. `fastapi` di-build offline di atas
image AI itu.

## Pengaturan di .env

**Database** (`web/.env`): `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, `DB_PASSWORD`.
- Defaultnya container `sqlserver`.
- Untuk SQL Server lain, isi juga `DB_*` di `sqlserver/.env` supaya migration dijalankan ke server
  itu: `cd sqlserver && docker compose up -d migrate`.

**Foto bukti** (`web/.env`):
- `EVIDENCE_STORAGE=db` (default): foto disimpan di database.
- Ke NAS (SMB): buka komentar baris berikut dan isi `NAS_*`:

```
COMPOSE_PATH_SEPARATOR=:
COMPOSE_FILE=docker-compose.yml:docker-compose.nas.yml
NAS_SHARE=//r2folder.pep.pertamina.com/share/BS/IT/IT_DAMA/_All/14_PROJECT/CCTV AI/CCTV_EVIDENCE
NAS_ADDR=10.12.2.90
NAS_DOMAIN=PERTAMINA
NAS_USER=<akun dengan hak TULIS ke folder itu>
NAS_PASSWORD=<password akun itu>
```

Lalu jalankan `cd web && docker compose up -d`. Cek dengan `curl http://127.0.0.1:8091/api/health`,
hasilnya harus `"evidence":"file ok"`.

Ketentuan:
- Foto tersimpan di NAS sebagai `yyyy/MM/dd/<kategori>-cam<n>-<jam>-<id>.jpg`.
- Bila NAS tidak bisa ditulisi, event tertahan di antrean AI dan dikirim ulang otomatis.
- Mengganti `NAS_*` setelahnya:
  `docker compose down && docker volume rm smartcctv_mini_evidence_nas`, lalu `up -d` lagi.
- Foto lama yang sudah ada di database tetap tampil.

## Perintah lain

| Keperluan | Perintah (di folder terkait) |
|---|---|
| Log | `docker logs -f smartcctv_mini_web` / `smartcctv_mini_api` / `smartcctv_mini_sql` |
| Hentikan | `docker compose down` (data DB tetap di volume) |
| Migration baru | tambah file di `sqlserver/migrate/migrations/versions/`, lalu `cd sqlserver && docker compose up -d --build migrate` |
| Ubah kamera | menu **Pengaturan**. AI memuat ulang sendiri dalam ±15 dtk |
