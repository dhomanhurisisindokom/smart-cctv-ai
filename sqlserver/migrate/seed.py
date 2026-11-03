"""First admin account, only while the users table is empty. Never resets an existing password."""
import base64
import hashlib
import os

from sqlalchemy import create_engine, text

from db import url

ITERATIONS = 100000


def hash_password(password):
    # Same format is verified by the .NET web (Data/PasswordHasher.cs).
    salt = os.urandom(16)
    digest = hashlib.pbkdf2_hmac('sha256', password.encode('utf-8'), salt, ITERATIONS)
    return 'pbkdf2_sha256$%d$%s$%s' % (ITERATIONS, base64.b64encode(salt).decode(), base64.b64encode(digest).decode())


username = os.environ.get('MINI_ADMIN_USERNAME', 'admin').strip()
password = os.environ['MINI_ADMIN_PASSWORD']
if len(password) < 8:
    raise SystemExit('MINI_ADMIN_PASSWORD minimal 8 karakter')

with create_engine(url()).begin() as conn:
    if conn.execute(text('SELECT COUNT(*) FROM users')).scalar():
        print('Users sudah ada; akun admin awal tidak dibuat ulang.')
    else:
        conn.execute(text(
            "INSERT INTO users (username, full_name, role, password_hash, is_active, created_at) "
            "VALUES (:u, 'Administrator', 'admin', :h, 1, SYSUTCDATETIME())"), {'u': username, 'h': hash_password(password)})
        print('Akun admin awal dibuat:', username)
