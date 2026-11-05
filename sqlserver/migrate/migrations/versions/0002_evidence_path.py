"""Evidence JPEG on a file share: path relative to the web's EVIDENCE_DIR.

Rows keep using violation_evidence when the web stores evidence in the database
(EVIDENCE_STORAGE=db); with EVIDENCE_STORAGE=file only evidence_path is set.

Revision ID: 0002
Revises: 0001
Create Date: 2026-11-05
"""
from alembic import op
import sqlalchemy as sa

revision = '0002'
down_revision = '0001'
branch_labels = None
depends_on = None


def upgrade():
    op.add_column('violations', sa.Column('evidence_path', sa.Unicode(400), nullable=True))


def downgrade():
    op.drop_column('violations', 'evidence_path')
