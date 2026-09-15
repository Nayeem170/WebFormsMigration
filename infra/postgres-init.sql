-- Local development bootstrap. The credentials here are LOCAL-ONLY (see the
-- Phase 7 decision block in 09-scaling-and-production-plan.md): they must
-- never be pointed at a real database.
CREATE ROLE ccw_app LOGIN PASSWORD 'localdev';
-- Phase 2's design: DDL migrator role, apps held to DML. Phase 7 narrows
-- the migrator off postgres (superuser).
CREATE ROLE ccw_migrator LOGIN PASSWORD 'localdev';
CREATE DATABASE ccw_orders;

GRANT CONNECT ON DATABASE ccw_catalog TO ccw_app, ccw_migrator;
GRANT USAGE, CREATE ON SCHEMA public TO ccw_app, ccw_migrator;
ALTER DEFAULT PRIVILEGES FOR ROLE ccw_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ccw_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ccw_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO ccw_app;

\connect ccw_orders
GRANT CONNECT ON DATABASE ccw_orders TO ccw_app, ccw_migrator;
GRANT USAGE, CREATE ON SCHEMA public TO ccw_app, ccw_migrator;
ALTER DEFAULT PRIVILEGES FOR ROLE ccw_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ccw_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ccw_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO ccw_app;
