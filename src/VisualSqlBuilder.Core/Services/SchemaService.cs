using 
    Microsoft.Data.SqlClient;
using System.Data;
using VisualSqlBuilder.Core.Models;

namespace VisualSqlBuilder.Core.Services;

public interface ISchemaService
{
    Task<List<TableModel>> LoadTablesFromSqlServerAsync(string connectionString);
    Task<List<RelationshipModel>> LoadRelationshipsFromSqlServerAsync(string connectionString, List<TableModel> tables);
    Task<List<TableModel>> LoadTablesFromExcelAsync(Stream excelStream);
    Task<DataTable> ExecuteQueryAsync(string connectionString, string sql, int maxRows = 100);
}

public class SchemaService : ISchemaService
{
    public async Task<List<TableModel>> LoadTablesFromSqlServerAsync(string connectionString)
    {
        var tables = new List<TableModel>();

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var schemaQuery = @"
                SELECT
                    s.name AS SchemaName,
                    t.name AS TableName,
                    c.name AS ColumnName,
                    ty.name AS DataType,
                    c.max_length,
                    c.is_nullable,
                    c.is_identity,
                    CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
                    CASE WHEN fk.parent_column_id IS NOT NULL THEN 1 ELSE 0 END AS IsForeignKey
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                INNER JOIN sys.columns c ON t.object_id = c.object_id
                INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                LEFT JOIN (
                    SELECT ic.object_id, ic.column_id
                    FROM sys.index_columns ic
                    INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    WHERE i.is_primary_key = 1
                ) pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id
                LEFT JOIN sys.foreign_key_columns fk ON c.object_id = fk.parent_object_id AND c.column_id = fk.parent_column_id
                ORDER BY s.name, t.name, c.column_id";

        using var command = new SqlCommand(schemaQuery, connection);
        using var reader = await command.ExecuteReaderAsync();

        var tableDict = new Dictionary<string, TableModel>();

        while (await reader.ReadAsync())
        {
            var schemaName = reader.GetString(0);
            var tableName = reader.GetString(1);
            var fullTableName = $"{schemaName}.{tableName}";

            if (!tableDict.ContainsKey(fullTableName))
            {
                tableDict[fullTableName] = new TableModel
                {
                    Schema = schemaName,
                    Name = tableName,
                    Alias = tableName
                };
            }

            var column = new ColumnModel
            {
                Name = reader.GetString(2),
                DataType = reader.GetString(3),
                MaxLength = reader.IsDBNull(4) ? null : reader.GetInt16(4),
                IsNullable = reader.GetBoolean(5),
                IsPrimaryKey = reader.GetInt32(7) == 1,
                IsForeignKey = reader.GetInt32(8) == 1
            };

            tableDict[fullTableName].Columns.Add(column);
        }

        tables.AddRange(tableDict.Values);
        return tables;
    }

    public async Task<List<RelationshipModel>> LoadRelationshipsFromSqlServerAsync(string connectionString, List<TableModel> tables)
    {
        var relationships = new List<RelationshipModel>();

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var fkQuery = @"
                SELECT
                    fk.name AS ForeignKeyName,
                    ps.name AS ParentSchema,
                    pt.name AS ParentTable,
                    pc.name AS ParentColumn,
                    rs.name AS ReferencedSchema,
                    rt.name AS ReferencedTable,
                    rc.name AS ReferencedColumn
                FROM sys.foreign_keys fk
                INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                INNER JOIN sys.tables pt ON fkc.parent_object_id = pt.object_id
                INNER JOIN sys.schemas ps ON pt.schema_id = ps.schema_id
                INNER JOIN sys.columns pc ON fkc.parent_object_id = pc.object_id AND fkc.parent_column_id = pc.column_id
                INNER JOIN sys.tables rt ON fkc.referenced_object_id = rt.object_id
                INNER JOIN sys.schemas rs ON rt.schema_id = rs.schema_id
                INNER JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id";

        using var command = new SqlCommand(fkQuery, connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var parentSchema = reader.GetString(1);
            var parentTable = reader.GetString(2);
            var parentColumn = reader.GetString(3);
            var refSchema = reader.GetString(4);
            var refTable = reader.GetString(5);
            var refColumn = reader.GetString(6);

            var sourceTable = tables.FirstOrDefault(t => t.Schema == parentSchema && t.Name == parentTable);
            var targetTable = tables.FirstOrDefault(t => t.Schema == refSchema && t.Name == refTable);

            if (sourceTable != null && targetTable != null)
            {
                var sourceCol = sourceTable.Columns.FirstOrDefault(c => c.Name == parentColumn);
                var targetCol = targetTable.Columns.FirstOrDefault(c => c.Name == refColumn);

                if (sourceCol != null && targetCol != null)
                {
                    relationships.Add(new RelationshipModel
                    {
                        SourceTableId = sourceTable.Id,
                        SourceColumnId = sourceCol.Id,
                        TargetTableId = targetTable.Id,
                        TargetColumnId = targetCol.Id,
                        Type = targetCol.IsPrimaryKey ? RelationshipType.Primary : RelationshipType.Foreign,
                        JoinType = JoinType.InnerJoin,
                        Cardinality = targetCol.IsPrimaryKey ? "N:1" : "N:N"
                    });
                }
            }
        }

        return relationships;
    }

    public async Task<List<TableModel>> LoadTablesFromExcelAsync(Stream excelStream)
    {
        var tables = new List<TableModel>();

        using var workbook = new ClosedXML.Excel.XLWorkbook(excelStream);

        foreach (var worksheet in workbook.Worksheets)
        {
            var table = new TableModel
            {
                Name = worksheet.Name,
                Alias = worksheet.Name,
                IsFromExcel = true,
                Schema = "excel"
            };

            var firstRow = worksheet.FirstRowUsed();
            if (firstRow != null)
            {
                foreach (var cell in firstRow.Cells())
                {
                    var columnName = cell.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(columnName))
                    {
                        var dataType = DetectColumnType(worksheet, cell.Address.ColumnNumber);
                        table.Columns.Add(new ColumnModel
                        {
                            Name = columnName,
                            DataType = dataType,
                            IsNullable = true
                        });
                    }
                }
            }

            tables.Add(table);
        }

        return await Task.FromResult(tables);
    }

    private string DetectColumnType(ClosedXML.Excel.IXLWorksheet worksheet, int columnNumber)
    {
        var sampleSize = Math.Min(100, worksheet.LastRowUsed()?.RowNumber() ?? 0);
        var hasDate = false;
        var hasNumber = false;
        var hasText = false;

        for (int row = 2; row <= sampleSize; row++)
        {
            var cell = worksheet.Cell(row, columnNumber);
            if (!cell.IsEmpty())
            {
                if (cell.DataType == ClosedXML.Excel.XLDataType.DateTime)
                    hasDate = true;
                else if (cell.DataType == ClosedXML.Excel.XLDataType.Number)
                    hasNumber = true;
                else
                    hasText = true;
            }
        }

        if (hasDate && !hasText) return "datetime";
        if (hasNumber && !hasText && !hasDate) return "decimal";
        return "nvarchar";
    }

    public async Task<DataTable> ExecuteQueryAsync(string connectionString, string sql, int maxRows = 100)
    {
        var dataTable = new DataTable();

        // Add TOP clause if not present
        if (!sql.ToUpper().Contains("TOP "))
        {
            sql = sql.Replace("SELECT ", $"SELECT TOP {maxRows} ", StringComparison.OrdinalIgnoreCase);
        }

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand(sql, connection);
        using var adapter = new SqlDataAdapter(command);

        adapter.Fill(dataTable);

        return dataTable;
    }
}