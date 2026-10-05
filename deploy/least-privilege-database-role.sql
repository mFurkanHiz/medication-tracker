-- A database role for the application that is not the cluster superuser.
--
-- WHY THIS FILE EXISTS
-- compose.production.yml sets POSTGRES_USER: medication_tracker, and the official
-- postgres image creates that account as the cluster's bootstrap SUPERUSER. Until
-- acceptance row 37 was applied, the application's connection string used the same
-- name, so the API connected to PostgreSQL with superuser rights.
--
-- That is more than it needs and more than is safe on a shared host. A superuser
-- connection can read and drop every database in the cluster, not only this one, and
-- COPY ... FROM PROGRAM lets it run commands as the postgres process. Any SQL injection
-- or leaked password is therefore not a "read the household tables" problem, it is a
-- "the database container is yours" problem.
--
-- WHO RUNS WHAT, AFTERWARDS
-- The deployment keeps applying migrations as the bootstrap superuser, inside the
-- database container over its local socket (deploy/deploy-production.sh); that account's
-- password never leaves the host. The API connects as medication_tracker_app, which owns
-- the application schemas, holds every right on the tables and sequences in them, and
-- is granted the same on whatever the superuser's future migrations create there — the
-- ALTER DEFAULT PRIVILEGES below is what keeps the next migration from leaving the
-- application unable to read its own new table.
--
-- HOW IT IS APPLIED
-- By deploy/apply-least-privilege.sh on the host, driven by the "Apply least privilege"
-- workflow: the password is generated on the host, this file runs as the superuser,
-- the role is proven confined, APP_DATABASE_USER / APP_DATABASE_PASSWORD are written to
-- .env.production, and only the api container is recreated. Running this file alone
-- does nothing to the live API: it keeps using whatever .env.production says.
--
-- By hand, in an emergency, the same steps are:
--   docker compose --project-name medication-tracker \
--     --env-file /opt/medication-tracker/.env.production \
--     -f /opt/medication-tracker/compose.production.yml \
--     exec -T database psql -U medication_tracker -d medication_tracker \
--     -v app_password="'<the new password>'" -f - < deploy/least-privilege-database-role.sql
--   then APP_DATABASE_USER=medication_tracker_app and APP_DATABASE_PASSWORD=<it> in
--   /opt/medication-tracker/.env.production, and `up -d --no-deps --force-recreate api`.
--
-- Everything below is idempotent: it can be run twice without error, and running it
-- again with a new :app_password rotates the credential.
--
-- VERIFIED, not assumed. Run against a throwaway database on 2026-10-04 and again on
-- 2026-10-05 after the default-privilege clause was added: the role was created
-- NOSUPERUSER / NOCREATEDB / NOCREATEROLE / NOBYPASSRLS, it owned the eight schemas, it
-- could CREATE TABLE and INSERT inside them, it could read and write a table the
-- superuser created afterwards, and COPY ... TO PROGRAM was refused with "Only roles with
-- privileges of the pg_execute_server_program role may COPY to or from an external
-- program" — which is the specific power this file exists to take away.

\set ON_ERROR_STOP on

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'medication_tracker_app') THEN
        CREATE ROLE medication_tracker_app LOGIN;
    END IF;
END
$$;

ALTER ROLE medication_tracker_app
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;

ALTER ROLE medication_tracker_app PASSWORD :app_password;

-- PUBLIC loses its default rights on this database, so a future role cannot connect to
-- it merely by existing.
--
-- Being precise about what this does and does not confine: the new role can still connect
-- to the cluster's own postgres and template databases, because PUBLIC holds CONNECT on
-- those by default and revoking it there is a change to databases this file does not own.
-- That is acceptable here only because PostgreSQL runs in this project's own container
-- (medication-tracker-database-1) and that cluster holds no other project's data. If the
-- cluster is ever shared, revoke CONNECT on the other databases too.
REVOKE ALL ON DATABASE medication_tracker FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE medication_tracker TO medication_tracker_app;

-- The application owns its schemas and holds every right on what is in them today.
-- Tables the deployment creates tomorrow are owned by the superuser that runs the
-- migration, so the default privileges for that role, per schema, grant them to the
-- application as they appear. It owns nothing outside these schemas.
DO $$
DECLARE
    target text;
BEGIN
    FOREACH target IN ARRAY ARRAY[
        'care', 'catalog', 'households', 'identity', 'inventory', 'sync', 'treatments',
        'infrastructure'
    ]
    LOOP
        EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I', target);
        EXECUTE format('ALTER SCHEMA %I OWNER TO medication_tracker_app', target);
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO medication_tracker_app', target);
        EXECUTE format(
            'GRANT ALL ON ALL TABLES IN SCHEMA %I TO medication_tracker_app', target);
        EXECUTE format(
            'GRANT ALL ON ALL SEQUENCES IN SCHEMA %I TO medication_tracker_app', target);
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE medication_tracker IN SCHEMA %I '
            'GRANT ALL ON TABLES TO medication_tracker_app', target);
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE medication_tracker IN SCHEMA %I '
            'GRANT ALL ON SEQUENCES TO medication_tracker_app', target);
    END LOOP;
END
$$;

-- public stays readable but not writable: nothing of ours lives there, and leaving it
-- writable is how a role with no business creating tables ends up creating one.
REVOKE CREATE ON SCHEMA public FROM medication_tracker_app;
