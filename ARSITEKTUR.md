# Smart CCTV Mini — Arsitektur

## 1. Gambaran

```
                       ┌──────────────────────── server ────────────────────────┐
 Browser ──halaman───────► web/  .NET 4.8 MVC (Mono)  :8091 ──SQL──► sqlserver/   │
    │                  │         - login, user, kamera, kasus,        SQL Server  │
    │                  │           catatan, laporan                   2022        │
    │                  │         - GET  /api/cameras   ◄──┐                       │
    │                  │         - POST /api/ingest    ◄──┤ token                 │
    │                  │                                  │                       │
    └──stream MJPEG──────► fastapi/  FastAPI + AI (GPU) :8090 ◄── RTSP kamera 1, 2 │
       (token dari web)│           - APD v2 + vehicle violation                   │
                       │           - outbox: event tersimpan di disk sampai       │
                       │             diterima web                                 │
                       └──────────────────────────────────────────────────────────┘
       semua container di jaringan Docker `smartcctv_mini`
```

**Prinsip: satu database, satu pemilik.** Hanya web (.NET) yang membaca dan menulis SQL Server.
Layanan AI tidak punya driver atau koneksi database. Ia mengirim pelanggaran ke web lewat API.

- Web bisa jalan walau AI sedang restart atau memuat model. Login, kasus, dan laporan tetap bisa dipakai.
- AI bisa restart kapan saja tanpa risiko merusak data.
- Skema hanya diubah lewat migration (folder `sqlserver/`).

## 2. Komponen

### sqlserver/

| Service | Isi |
|---|---|
| `sqlserver` | `mcr.microsoft.com/mssql/server:2022-latest`. Data di volume `mini_sql_data`. Port hanya 127.0.0.1:14330 |
| `migrate` | Jalan sekali lalu berhenti: `ensure_database.py` (buat DB bila belum ada) → `alembic upgrade head` → `seed.py` (akun admin awal bila tabel `users` kosong) |

Compose ini juga membuat jaringan `smartcctv_mini`; `web/` dan `fastapi/` bergabung sebagai external.

Skema (migration `0001`):

| Tabel | Isi |
|---|---|
| `users` | username, nama, email, role (`admin`/`supervisor`/`operator`), hash PBKDF2-SHA256, aktif, login terakhir |
| `cameras` | slot 1–2, nama, lokasi, RTSP URL, APD aktif, vehicle aktif, mask APD, seragam penutup, profil vehicle |
| `violations` | `event_key` unik (anti dobel), kamera, kategori, jenis, confidence, waktu deteksi (UTC), status kasus (`NULL`=alert, `baru`/`diproses`/`selesai`), penanggung jawab |
| `violation_evidence` | foto bukti JPEG. Dipisah supaya daftar tidak ikut membaca gambar |
| `case_notes` | catatan investigasi per kasus |

### web/ (.NET Framework 4.8)

- ASP.NET MVC 5 + Razor, data lewat ADO.NET (`System.Data.SqlClient`) dengan query berparameter.
- Di Linux berjalan di **Mono 5.18 + xsp4**. Image di-build dari `debian:buster` (paket arsip):
  NuGet MVC 5.2.9, lalu kode dikompilasi dengan `mcs`. `web/src/MiniWeb.csproj` tersedia untuk
  Visual Studio / IIS di Windows.
- Halaman sesuai mockup: Login, Dashboard, APD Detection, Vehicle Violation (kartu kamera live,
  popup Tampilan AI / Live, riwayat dengan filter, pager, unduh CSV), Case Violation, Detail kasus
  (foto bukti, status, penugasan, catatan, batalkan kasus), Laporan & Analitik (KPI, distribusi,
  tren per hari, per kamera, unduh CSV), Pengaturan kamera, dan Manajemen User.
- Keamanan:
  - login dengan session (8 jam);
  - menu Pengaturan dan User khusus admin;
  - anti-forgery token di semua form POST;
  - RTSP URL (berisi password) tidak pernah ditampilkan ulang;
  - CSV dinetralkan dari formula.
