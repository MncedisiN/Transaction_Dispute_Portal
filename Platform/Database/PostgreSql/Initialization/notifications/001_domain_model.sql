-- Append-only domain identity migration. Preserve public identities, money, states and delivery records.
CREATE SCHEMA IF NOT EXISTS notifications;
CREATE TEMP TABLE domain_migration_counts(table_name text PRIMARY KEY, row_count bigint NOT NULL) ON COMMIT DROP;
INSERT INTO domain_migration_counts VALUES ('notifications', (SELECT count(*) FROM public.notifications));
ALTER TABLE public.notifications SET SCHEMA notifications;
ALTER TABLE notifications."notifications" RENAME COLUMN "id" TO "publicId";
ALTER TABLE notifications."notifications" RENAME COLUMN "user_id" TO "customerPublicId";
ALTER TABLE notifications."notifications" RENAME COLUMN "source_event_id" TO "sourceEventPublicId";
ALTER TABLE notifications."notifications" RENAME COLUMN "aggregate_id" TO "aggregatePublicId";
ALTER TABLE notifications."notifications" RENAME COLUMN "template_key" TO "templateKey";
ALTER TABLE notifications."notifications" RENAME COLUMN "created_at" TO "createdAt";
ALTER TABLE notifications."notifications" RENAME COLUMN "delivered_at" TO "deliveredAt";
ALTER TABLE notifications."notifications" RENAME COLUMN "read_at" TO "readAt";
ALTER TABLE notifications."notifications" DROP CONSTRAINT notifications_pkey;
ALTER TABLE notifications."notifications" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE notifications."notifications" ADD CONSTRAINT notifications_pkey PRIMARY KEY(id);
ALTER TABLE notifications."notifications" ADD CONSTRAINT notifications_public_identity UNIQUE("publicId");
CREATE OR REPLACE FUNCTION notifications.protect_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF to_jsonb(NEW)->'id' IS DISTINCT FROM to_jsonb(OLD)->'id'
       OR to_jsonb(NEW)->'publicId' IS DISTINCT FROM to_jsonb(OLD)->'publicId' THEN
        RAISE EXCEPTION 'Record identity is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON notifications."notifications" FOR EACH ROW EXECUTE FUNCTION notifications.protect_identity();
DO $$ BEGIN IF (SELECT count(*) FROM notifications."notifications") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='notifications') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
