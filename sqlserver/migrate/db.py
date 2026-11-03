import os
from urllib.parse import quote_plus


def url(database=None):
    return 'mssql+pymssql://%s:%s@%s:%s/%s' % (
        quote_plus(os.environ.get('DB_USER', 'sa')), quote_plus(os.environ['DB_PASSWORD']),
        os.environ.get('DB_HOST', 'sqlserver'), os.environ.get('DB_PORT', '1433'),
        database or os.environ.get('DB_NAME', 'smartcctv_mini'))
