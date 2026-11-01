# Smart CCTV Mini

Web inference sederhana: dua model dari project utama (APD detection v2 dan vehicle violation)
dijalankan pada 2 kamera dan hasilnya ditampilkan langsung di browser (MJPEG). Tanpa database,
snapshot, login, atau fitur lain.

AI-nya **sama persis** dengan project utama: image dibangun di atas image live-inference project
utama (`fs-smartcctv-pep-demo-live:bundle-gpu`), memakai `app.demo_live.runtime` dan `app.ai`
tanpa perubahan. Yang ditambahkan hanya folder `mini/` (FastAPI + satu halaman HTML).

## Jalankan di server A100 (offline)

Syarat: bundle project utama sudah di-install (image `fs-smartcctv-pep-demo-live:bundle-gpu` ada).

```bash
cd ~/smart-cctv-mini
cp ~/smart-cctv-bundle/.env .env      # nama variabel kamera sama
sudo docker compose -f docker-compose.yml -f docker-compose.gpu.yml up -d --build
```

Buka `http://<ip-server>:8090`.

Kamera yang hanya menerima satu sesi bisa "direbut" bila live-inference project utama juga jalan;
hentikan dulu bila perlu: `sudo docker stop smartcctv_demo_live`.

## Jalankan di laptop (CPU)

```bash
cp .env.example .env    # isi CCTV_1_URL / CCTV_2_URL
docker compose up -d --build
```

## Endpoint

- `/` halaman web
- `/stream/{1|2}/{apd|vehicle}` MJPEG hasil deteksi
- `/api/status` status model dan kamera
