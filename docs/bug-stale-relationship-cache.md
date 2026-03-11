# Bug: Stale Merged Relationships After Config Change

## Summary

When explicit `Relationships` are removed from `persistence.project.json`, the previously merged ForeignKeys persist on cached `Model` objects until `db-design.json` is also modified.

## Root Cause

`TimestampCache` reloads the config when `persistence.project.json` changes, but does **not** invalidate the schema cache. Since `RelationshipMerger.Merge()` adds ForeignKeys directly to the in-memory `Model` objects, those FKs survive across `generate` calls as long as `db-design.json` hasn't changed.

The schema cache check (`GetSchema`) only compares `File.GetLastWriteTimeUtc` on `db-design.json` — if that file hasn't been touched, the cached models (with stale merged FKs) are returned as-is.

## Reproduction

1. Add a relationship in `persistence.project.json` (e.g., `BillPayments → Bills`)
2. Run `generate` — FK navigation properties are generated correctly
3. Remove the relationship from `persistence.project.json`
4. Run `generate` again — the FK navigation properties are still generated despite the relationship being removed from config

## Workaround

Touch `db-design.json` to force a schema cache reload:

```bash
touch db-design.json
```

## Suggested Fix

In `TimestampCache.GetConfiguration()`, when the config has changed (cache miss), also invalidate the schema cache so that `RelationshipMerger.Merge()` runs against clean models on the next `generate` call.

Alternatively, `RelationshipMerger.Merge()` could clear existing FKs that were added by previous explicit relationships before applying the current set. This would require tracking which FKs were introspected vs. explicitly added.
