"""Initial schema: users, cameras, violations (+ evidence), case notes.

Revision ID: 0001
Revises:
Create Date: 2026-11-03
"""
from alembic import op
import sqlalchemy as sa

revision = '0001'
down_revision = None
branch_labels = None
depends_on = None


def upgrade():
    op.create_table(
        'users',
        sa.Column('id', sa.Integer, primary_key=True, autoincrement=True),
        sa.Column('username', sa.Unicode(64), nullable=False, unique=True),
        sa.Column('full_name', sa.Unicode(128), nullable=False),
        sa.Column('email', sa.Unicode(128), nullable=True),
        # admin | supervisor | operator
        sa.Column('role', sa.String(16), nullable=False),
        sa.Column('password_hash', sa.String(200), nullable=False),
        sa.Column('is_active', sa.Boolean, nullable=False, server_default=sa.true()),
        sa.Column('created_at', sa.DateTime, nullable=False, server_default=sa.text('SYSUTCDATETIME()')),
        sa.Column('last_login_at', sa.DateTime, nullable=True),
        sa.CheckConstraint("role IN ('admin','supervisor','operator')", name='ck_users_role'),
    )

    # The AI runtime has two camera slots, so ids are fixed (1, 2) rather than identity.
    op.create_table(
        'cameras',
        sa.Column('id', sa.Integer, primary_key=True, autoincrement=False),
        sa.Column('name', sa.Unicode(64), nullable=False),
        sa.Column('location', sa.Unicode(128), nullable=False, server_default=''),
        sa.Column('rtsp_url', sa.Unicode(500), nullable=True),
        sa.Column('apd_enabled', sa.Boolean, nullable=False, server_default=sa.true()),
        sa.Column('vehicle_enabled', sa.Boolean, nullable=False, server_default=sa.false()),
        # Paths/profiles understood by the AI package, e.g. masks/posluar_1080p.json.
        sa.Column('apd_mask', sa.Unicode(200), nullable=True),
        sa.Column('apd_penutup', sa.Unicode(200), nullable=True),
        sa.Column('vehicle_profile', sa.Unicode(64), nullable=True),
        sa.Column('updated_at', sa.DateTime, nullable=False, server_default=sa.text('SYSUTCDATETIME()')),
        sa.CheckConstraint('id IN (1, 2)', name='ck_cameras_slot'),
    )

    op.create_table(
        'violations',
        sa.Column('id', sa.BigInteger, primary_key=True, autoincrement=True),
        # Idempotency key from the AI outbox; a retried delivery never adds a row.
        sa.Column('event_key', sa.String(64), nullable=False, unique=True),
        sa.Column('camera_id', sa.Integer, sa.ForeignKey('cameras.id'), nullable=False),
        sa.Column('camera_name', sa.Unicode(64), nullable=False),
        # apd | vehicle
        sa.Column('category', sa.String(16), nullable=False),
        sa.Column('label', sa.Unicode(128), nullable=False),
        sa.Column('severity', sa.String(16), nullable=False),
        sa.Column('confidence', sa.Float, nullable=False),
        sa.Column('detected_at', sa.DateTime, nullable=False),  # UTC
        sa.Column('created_at', sa.DateTime, nullable=False, server_default=sa.text('SYSUTCDATETIME()')),
        # NULL = plain alert; baru | diproses | selesai once promoted to a case.
        sa.Column('case_status', sa.String(16), nullable=True),
        sa.Column('case_opened_at', sa.DateTime, nullable=True),
        sa.Column('assigned_user_id', sa.Integer, sa.ForeignKey('users.id'), nullable=True),
        sa.CheckConstraint("category IN ('apd','vehicle')", name='ck_violations_category'),
        sa.CheckConstraint("case_status IS NULL OR case_status IN ('baru','diproses','selesai')",
                           name='ck_violations_case_status'),
    )
    op.create_index('ix_violations_detected', 'violations', ['detected_at'])
    op.create_index('ix_violations_category_detected', 'violations', ['category', 'detected_at'])
    op.create_index('ix_violations_case_status', 'violations', ['case_status'])

    # Kept apart so list queries never read image bytes.
    op.create_table(
        'violation_evidence',
        sa.Column('violation_id', sa.BigInteger, sa.ForeignKey('violations.id', ondelete='CASCADE'), primary_key=True),
        sa.Column('jpeg', sa.LargeBinary, nullable=False),
    )

    op.create_table(
        'case_notes',
        sa.Column('id', sa.BigInteger, primary_key=True, autoincrement=True),
        sa.Column('violation_id', sa.BigInteger, sa.ForeignKey('violations.id', ondelete='CASCADE'), nullable=False),
        sa.Column('user_id', sa.Integer, sa.ForeignKey('users.id'), nullable=False),
        sa.Column('note', sa.Unicode(1000), nullable=False),
        sa.Column('created_at', sa.DateTime, nullable=False, server_default=sa.text('SYSUTCDATETIME()')),
    )
    op.create_index('ix_case_notes_violation', 'case_notes', ['violation_id'])


def downgrade():
    op.drop_table('case_notes')
    op.drop_table('violation_evidence')
    op.drop_table('violations')
    op.drop_table('cameras')
    op.drop_table('users')
