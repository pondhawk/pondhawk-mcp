# Feature Spec: `generate_migration` MCP Tool

## Summary

Add a `generate_migration` tool to the Fabrica Persistence MCP server that automatically produces delta SQL migration scripts by diffing two versions of `db-design.json` — the current baseline snapshot and the working copy (desired state).

## Motivation

Projects using Fabrica Persistence already have:

- `db-design.json` as the declarative schema source of truth
- `generate_ddl` for full-schema DDL output
- `introspect_schema` to reverse-engineer a live database into `db-design.json`

The missing piece is **automated delta migration script generation**. Today, developers must hand-write `ALTER TABLE` statements by mentally diffing schema changes. This is error-prone, tedious, and the MCP server already has all the schema knowledge needed to automate it.

## Design

### Baseline via Snapshots

Each migration is paired with a snapshot of `db-design.json` at the time it was generated. The most recent snapshot serves as the baseline for the next migration. No git history parsing. No database connection.

```
migrations/
  V001__initial_schema.sql            ← delta SQL (full CREATE for first migration)
  V001__initial_schema.json           ← snapshot of db-design.json at V001
  V002__add_billing_columns.sql       ← delta SQL (ALTER statements)
  V002__add_billing_columns.json      ← snapshot of db-design.json at V002
```

### Migration Generation Flow

```
Developer edits db-design.json (desired state)
        │
        ▼
  generate_migration tool invoked
        │
        ├── 1. Locate latest snapshot in migrations folder (e.g., V002__*.json)
        │      └── If none exists, baseline is "empty schema" (no tables)
        │
        ├── 2. Deserialize baseline snapshot → baseline model
        │
        ├── 3. Deserialize db-design.json from project root → desired model
        │
        ├── 4. Diff baseline vs desired → list of schema changes
        │
        ├── 5. Render changes as provider-specific SQL (e.g., MySQL ALTER statements)
        │
        ├── 6. Write migration SQL file: V003__<description>.sql
        │
        ├── 7. Copy current db-design.json → V003__<description>.json (new snapshot)
        │
        └── 8. Return summary of changes to caller

Developer reviews generated SQL, commits db-design.json + .sql + .json together
        │
        ▼
DbUp applies the script at deploy time
```

### No Database Connection Required

The diff is purely file-based: JSON → JSON → SQL. The tool does not connect to any database. This makes it deterministic, offline-capable, and CI-friendly.

## Tool Interface

### `generate_migration`

**Parameters:**

| Parameter     | Type   | Required | Default           | Description                                                              |
|---------------|--------|----------|-------------------|--------------------------------------------------------------------------|
| `description` | string | Yes      | —                 | Short description for the migration filename (e.g., `"add_billing_columns"`) |
| `provider`    | string | No       | Project default   | Target SQL dialect: `mysql`, `postgresql`, `sqlserver`, `sqlite`         |
| `output`      | string | No       | `"migrations"`    | Output directory relative to project root                                |
| `dryRun`      | bool   | No       | `false`           | If true, return the SQL and change summary without writing files         |

**Returns:**

```json
{
  "migrationFile": "migrations/V003__add_billing_columns.sql",
  "snapshotFile": "migrations/V003__add_billing_columns.json",
  "version": 3,
  "changes": [
    { "type": "TableAdded", "table": "SyncOutbox" },
    { "type": "ColumnAdded", "table": "Bills", "column": "DueDate" },
    { "type": "ColumnModified", "table": "Invoices", "column": "Amount", "detail": "decimal(10,2) → decimal(20,2)" },
    { "type": "IndexAdded", "table": "SyncOutbox", "index": "IDX_SyncOutbox_1" }
  ],
  "warnings": [
    { "type": "Destructive", "message": "Column 'OldField' removed from table 'Bills'" }
  ],
  "sql": "-- V003: add_billing_columns\n\nALTER TABLE ..."
}
```

## Diff Operations

### Supported Change Types

The diff engine compares baseline and desired `db-design.json` models and emits the following change types:

#### Table Level

| Change          | Detection                                    | SQL Output                  |
|-----------------|----------------------------------------------|-----------------------------|
| `TableAdded`    | Table exists in desired but not in baseline  | `CREATE TABLE ...`          |
| `TableRemoved`  | Table exists in baseline but not in desired  | `DROP TABLE ...`            |

#### Column Level

| Change            | Detection                                          | SQL Output                          |
|-------------------|-----------------------------------------------------|-------------------------------------|
| `ColumnAdded`     | Column exists in desired table but not in baseline  | `ALTER TABLE ADD COLUMN ...`        |
| `ColumnRemoved`   | Column exists in baseline table but not in desired  | `ALTER TABLE DROP COLUMN ...`       |
| `ColumnModified`  | Column exists in both but properties differ         | `ALTER TABLE MODIFY COLUMN ...`     |

Column modification detects changes to: `DataType`, `IsNullable`, `DefaultValue`, `MaxLength`, `Precision`, `Scale`, `IsIdentity`.

#### Index Level

| Change         | Detection                                           | SQL Output              |
|----------------|------------------------------------------------------|-------------------------|
| `IndexAdded`   | Index exists in desired table but not in baseline    | `CREATE INDEX ...`      |
| `IndexRemoved` | Index exists in baseline table but not in desired    | `DROP INDEX ...`        |
| `IndexModified`| Index exists in both but columns or uniqueness differ| `DROP INDEX` + `CREATE INDEX` |

