-- A database role for the application that is not the cluster superuser.
--
-- WHY THIS FILE EXISTS
-- compose.production.yml sets POSTGRES_USER: medication_tracker, and the official
-- postgres image creates that account as the cluster's bootstrap SUPERUSER. The
-- application's connection string then uses the same name, so the API connects to
-- PostgreSQL with superuser rights.
--
-- That is more than it needs and more than is safe on a shared host. A superuser
-- connection can read and drop every database in the cluster, not only this one, and
-- COPY ... FROM PROGRAM lets it run commands as the postgres process. Any SQL injection
-- or leaked DATABASE_PASSWORD is therefore not a "read the household tables" problem, it
-- is a "the database container is yours" problem.
--
-- The migration step uses the same connection string, so the application role does need
-- DDL on its own schemas. The point is not to take DDL away; it is to stop the account
-- being a superuser and to confine it to this one database.
--
-- WHAT THIS IS NOT
-- This is not run by the deployment. It changes the credential the live API authenticates
-- with, so applying it is the owner's decision and needs a password only the owner holds.
-- Running it does nothing by itself: the application keeps using the old account until
-- DATABASE_PASSWORD and the username in compose.production.yml are changed together.
--
-- HOW TO APPLY IT (owner, in this order)
--   1. Choose a new password. Do not reuse DATABASE_PASSWORD.
--   2. Run this file as the current superuser, substituting the password:
--        docker compose --project-name medication-tracker \
--          --env-file /opt/medication-tracker/.env.production \
--          -f /opt/medication-tracker/compose.production.yml \
--          exec -T database psql -U medication_tracker -d medication_tracker \
--          -v app_password="'<the new password>'" \
--          -f - < deploy/least-privilege-database-role.sql
--   3. Verify the new role can read and write:
--        \c medication_tracker medication_tracker_app
--        SELECT count(*) FROM households.households;
--   4. Set DATABASE_PASSWORD to the new password in /opt/medication-tracker/.env.production
--      and change Username=medication_tracker to Username=medication_tracker_app in
--      compose.production.yml, then merge so the deployment restarts the API with it.
--   5. Only once the API is serving (GET / -> 200, GET /api/auth/session -> 401), revoke
--      the old account's remaining power. Do not do this before step 4 is proven.
--
-- Everything below is idempotent: it can be run twice without error.
--
-- VERIFIED, not assumed. Run against a throwaway database on 2026-10-04: the role was
-- created NOSUPERUSER / NOCREATEDB / NOCREATEROLE / NOBYPASSRLS, it owned the eight
-- schemas, it could CREATE TABLE and INSERT inside them (so the migration step still
-- works), and COPY ... TO PROGRAM was refused with "Only roles with privileges of the
-- pg_execute_server_program role may COPY to or from an external program" — which is the
-- specific power this file exists to take away.

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

-- The application owns its schemas, because the deployment applies migrations through
-- this same connection. It owns nothing outside them.
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
        EXECUTE format(
            'GRANT ALL ON ALL TABLES IN SCHEMA %I TO medication_tracker_app', target);
        EXECUTE format(
            'GRANT ALL ON ALL SEQUENCES IN SCHEMA %I TO medication_tracker_app', target);
    END LOOP;
END
$$;

-- public stays readable but not writable: nothing of ours lives there, and leaving it
-- writable is how a role with no business creating tables ends up creating one.
REVOKE CREATE ON SCHEMA public FROM medication_tracker_app;
