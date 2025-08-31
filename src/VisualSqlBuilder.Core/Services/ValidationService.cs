using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Data.SqlClient;
using System.Data;
using VisualSqlBuilder.Core.Models;

namespace VisualSqlBuilder.Core.Services;

public interface IValidationService
{
    Task<ValidationResult> ValidateWithSqlRuleAsync(string connectionString, ValidationRule rule, DataTable data);
    Task<ValidationResult> ValidateWithCSharpRuleAsync(ValidationRule rule, DataTable data);
    Task<List<ValidationResult>> ValidateDataAsync(string connectionString, List<ValidationRule> rules, DataTable data);
}

public class ValidationService : IValidationService
{
    public async Task<ValidationResult> ValidateWithSqlRuleAsync(string connectionString, ValidationRule rule, DataTable data)
    {
        var result = new ValidationResult
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            IsValid = true,
            Errors = new List<string>()
        };

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // Create temp table with data
            var tempTableName = $"#TempValidation_{Guid.NewGuid():N}";
            var createTableSql = GenerateCreateTempTableSql(tempTableName, data);

            using (var createCmd = new SqlCommand(createTableSql, connection))
            {
                await createCmd.ExecuteNonQueryAsync();
            }

            // Insert data into temp table
            await BulkInsertDataAsync(connection, tempTableName, data);

            // Execute validation rule
            var validationSql = rule.Expression.Replace("@Table", tempTableName);
            using var validateCmd = new SqlCommand(validationSql, connection);
            using var reader = await validateCmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.IsValid = false;
                result.Errors.Add(reader[0]?.ToString() ?? "Validation failed");
            }

            // Clean up temp table
            using var dropCmd = new SqlCommand($"DROP TABLE {tempTableName}", connection);
            await dropCmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Validation error: {ex.Message}");
        }

        return result;
    }

    public async Task<ValidationResult> ValidateWithCSharpRuleAsync(ValidationRule rule, DataTable data)
    {
        var result = new ValidationResult
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            IsValid = true,
            Errors = new List<string>()
        };

        try
        {
            var scriptOptions = ScriptOptions.Default
                .WithReferences(typeof(DataTable).Assembly)
                .WithImports("System", "System.Data", "System.Linq");

            var script = CSharpScript.Create<bool>(
                rule.Expression,
                scriptOptions,
                typeof(ValidationContext));

            var context = new ValidationContext { Data = data };
            var scriptResult = await script.RunAsync(context);

            result.IsValid = scriptResult.ReturnValue;
            if (!result.IsValid)
            {
                result.Errors.Add(rule.ErrorMessage ?? "Validation failed");
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Script error: {ex.Message}");
        }

        return result;
    }

    public async Task<List<ValidationResult>> ValidateDataAsync(string connectionString, List<ValidationRule> rules, DataTable data)
    {
        var results = new List<ValidationResult>();

        foreach (var rule in rules.Where(r => r.IsActive))
        {
            ValidationResult result;

            if (rule.RuleType == "SQL")
            {
                result = await ValidateWithSqlRuleAsync(connectionString, rule, data);
            }
            else
            {
                result = await ValidateWithCSharpRuleAsync(rule, data);
            }

            results.Add(result);
        }

        return results;
    }

    private string GenerateCreateTempTableSql(string tableName, DataTable data)
    {
        var columns = new List<string>();

        foreach (DataColumn column in data.Columns)
        {
            var sqlType = GetSqlType(column.DataType);
            columns.Add($"[{column.ColumnName}] {sqlType} NULL");
        }

        return $"CREATE TABLE {tableName} ({string.Join(", ", columns)})";
    }

    private string GetSqlType(Type type)
    {
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "BIT",
            TypeCode.Byte => "TINYINT",
            TypeCode.Int16 => "SMALLINT",
            TypeCode.Int32 => "INT",
            TypeCode.Int64 => "BIGINT",
            TypeCode.Single => "REAL",
            TypeCode.Double => "FLOAT",
            TypeCode.Decimal => "DECIMAL(18,2)",
            TypeCode.DateTime => "DATETIME2",
            TypeCode.String => "NVARCHAR(MAX)",
            _ => "NVARCHAR(MAX)"
        };
    }

    private async Task BulkInsertDataAsync(SqlConnection connection, string tableName, DataTable data)
    {
        using var bulkCopy = new SqlBulkCopy(connection);
        bulkCopy.DestinationTableName = tableName;

        foreach (DataColumn column in data.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(data);
    }
}

public class ValidationContext
{
    public DataTable Data { get; set; } = new DataTable();
}

public class ValidationResult
{
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

