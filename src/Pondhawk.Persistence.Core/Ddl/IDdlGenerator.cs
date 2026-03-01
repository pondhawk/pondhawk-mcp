using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Models;

namespace Pondhawk.Persistence.Core.Ddl;

public interface IDdlGenerator
{
    string Generate(List<Model> models, List<SchemaFileEnum>? enums = null,
        string? projectName = null, string? description = null);
}