- Endpoint mesin-ke-mesin untuk AI (header `Authorization: Bearer MINI_INGEST_TOKEN`):
  - `GET /api/cameras`: konfigurasi kamera;
  - `POST /api/cameras/bootstrap`: instalasi pertama; kamera dari `fastapi/.env` disimpan bila tabel masih kosong;
  - `POST /api/ingest`: satu pelanggaran + foto. Idempoten berdasarkan `event_key`.

### fastapi/ (AI)

- Image = image AI (PyTorch 2.6 + CUDA 12.4, bobot model, kode APD v2 dan vehicle violation)
  + `mini/main.py`. Kode dan model AI tidak diubah, jadi hasil deteksinya identik.
- Saat start: ambil kamera dari web → set variabel `CCTV_*` → muat runtime AI. Setiap 15 detik
  konfigurasi dicek; bila berubah, proses keluar dan Docker menyalakannya lagi dengan setting baru.
- Pelanggaran yang terkonfirmasi dikirim oleh **outbox bawaan runtime**:
  - ditulis ke disk (volume `mini_outbox`);
  - dikirim ke `POST /api/ingest`, diulang sampai web menjawab sukses;
  - baru dihapus setelah web mengonfirmasi.

  Jadi bila web mati sebentar, tidak ada event yang hilang atau dobel.
- Stream untuk browser: `/stream/{kamera}/{apd|vehicle|raw}` (MJPEG) dan `/api/status`.
  - Keduanya wajib membawa token `?t=` yang dibuat web: HMAC-SHA256 dengan `MINI_STREAM_KEY`, berlaku 12 jam.
  - Video tidak lewat .NET, jadi kecepatannya sama dengan sebelumnya.

## 3. Alur

1. **Login** → web memeriksa hash password di tabel `users`.
2. **Halaman APD/Vehicle** → web membuat token stream → browser membuka
   `http://<server>:8090/stream/1/apd?t=...` langsung ke AI dan memanggil `/api/status` tiap 2 detik.
3. **AI mendeteksi pelanggaran** → outbox → `POST /api/ingest` → web menyimpan baris
   `violations` + `violation_evidence` → muncul di riwayat dan dashboard.
4. **Operator** membuka detail → "Jadikan Kasus" → status, penugasan, dan catatan disimpan web.
5. **Admin mengubah kamera** → web menyimpan ke `cameras` → AI membaca perubahan (≤15 dtk) →
   restart → model siap ±1–2 menit.

## 4. Rahasia (.env)

| Variabel | Ada di | Fungsi |
|---|---|---|
| `MSSQL_SA_PASSWORD` | sqlserver, web | login SQL Server |
| `MINI_ADMIN_PASSWORD` | sqlserver | password admin web awal (sekali) |
| `MINI_INGEST_TOKEN` | web, fastapi | AI ↔ web (`/api/*`) |
| `MINI_STREAM_KEY` | web, fastapi | tanda tangan token stream |
| `CCTV_n_*` | fastapi | kamera untuk instalasi pertama saja |

`sh deploy/init-env.sh` membuat ketiga `.env` dengan nilai acak yang cocok. File `.env` tidak masuk git.

## 5. Performa

- Kecepatan AI tidak berubah oleh frontend. Di A100 terukur ±3,7 sampel/detik per kamera (±130 ms per sampel).
- Laptop tanpa GPU: ±0,5 sampel/detik.
- Web hanya melayani halaman dan data. Video dikirim AI langsung ke browser.
- Foto bukti ±400 KB per pelanggaran disimpan di database. Untuk pemakaian lama, perlu dipikirkan
  retensi, misalnya menghapus foto alert lama yang tidak menjadi kasus.

## 6. Batasan

- Runtime AI punya **2 slot kamera** (id 1 dan 2).
- Kamera yang hanya menerima satu sesi RTSP bisa "direbut" oleh aplikasi lain yang membuka kamera yang sama.
- Mono's `SqlClient` tidak mendukung enkripsi TLS ke SQL Server. Koneksi DB hanya lewat jaringan
  Docker internal, dan port SQL hanya dibuka di 127.0.0.1.
- Login memakai akun lokal (tabel `users`), bukan Keycloak seperti di mockup.
