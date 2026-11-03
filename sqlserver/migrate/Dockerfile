# Schema migrations (Alembic) for the mini database. pymssql bundles FreeTDS, so no ODBC driver.
FROM python:3.12-slim-bookworm
RUN pip install --no-cache-dir alembic==1.13.2 sqlalchemy==2.0.35 pymssql==2.4.6
WORKDIR /migrate
COPY . .
CMD ["sh", "-c", "python ensure_database.py && alembic upgrade head && python seed.py"]
