CREATE TABLE IF NOT EXISTS notifications (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    source_event_id uuid NOT NULL UNIQUE,
    aggregate_id uuid NOT NULL,
    template_key varchar(80) NOT NULL,
    channel varchar(20) NOT NULL DEFAULT 'inapp',
    status varchar(20) NOT NULL DEFAULT 'Pending',
    created_at timestamptz NOT NULL DEFAULT now(),
    delivered_at timestamptz,
    read_at timestamptz
);

CREATE INDEX IF NOT EXISTS ix_notifications_owner ON notifications (
    user_id,
    created_at
);

CREATE INDEX IF NOT EXISTS ix_notifications_delivery ON notifications (
    created_at,
    id
) WHERE status='Pending' AND channel='inapp' AND template_key LIKE 'dispute.refund.%';

CREATE INDEX IF NOT EXISTS ix_notification_request_link ON service_outbox (
    (body->'payload'->>'notificationId')
) WHERE event_type='notification.requested';
