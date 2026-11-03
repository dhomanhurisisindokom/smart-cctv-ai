"""Create the database on first install (Alembic needs it to exist)."""
import os
import re
import time

from sqlalchemy import create_engine, text

from db import url

name = os.environ.get('DB_NAME', 'smartcctv_mini')
if not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]{0,63}', name):
    raise SystemExit('DB_NAME tidak valid')

engine = create_engine(url('master'), isolation_level='AUTOCOMMIT')
for attempt in range(30):
    try:
        with engine.connect() as conn:
            if conn.execute(text('SELECT DB_ID(:n)'), {'n': name}).scalar() is None:
                conn.execute(text('CREATE DATABASE [%s]' % name))
                print('Database dibuat:', name)
            else:
                print('Database sudah ada:', name)
        break
    except Exception as exc:
        if attempt == 29:
            raise
        print('Menunggu SQL Server...', type(exc).__name__)
        time.sleep(2)
