using Pondhawk.Persistence.Core.Ddl;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Ddl;

public class DdlTypeMapperTests
{
    [Theory]
    [InlineData("sqlserver", "int", "int")]
    [InlineData("sqlserver", "bigint", "bigint")]
    [InlineData("sqlserver", "smallint", "smallint")]
    [InlineData("sqlserver", "tinyint", "tinyint")]
    [InlineData("sqlserver", "boolean", "bit")]
    [InlineData("sqlserver", "bool", "bit")]
    [InlineData("sqlserver", "decimal", "decimal")]
    [InlineData("sqlserver", "float", "float")]
    [InlineData("sqlserver", "real", "real")]
    [InlineData("sqlserver", "money", "money")]
    [InlineData("sqlserver", "varchar", "varchar")]
    [InlineData("sqlserver", "nvarchar", "nvarchar")]
    [InlineData("sqlserver", "text", "text")]
    [InlineData("sqlserver", "datetime", "datetime")]
    [InlineData("sqlserver", "datetime2", "datetime2")]
    [InlineData("sqlserver", "date", "date")]
    [InlineData("sqlserver", "time", "time")]
    [InlineData("sqlserver", "timestamp", "rowversion")]
    [InlineData("sqlserver", "uuid", "uniqueidentifier")]
    [InlineData("sqlserver", "uniqueidentifier", "uniqueidentifier")]
    [InlineData("sqlserver", "binary", "binary")]
    [InlineData("sqlserver", "varbinary", "varbinary")]
    [InlineData("sqlserver", "blob", "varbinary(max)")]
    [InlineData("sqlserver", "image", "image")]
    [InlineData("sqlserver", "xml", "xml")]
    [InlineData("sqlserver", "json", "nvarchar(max)")]
    [InlineData("sqlserver", "jsonb", "nvarchar(max)")]
    public void SqlServer_MapsTypesCorrectly(string provider, string input, string expected)
    {
        DdlTypeMapper.MapType(provider, input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("postgresql", "int", "integer")]
    [InlineData("postgresql", "bigint", "bigint")]
    [InlineData("postgresql", "boolean", "boolean")]
    [InlineData("postgresql", "bool", "boolean")]
    [InlineData("postgresql", "decimal", "numeric")]
    [InlineData("postgresql", "float", "double precision")]
    [InlineData("postgresql", "varchar", "varchar")]
    [InlineData("postgresql", "nvarchar", "varchar")]
    [InlineData("postgresql", "datetime", "timestamp")]
    [InlineData("postgresql", "uuid", "uuid")]
    [InlineData("postgresql", "uniqueidentifier", "uuid")]
    [InlineData("postgresql", "binary", "bytea")]
    [InlineData("postgresql", "varbinary", "bytea")]
    [InlineData("postgresql", "blob", "bytea")]
    [InlineData("postgresql", "json", "jsonb")]
    public void PostgreSql_MapsTypesCorrectly(string provider, string input, string expected)
    {
        DdlTypeMapper.MapType(provider, input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("mysql", "int", "int")]
    [InlineData("mysql", "boolean", "tinyint(1)")]
    [InlineData("mysql", "bool", "tinyint(1)")]
    [InlineData("mysql", "float", "double")]
    [InlineData("mysql", "money", "decimal(19,4)")]
    [InlineData("mysql", "uuid", "char(36)")]
    [InlineData("mysql", "datetime2", "datetime(6)")]
    [InlineData("mysql", "image", "longblob")]
    [InlineData("mysql", "json", "json")]
    public void MySql_MapsTypesCorrectly(string provider, string input, string expected)
    {
        DdlTypeMapper.MapType(provider, input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("sqlite", "int", "INTEGER")]
    [InlineData("sqlite", "bigint", "INTEGER")]
    [InlineData("sqlite", "boolean", "INTEGER")]
    [InlineData("sqlite", "decimal", "REAL")]
    [InlineData("sqlite", "float", "REAL")]
    [InlineData("sqlite", "varchar", "TEXT")]
    [InlineData("sqlite", "datetime", "TEXT")]
    [InlineData("sqlite", "uuid", "TEXT")]
    [InlineData("sqlite", "binary", "BLOB")]
    [InlineData("sqlite", "json", "TEXT")]
    public void Sqlite_MapsTypesCorrectly(string provider, string input, string expected)
    {
        DdlTypeMapper.MapType(provider, input).ShouldBe(expected);
    }

    [Fact]
    public void UnrecognizedType_PassedThroughVerbatim()
    {
        DdlTypeMapper.MapType("sqlserver", "custom_domain_type").ShouldBe("custom_domain_type");
        DdlTypeMapper.MapType("postgresql", "citext").ShouldBe("citext");
    }

    [Fact]
    public void ParameterizedType_PreservesPrecision()
    {
        DdlTypeMapper.MapType("sqlserver", "decimal(18,2)").ShouldBe("decimal(18,2)");
        DdlTypeMapper.MapType("postgresql", "decimal(18,2)").ShouldBe("numeric(18,2)");
        DdlTypeMapper.MapType("mysql", "decimal(18,2)").ShouldBe("decimal(18,2)");
        DdlTypeMapper.MapType("sqlite", "decimal(18,2)").ShouldBe("REAL(18,2)");
    }

    [Fact]
    public void ParameterizedType_VarcharWithLength()
    {
        DdlTypeMapper.MapType("sqlserver", "varchar(255)").ShouldBe("varchar(255)");
        DdlTypeMapper.MapType("postgresql", "nvarchar(100)").ShouldBe("varchar(100)");
        DdlTypeMapper.MapType("sqlite", "varchar(50)").ShouldBe("TEXT(50)");
    }

    [Theory]
    [InlineData("INT")]
    [InlineData("Int")]
    [InlineData("int")]
    public void CaseInsensitive_Matching(string input)
    {
        DdlTypeMapper.MapType("sqlserver", input).ShouldBe("int");
    }

    [Fact]
    public void MariaDb_TreatedAsMySql()
    {
        DdlTypeMapper.MapType("mariadb", "boolean").ShouldBe("tinyint(1)");
        DdlTypeMapper.MapType("mariadb", "json").ShouldBe("json");
    }

    [Fact]
    public void EmptyOrNull_ReturnsAsIs()
    {
        DdlTypeMapper.MapType("sqlserver", "").ShouldBe("");
        DdlTypeMapper.MapType("sqlserver", " ").ShouldBe(" ");
    }
}