#### Foreign Key Level

| Change              | Detection                                     | SQL Output                            |
|---------------------|------------------------------------------------|---------------------------------------|
| `ForeignKeyAdded`   | FK exists in desired but not in baseline       | `ALTER TABLE ADD CONSTRAINT ...`      |
| `ForeignKeyRemoved` | FK exists in baseline but not in desired       | `ALTER TABLE DROP FOREIGN KEY ...`    |
| `ForeignKeyModified`| FK exists in both but definition differs       | `DROP` + `ADD`                        |

#### Primary Key Level

| Change              | Detection                                     | SQL Output                            |
|---------------------|------------------------------------------------|---------------------------------------|
| `PrimaryKeyModified`| PK columns differ between baseline and desired | `DROP PRIMARY KEY` + `ADD PRIMARY KEY`|

### Not Supported (Require Manual Scripts)

| Scenario          | Reason                                                                 |
|-------------------|------------------------------------------------------------------------|
| **Table renames** | A rename looks like a drop + add; ambiguous without hints              |
| **Column renames**| Same ambiguity — cannot distinguish rename from drop + add             |
| **Data migrations** | Business logic (backfills, transforms) cannot be derived from schema |
| **View changes**  | Views are read-only projections; DDL varies by provider                |

For renames, the developer should hand-write the migration script and manually copy the snapshot. The tool should detect potential renames (table removed + table added with similar columns) and surface them as warnings.

## Versioning Scheme

- Versions are sequential integers: `V001`, `V002`, `V003`, ...
- The tool scans the output directory for existing `V###__*.sql` files and increments from the highest found.
- Zero-padded to 3 digits by default. If the project exceeds 999 migrations, expand to 4 digits.
- The description is slugified to lowercase with underscores: `"Add Billing Columns"` → `add_billing_columns`.

## Warnings and Safety

The tool must flag destructive operations and return them in a `warnings` array:

| Warning Type      | Trigger                                          |
|-------------------|--------------------------------------------------|
| `Destructive`     | `TableRemoved`, `ColumnRemoved`                  |
| `PossibleRename`  | Table or column removed + similar one added       |
| `DataLoss`        | Column type narrowed (e.g., `varchar(255)` → `varchar(50)`) |
| `NoChanges`       | Baseline and desired are identical — no migration emitted |

When `NoChanges` is detected, the tool writes no files and returns an empty change list with the warning.

## SQL Output Format

Generated SQL files should follow this structure:

```sql
-- Migration: V003__add_billing_columns
-- Generated: 2026-03-01T12:00:00Z
-- Provider: mysql
--
-- Changes:
--   + Table SyncOutbox (CREATE)
--   + Column Bills.DueDate (ADD)
--   ~ Column Invoices.Amount (MODIFY: decimal(10,2) -> decimal(20,2))
--
-- Warnings:
--   ! Column Bills.OldField (DROP) — destructive
--

-- [1] Create table: SyncOutbox
CREATE TABLE `SyncOutbox` (
  ...
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- [2] Add column: Bills.DueDate
ALTER TABLE `Bills` ADD COLUMN `DueDate` datetime NOT NULL DEFAULT '1883-11-19 00:00:00';

-- [3] Modify column: Invoices.Amount
ALTER TABLE `Invoices` MODIFY COLUMN `Amount` decimal(20,2) NOT NULL DEFAULT 0.00;
```

Each statement is numbered, commented, and separated for readability and selective execution during troubleshooting.

## Column Ordering

When adding columns, the tool should append them to the end of the table by default. MySQL supports `AFTER <column>` syntax to control position — the tool may optionally use this to match the column order in `db-design.json`, but this is a nice-to-have, not required.

## First Migration (Bootstrap)

When no snapshot exists in the migrations folder:

- Baseline is treated as an empty schema (no tables, no indexes, no FKs).
- The diff produces `TableAdded` for every table in `db-design.json`.
- Output is equivalent to `generate_ddl` but wrapped in the migration file format with a snapshot.

## Interaction with Existing Tools

| Tool                | Relationship                                                        |
|---------------------|---------------------------------------------------------------------|
| `generate_ddl`      | Produces a full DDL file (documentation/reference). Unchanged.      |
| `introspect_schema` | Reads a live DB into `db-design.json`. Used for bootstrapping, not for migration generation. Unchanged. |
| `generate_migration`| **New.** Reads `db-design.json` + last snapshot → emits delta SQL.  |
| `generate`          | Renders Liquid templates. Unchanged.                                |

## Edge Cases

1. **Multiple schemas**: Diff operates per-schema. Changes in one schema don't affect another.
2. **Empty migrations folder**: Treated as first migration (bootstrap).
3. **Snapshot missing but SQL exists**: Error — the migration history is corrupt. The tool should report this and refuse to generate.
4. **db-design.json unchanged since last snapshot**: `NoChanges` warning, no files written.
5. **Concurrent developers**: Two developers may generate migrations with the same version number. This is resolved at merge time (renumber the later migration). The tool does not need to handle this — it's a git workflow concern.
