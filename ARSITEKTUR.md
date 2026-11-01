# Smart CCTV Mini — Stack & Arsitektur

Web inference sederhana: model **APD detection v2** dan **vehicle violation** dari project utama
dijalankan pada 2 kamera, hasilnya ditampilkan langsung di browser. Tanpa database, login,
snapshot, laporan, atau playback.

- Server A100: `http://10.12.21.10:8090`
- Folder di server: `~/smart-cctv-mini`
- Folder di laptop: `Documents/smart-cctv-mini`

---

## 1. Stack

| Lapisan | Teknologi |
|---|---|
| Web server + API | FastAPI + Uvicorn (Python 3.12) — `mini/main.py` |
| Tampilan | 1 halaman HTML + JavaScript biasa, tanpa framework — `mini/static/index.html` |
| Video ke browser | MJPEG (`multipart/x-mixed-replace`), dibaca langsung oleh tag `<img>` |
| Ambil video kamera | OpenCV + FFmpeg, RTSP lewat TCP |
| AI | PyTorch 2.6 + CUDA 12.4: paket APD detection v2 dan vehicle violation (RT-DETR, pose) |
| Kontainer | Docker Compose; image dibangun di atas `fs-smartcctv-pep-demo-live:bundle-gpu` |
| GPU | A100 MIG 3g.40gb lewat NVIDIA Container Toolkit (`count: all`) |

Hanya **1 container** (`smartcctv_mini`). Tidak ada SQL Server, .NET/Mono, nginx, maupun mediamtx.

---

## 2. Arsitektur

```
 Kamera 1 (RTSP) ──► thread pembaca 1 ─┐   simpan hanya frame terbaru
 Kamera 2 (RTSP) ──► thread pembaca 2 ─┤   (frame lama dibuang, tidak antre)
                                       ▼
                         1 thread inferensi (GPU)
                         bergantian kam 1 → kam 2 → kam 1 ...
                           ├─ APD v2 : detektor orang + pose + cek helm / kacamata /
                           │           sarung tangan / wearpack + voting + alarm
                           └─ Vehicle: RT-DETR (jalan paralel dengan APD)
                         → gambar kotak + label → JPEG
                                       ▼
                         hasil terakhir per kamera & mode (di memori)
                                       ▼
            FastAPI ──  /                              halaman web
                    ──  /stream/{kam}/{apd|vehicle}    MJPEG, dikirim begitu ada frame baru
                    ──  /api/status                    status model & kamera (polling 1 dtk)
                                       ▼
                               Browser (4 panel)
```

### Alur singkat

1. Satu thread per kamera membaca RTSP terus-menerus dan menyimpan **hanya frame terbaru**.
2. Satu thread inferensi mengambil frame terbaru tiap kamera secara bergantian, menjalankan APD
   dan vehicle, menggambar kotak, lalu menyimpan hasilnya sebagai JPEG di memori.
3. Browser membuka 4 stream MJPEG (kamera 1/2 × APD/vehicle). Server langsung mengirim JPEG baru
   setiap kali inferensi menghasilkan satu.
4. Status (model siap, kamera tersambung, sampel/detik, ms/sampel, pelanggaran) diambil tiap detik
   dari `/api/status`.

---

## 3. Apa yang sama dan apa yang dibuang dibanding project utama

**Sama persis**

- Kode AI dan runtime: `app.demo_live.runtime` dan `app.ai` dipakai langsung dari image project
  utama, tidak diubah. Model, ambang, aturan, voting, dan mask identik → hasil deteksi sama.
- Nama variabel `.env` (`CCTV_1_URL`, `CCTV_2_URL`, `CCTV_n_AI_ENABLED`, `CCTV_n_APD_MASK`, ...).

**Dibuang**

| Komponen project utama | Di mini |
|---|---|
| SQL Server + API backend | Tidak ada; event tidak disimpan (`DETECTION_PERSIST_ENABLED=false`) |
| Frontend .NET/Mono | Diganti 1 halaman HTML dari FastAPI |
| nginx | Tidak perlu; browser langsung ke FastAPI |
| mediamtx (video mentah) | Tidak ada; hanya hasil AI yang ditampilkan |
| Login, snapshot, laporan, playback, retensi | Tidak ada |

Jalur gambar jadi jauh lebih pendek:

- Project utama: demo-live → backend/frontend (Mono) → nginx → browser (polling)
- Mini: proses AI → browser (1 koneksi stream)

---

## 4. Struktur file

```
smart-cctv-mini/
├── Dockerfile               FROM image AI project utama + COPY mini/
├── docker-compose.yml       service `mini`, port 8090, variabel kamera
├── docker-compose.gpu.yml   CUDA, interval sampel 0.12 dtk, akses GPU
├── .env.example             contoh isi .env
├── mini/
│   ├── main.py              FastAPI: halaman, MJPEG stream, status (~80 baris)
│   └── static/index.html    tampilan web
├── README.md                cara menjalankan
└── ARSITEKTUR.md            dokumen ini
```

---

## 5. Menjalankan

**Server A100** (butuh image `fs-smartcctv-pep-demo-live:bundle-gpu` dari bundle utama):

```bash
cd ~/smart-cctv-mini
cp ~/smart-cctv-bundle/.env .env
sudo docker compose -f docker-compose.yml -f docker-compose.gpu.yml up -d --build
```

Build offline, hanya beberapa detik. Model siap ±1–2 menit setelah container menyala.

**Laptop (CPU)**:

```bash
cp .env.example .env     # isi CCTV_1_URL / CCTV_2_URL
docker compose up -d --build
```

Perintah lain (di server, tambahkan `-f docker-compose.yml -f docker-compose.gpu.yml`):

| Keperluan | Perintah |
|---|---|
| Lihat log | `sudo docker logs -f smartcctv_mini` |
| Hentikan | `sudo docker compose down` |
| Terapkan perubahan `.env` | `sudo docker compose ... up -d` |

---

## 6. Performa & batasannya

Hasil terukur di A100: **±2,75 sampel/detik per kamera, ±221 ms per sampel** (naik-turun sampai
±450 ms saat GPU sibuk).

| Batas | Penjelasan |
|---|---|
| GPU dipakai bersama | Aplikasi lain (`~/apd-video-api`, port 8000) jalan di potongan GPU yang sama dan memproses kamera lain 5 fps terus-menerus. |
| Dua kamera bergantian | Satu jalur inferensi untuk 2 kamera (model dipakai bersama, tidak aman paralel). `CCTV_2_AI_ENABLED=false` → kamera 1 ±2× lebih cepat. |
| Potongan MIG 3g | ±3/7 tenaga komputasi A100 penuh. |
| CPU VM 8 core | Decode video, aturan APD, gambar kotak, encode JPEG jalan di CPU. |
| Laptop (CPU saja) | ±1,9 dtk per sampel → ±0,5 sampel/detik. |

Di GPU, `CCTV_SAMPLE_SECONDS` dari `.env` **tidak dipakai**; yang berlaku
`CCTV_SAMPLE_SECONDS_GPU` (bawaan 0,12 dtk).

FP16 atau batching sengaja **tidak** dipakai. Keduanya mengubah perhitungan model, jadi hasil
deteksi bisa bergeser dari project utama.

---

## 7. Catatan

- Kamera yang hanya menerima satu sesi bisa "direbut" bila live-inference project utama juga jalan.
  Hentikan dulu bila perlu: `sudo docker stop smartcctv_demo_live`.
- Kredensial kamera hanya ada di `.env` (tidak ikut git).
