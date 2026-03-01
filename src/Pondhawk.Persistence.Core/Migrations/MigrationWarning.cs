namespace Pondhawk.Persistence.Core.Migrations;

public enum WarningType
{
    Destructive,
    PossibleRename,
    DataLoss,
    NoChanges
}

public sealed record MigrationWarning(WarningType Type, string Message);
