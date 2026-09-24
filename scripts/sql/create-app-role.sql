
\set ON_ERROR_STOP on

DO $role$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'Avallo_app') THEN
        CREATE ROLE "Avallo_app";
    END IF;
END
$role$;

ALTER ROLE "Avallo_app" WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS
    PASSWORD :'app_password';

DO $db$
BEGIN
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO "Avallo_app"', current_database());
END
$db$;

GRANT USAGE ON SCHEMA public TO "Avallo_app";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO "Avallo_app";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO "Avallo_app";

-- Tabelas criadas por migrations futuras tambem precisam ser acessiveis ao role.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "Avallo_app";
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO "Avallo_app";
