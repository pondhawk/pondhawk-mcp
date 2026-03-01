using FluentMigrator;
using FluentMigrator.Expressions;
using FluentMigrator.Model;
using FluentMigrator.Runner.Generators;
using Pondhawk.Persistence.Core.Ddl;
using Pondhawk.Persistence.Core.Models;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Migrations;

public static class MigrationSqlGenerator
{
    public static List<(SchemaChange Change, string Sql)> Generate(string provider, List<SchemaChange> changes)
    {
        var generator = (DdlGeneratorBase)DdlGeneratorFactory.Create(provider);
        var migrationGen = generator.GetMigrationGeneratorInternal();
        var results = new List<(SchemaChange, string)>();

        foreach (var change in changes)
        {
            var sql = GenerateForChange(generator, migrationGen, change);
            if (sql.Count > 0)
            {
                foreach (var s in sql)
                    results.Add((change, s));
            }
        }

        return results;
    }

    private static List<string> GenerateForChange(DdlGeneratorBase generator, IMigrationGenerator migrationGen, SchemaChange change)
    {
        return change switch
        {
            TableAdded ta => GenerateTableAdded(generator, migrationGen, ta),
            TableRemoved tr => [GenerateTableRemoved(migrationGen, tr)],
            ColumnAdded ca => [GenerateColumnAdded(generator, migrationGen, ca)],
            ColumnRemoved cr => [GenerateColumnRemoved(migrationGen, cr)],
            ColumnModified cm => [GenerateColumnModified(generator, migrationGen, cm)],
            IndexAdded ia => [GenerateIndexAdded(generator, migrationGen, ia)],
            IndexRemoved ir => [GenerateIndexRemoved(migrationGen, ir)],
            IndexModified im => GenerateIndexModified(generator, migrationGen, im),
            ForeignKeyAdded fka => [GenerateForeignKeyAdded(generator, migrationGen, fka)],
            ForeignKeyRemoved fkr => [GenerateForeignKeyRemoved(migrationGen, fkr)],
            ForeignKeyModified fkm => GenerateForeignKeyModified(generator, migrationGen, fkm),
            PrimaryKeyModified pkm => GeneratePrimaryKeyModified(migrationGen, pkm),
            _ => []
        };
    }

    private static List<string> GenerateTableAdded(DdlGeneratorBase generator, IMigrationGenerator migrationGen, TableAdded ta)
    {
        var results = new List<string>();

        // CREATE TABLE
        var createExpr = generator.BuildCreateTableExpression(ta.Model);
        results.Add(migrationGen.Generate(createExpr));

        // CREATE INDEX for each index
        foreach (var idx in ta.Model.Indexes)
        {
            var indexExpr = generator.BuildCreateIndexExpression(ta.Model, idx);
            results.Add(migrationGen.Generate(indexExpr));
        }

        // CREATE FOREIGN KEY for each FK
        foreach (var fk in ta.Model.ForeignKeys)
        {
            var fkExpr = generator.BuildCreateForeignKeyExpression(ta.Model, fk);
            results.Add(migrationGen.Generate(fkExpr));
        }

        return results;
    }

    private static string GenerateTableRemoved(IMigrationGenerator migrationGen, TableRemoved tr)
    {
        var expr = new DeleteTableExpression
        {
            TableName = tr.TableName,
            SchemaName = tr.SchemaName
        };
        return migrationGen.Generate(expr);
    }

    private static string GenerateColumnAdded(DdlGeneratorBase generator, IMigrationGenerator migrationGen, ColumnAdded ca)
    {
        var expr = new CreateColumnExpression
        {
            TableName = ca.TableName,
            SchemaName = ca.SchemaName,
            Column = BuildColumnDefinition(generator, ca.Column)
        };
        return migrationGen.Generate(expr);
    }

    private static string GenerateColumnRemoved(IMigrationGenerator migrationGen, ColumnRemoved cr)
    {
        var expr = new DeleteColumnExpression
        {
            TableName = cr.TableName,
            SchemaName = cr.SchemaName,
        };
        expr.ColumnNames.Add(cr.ColumnName);
        return migrationGen.Generate(expr);
    }

    private static string GenerateColumnModified(DdlGeneratorBase generator, IMigrationGenerator migrationGen, ColumnModified cm)
    {
        var expr = new AlterColumnExpression
        {
            TableName = cm.TableName,
            SchemaName = cm.SchemaName,
            Column = BuildColumnDefinition(generator, cm.NewColumn)
        };
        return migrationGen.Generate(expr);
    }

    private static string GenerateIndexAdded(DdlGeneratorBase generator, IMigrationGenerator migrationGen, IndexAdded ia)
    {
        var model = new Model { Name = ia.TableName, Schema = ia.SchemaName };
        var indexExpr = generator.BuildCreateIndexExpression(model, ia.Index);
        return migrationGen.Generate(indexExpr);
    }

