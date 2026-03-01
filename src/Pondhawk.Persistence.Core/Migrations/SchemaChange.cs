using Pondhawk.Persistence.Core.Models;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Migrations;

public enum ChangeType
{
    TableAdded,
    TableRemoved,
    ColumnAdded,
    ColumnRemoved,
    ColumnModified,
    IndexAdded,
    IndexRemoved,
    IndexModified,
    ForeignKeyAdded,
    ForeignKeyRemoved,
    ForeignKeyModified,
    PrimaryKeyModified
}

public abstract record SchemaChange(ChangeType Type, string TableName, string SchemaName)
{
    public abstract string Describe();
}

public sealed record TableAdded(string TableName, string SchemaName, Model Model)
    : SchemaChange(ChangeType.TableAdded, TableName, SchemaName)
{
    public override string Describe() => $"Add table {SchemaName}.{TableName}";
}

public sealed record TableRemoved(string TableName, string SchemaName)
    : SchemaChange(ChangeType.TableRemoved, TableName, SchemaName)
{
    public override string Describe() => $"Drop table {SchemaName}.{TableName}";
}

public sealed record ColumnAdded(string TableName, string SchemaName, Attribute Column)
    : SchemaChange(ChangeType.ColumnAdded, TableName, SchemaName)
{
    public override string Describe() => $"Add column {SchemaName}.{TableName}.{Column.Name}";
}

public sealed record ColumnRemoved(string TableName, string SchemaName, string ColumnName)
    : SchemaChange(ChangeType.ColumnRemoved, TableName, SchemaName)
{
    public override string Describe() => $"Drop column {SchemaName}.{TableName}.{ColumnName}";
}

public sealed record ColumnModified(string TableName, string SchemaName, Attribute OldColumn, Attribute NewColumn)
    : SchemaChange(ChangeType.ColumnModified, TableName, SchemaName)
{
    public override string Describe() => $"Alter column {SchemaName}.{TableName}.{NewColumn.Name}";
}

public sealed record IndexAdded(string TableName, string SchemaName, IndexInfo Index)
    : SchemaChange(ChangeType.IndexAdded, TableName, SchemaName)
{
    public override string Describe() => $"Add index {Index.Name} on {SchemaName}.{TableName}";
}

public sealed record IndexRemoved(string TableName, string SchemaName, IndexInfo Index)
    : SchemaChange(ChangeType.IndexRemoved, TableName, SchemaName)
{
    public override string Describe() => $"Drop index {Index.Name} on {SchemaName}.{TableName}";
}

public sealed record IndexModified(string TableName, string SchemaName, IndexInfo OldIndex, IndexInfo NewIndex)
    : SchemaChange(ChangeType.IndexModified, TableName, SchemaName)
{
    public override string Describe() => $"Alter index {NewIndex.Name} on {SchemaName}.{TableName}";
}

public sealed record ForeignKeyAdded(string TableName, string SchemaName, ForeignKey ForeignKey)
    : SchemaChange(ChangeType.ForeignKeyAdded, TableName, SchemaName)
{
    public override string Describe() => $"Add foreign key {ForeignKey.Name} on {SchemaName}.{TableName}";
}

public sealed record ForeignKeyRemoved(string TableName, string SchemaName, ForeignKey ForeignKey)
    : SchemaChange(ChangeType.ForeignKeyRemoved, TableName, SchemaName)
{
    public override string Describe() => $"Drop foreign key {ForeignKey.Name} on {SchemaName}.{TableName}";
}

public sealed record ForeignKeyModified(string TableName, string SchemaName, ForeignKey OldForeignKey, ForeignKey NewForeignKey)
    : SchemaChange(ChangeType.ForeignKeyModified, TableName, SchemaName)
{
    public override string Describe() => $"Alter foreign key {NewForeignKey.Name} on {SchemaName}.{TableName}";
}

public sealed record PrimaryKeyModified(string TableName, string SchemaName, PrimaryKeyInfo? OldPrimaryKey, PrimaryKeyInfo? NewPrimaryKey)
    : SchemaChange(ChangeType.PrimaryKeyModified, TableName, SchemaName)
{
    public override string Describe() => $"Alter primary key on {SchemaName}.{TableName}";
}
