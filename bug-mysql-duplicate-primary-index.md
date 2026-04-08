# Bug: MySQL migration emits redundant `CREATE UNIQUE INDEX `PRIMARY`` statements

## Summary

`generate_migration` produces `CREATE UNIQUE INDEX `PRIMARY`` statements for every table, even though the primary key is already defined inline in the `CREATE TABLE` statement. MySQL rejects `PRIMARY` as an explicit index name (Error 1280).

## Steps to Reproduce

1. Introspect a MySQL database with `introspect_schema`
2. Run `generate_migration` (first migration, no prior snapshots)
3. Execute the generated SQL against MySQL

## Error

```
Query:
CREATE UNIQUE INDEX `PRIMARY` ON `Accounts` (`Id` ASC)

Error Code: 1280 - Incorrect index name 'PRIMARY'
```

## Root Cause

The DDL generator emits the primary key twice:

1. Inline in `CREATE TABLE` (correct):
   ```sql
   CREATE TABLE `Accounts` (..., CONSTRAINT `PRIMARY` PRIMARY KEY (`Id`)) ENGINE = INNODB;
   ```

2. As a separate `CREATE UNIQUE INDEX` (redundant/broken):
   ```sql
   CREATE UNIQUE INDEX `PRIMARY` ON `Accounts` (`Id` ASC);
   ```

MySQL does not allow creating an index named `PRIMARY` via `CREATE INDEX` — the primary key can only be defined in `CREATE TABLE` or `ALTER TABLE`.

## Impact

All 41 tables in the test database produced a redundant statement (41 total). The migration fails on the first one encountered.

## Expected Behavior

`generate_migration` should skip index emission for primary key indexes when the PK is already defined in the `CREATE TABLE` statement, or at minimum not emit `CREATE UNIQUE INDEX` with the reserved name `PRIMARY` for the MySQL provider.

## Environment

- Provider: MySQL
- Project: connect-persistence
- pondhawk-mcp: latest version (updated 2026-04-08)
- Migration: V001 (bootstrap — no prior snapshots)