    private static string GenerateIndexRemoved(IMigrationGenerator migrationGen, IndexRemoved ir)
    {
        var expr = new DeleteIndexExpression
        {
            Index = new IndexDefinition
            {
                Name = ir.Index.Name,
                TableName = ir.TableName,
                SchemaName = ir.SchemaName
            }
        };
        return migrationGen.Generate(expr);
    }

    private static List<string> GenerateIndexModified(DdlGeneratorBase generator, IMigrationGenerator migrationGen, IndexModified im)
    {
        // DROP old index, CREATE new index
        var dropExpr = new DeleteIndexExpression
        {
            Index = new IndexDefinition
            {
                Name = im.OldIndex.Name,
                TableName = im.TableName,
                SchemaName = im.SchemaName
            }
        };

        var model = new Model { Name = im.TableName, Schema = im.SchemaName };
        var createExpr = generator.BuildCreateIndexExpression(model, im.NewIndex);

        return [migrationGen.Generate(dropExpr), migrationGen.Generate(createExpr)];
    }

    private static string GenerateForeignKeyAdded(DdlGeneratorBase generator, IMigrationGenerator migrationGen, ForeignKeyAdded fka)
    {
        var model = new Model { Name = fka.TableName, Schema = fka.SchemaName };
        var fkExpr = generator.BuildCreateForeignKeyExpression(model, fka.ForeignKey);
        return migrationGen.Generate(fkExpr);
    }

    private static string GenerateForeignKeyRemoved(IMigrationGenerator migrationGen, ForeignKeyRemoved fkr)
    {
        var expr = new DeleteForeignKeyExpression
        {
            ForeignKey = new ForeignKeyDefinition
            {
                Name = fkr.ForeignKey.Name,
                ForeignTable = fkr.TableName,
                ForeignTableSchema = fkr.SchemaName
            }
        };
        return migrationGen.Generate(expr);
    }

    private static List<string> GenerateForeignKeyModified(DdlGeneratorBase generator, IMigrationGenerator migrationGen, ForeignKeyModified fkm)
    {
        // DROP old FK, CREATE new FK
        var dropExpr = new DeleteForeignKeyExpression
        {
            ForeignKey = new ForeignKeyDefinition
            {
                Name = fkm.OldForeignKey.Name,
                ForeignTable = fkm.TableName,
                ForeignTableSchema = fkm.SchemaName
            }
        };

        var model = new Model { Name = fkm.TableName, Schema = fkm.SchemaName };
        var createExpr = generator.BuildCreateForeignKeyExpression(model, fkm.NewForeignKey);

        return [migrationGen.Generate(dropExpr), migrationGen.Generate(createExpr)];
    }

    private static List<string> GeneratePrimaryKeyModified(IMigrationGenerator migrationGen, PrimaryKeyModified pkm)
    {
        var results = new List<string>();

        // Drop old PK if it existed
        if (pkm.OldPrimaryKey is not null)
        {
            var pkName = !string.IsNullOrEmpty(pkm.OldPrimaryKey.Name)
                ? pkm.OldPrimaryKey.Name
                : $"PK_{pkm.TableName}";
            var dropExpr = new DeleteConstraintExpression(ConstraintType.PrimaryKey)
            {
                Constraint = new ConstraintDefinition(ConstraintType.PrimaryKey)
                {
                    ConstraintName = pkName,
                    TableName = pkm.TableName,
                    SchemaName = pkm.SchemaName
                }
            };
            results.Add(migrationGen.Generate(dropExpr));
        }

        // Create new PK if desired
        if (pkm.NewPrimaryKey is not null)
        {
            var pkName = !string.IsNullOrEmpty(pkm.NewPrimaryKey.Name)
                ? pkm.NewPrimaryKey.Name
                : $"PK_{pkm.TableName}";
            var createExpr = new CreateConstraintExpression(ConstraintType.PrimaryKey)
            {
                Constraint = new ConstraintDefinition(ConstraintType.PrimaryKey)
                {
                    ConstraintName = pkName,
                    TableName = pkm.TableName,
                    SchemaName = pkm.SchemaName
                }
            };
            foreach (var col in pkm.NewPrimaryKey.Columns)
                createExpr.Constraint.Columns.Add(col);
            results.Add(migrationGen.Generate(createExpr));
        }

        return results;
    }

    private static ColumnDefinition BuildColumnDefinition(DdlGeneratorBase generator, Attribute attr)
    {
        var col = new ColumnDefinition
        {
            Name = attr.Name,
            CustomType = generator.ResolveColumnTypeInternal(attr),
            IsNullable = attr.IsNullable,
            IsIdentity = attr.IsIdentity
        };

        if (!string.IsNullOrEmpty(attr.DefaultValue) && !attr.IsIdentity)
            col.DefaultValue = RawSql.Insert(generator.ProcessDefaultValueInternal(attr));

        return col;
    }
}
