using Microsoft.Data.SqlClient;
using System.Data;
using VisualSqlBuilder.Core.Models;
using ClosedXML.Excel;

namespace VisualSqlBuilder.Core.Services;

public interface ISchemaService
{
    Task<List<TableModel>> LoadTablesFromSqlServerAsync(string connectionString);
    Task<List<RelationshipModel>> LoadRelationshipsFromSqlServerAsync(string connectionString, List<TableModel> tables);
    List<TableModel> LoadTablesFromExcel(Stream excelStream);
    Task<DataTable> ExecuteQueryAsync(string connectionString, string sql, int maxRows = 100);
    List<object[]> LoadExcelPreviewData(Stream excelStream, string sheetName, int maxRows = 50);
    ExcelWorkbookInfo GetExcelWorkbookInfo(Stream excelStream);

    DataTable GenerateExcelMockResults(List<TableModel> tables);
    DataTable GenerateMixedMockResults(List<TableModel> tables);
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

    public List<TableModel> LoadTablesFromExcel(Stream excelStream)
    {
        var tables = new List<TableModel>();

        try
        {
            using var workbook = new XLWorkbook(excelStream);

            foreach (var worksheet in workbook.Worksheets)
            {
                // Skip empty worksheets
                if (worksheet.LastRowUsed() == null || worksheet.LastColumnUsed() == null)
                    continue;

                var table = new TableModel
                {
                    Name = SanitizeSheetName(worksheet.Name),
                    Alias = GenerateAlias(worksheet.Name),
                    IsFromExcel = true,
                    Schema = "excel",
                    Description = $"Excel sheet: {worksheet.Name}"
                };

                var firstRow = worksheet.FirstRowUsed();
                if (firstRow == null) continue;

                var lastColumn = worksheet.LastColumnUsed();
                var lastRow = worksheet.LastRowUsed();

                // Set row count
                table.RowCount = lastRow.RowNumber() - 1; // Subtract header row

                // Detect if first row contains headers
                bool hasHeaders = DetectHeaders(worksheet);
                int dataStartRow = hasHeaders ? 2 : 1;
                int headerRow = hasHeaders ? 1 : 0;

                // Process columns
                for (int colNum = 1; colNum <= lastColumn.ColumnNumber(); colNum++)
                {
                    string columnName;

                    if (hasHeaders)
                    {
                        var headerCell = worksheet.Cell(1, colNum);
                        columnName = headerCell.Value.ToString().Trim();
                        if (string.IsNullOrWhiteSpace(columnName))
                            columnName = $"Column{colNum}";
                    }
                    else
                    {
                        columnName = $"Column{colNum}";
                    }

                    // Sanitize column name
                    columnName = SanitizeColumnName(columnName);

                    // Detect data type by sampling data
                    var dataType = DetectColumnType(worksheet, colNum, dataStartRow, lastRow.RowNumber());

                    // Detect if column could be a primary key (unique, non-null values)
                    var isPrimaryKey = DetectPrimaryKey(worksheet, colNum, dataStartRow, lastRow.RowNumber());

                    var column = new ColumnModel
                    {
                        Name = columnName,
                        DataType = dataType.Type,
                        MaxLength = dataType.MaxLength,
                        IsNullable = dataType.IsNullable,
                        IsPrimaryKey = isPrimaryKey,
                        IsForeignKey = false // We'll detect this later with AI
                    };

                    table.Columns.Add(column);
                }

                // Only add tables that have columns
                if (table.Columns.Any())
                {
                    tables.Add(table);
                }
            }

            return tables;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error processing Excel file: {ex.Message}", ex);
        }
    }

    public ExcelWorkbookInfo GetExcelWorkbookInfo(Stream excelStream)
    {
        try
        {
            using var workbook = new XLWorkbook(excelStream);

            var workbookInfo = new ExcelWorkbookInfo
            {
                FileName = "Excel Workbook",
                LastModified = DateTime.Now,
                FileSize = excelStream.Length
            };

            foreach (var worksheet in workbook.Worksheets)
            {
                var lastRow = worksheet.LastRowUsed();
                var lastColumn = worksheet.LastColumnUsed();

                if (lastRow == null || lastColumn == null) continue;

                var hasHeaders = DetectHeaders(worksheet);
                var columnNames = new List<string>();
                var dataTypes = new List<string>();

                // Get column names and types
                for (int colNum = 1; colNum <= lastColumn.ColumnNumber(); colNum++)
                {
                    if (hasHeaders)
                    {
                        var headerCell = worksheet.Cell(1, colNum);
                        var columnName = headerCell.Value.ToString().Trim();
                        columnNames.Add(string.IsNullOrWhiteSpace(columnName) ? $"Column{colNum}" : columnName);
                    }
                    else
                    {
                        columnNames.Add($"Column{colNum}");
                    }

                    var dataType = DetectColumnType(worksheet, colNum, hasHeaders ? 2 : 1, lastRow.RowNumber());
                    dataTypes.Add(dataType.Type);
                }

                var sheetInfo = new ExcelSheetInfo
                {
                    Name = worksheet.Name,
                    ColumnCount = lastColumn.ColumnNumber(),
                    RowCount = lastRow.RowNumber() - (hasHeaders ? 1 : 0),
                    HasHeader = hasHeaders,
                    ColumnNames = columnNames,
                    DataTypes = dataTypes
                };

                workbookInfo.Sheets.Add(sheetInfo);
            }

            return workbookInfo;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error analyzing Excel workbook: {ex.Message}", ex);
        }
    }

    public List<object[]> LoadExcelPreviewData(Stream excelStream, string sheetName, int maxRows = 50)
    {
        var previewData = new List<object[]>();

        try
        {
            using var workbook = new XLWorkbook(excelStream);
            var worksheet = workbook.Worksheets.FirstOrDefault(w => w.Name == sheetName);

            if (worksheet == null)
                return previewData;

            var lastRow = worksheet.LastRowUsed();
            var lastColumn = worksheet.LastColumnUsed();

            if (lastRow == null || lastColumn == null)
                return previewData;

            bool hasHeaders = DetectHeaders(worksheet);
            int startRow = hasHeaders ? 2 : 1;
            int endRow = Math.Min(lastRow.RowNumber(), startRow + maxRows - 1);

            for (int rowNum = startRow; rowNum <= endRow; rowNum++)
            {
                var rowData = new object[lastColumn.ColumnNumber()];

                for (int colNum = 1; colNum <= lastColumn.ColumnNumber(); colNum++)
                {
                    var cell = worksheet.Cell(rowNum, colNum);
                    rowData[colNum - 1] = GetCellValue(cell);
                }

                previewData.Add(rowData);
            }

            return previewData;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error loading preview data: {ex.Message}", ex);
        }
    }

    private bool DetectHeaders(IXLWorksheet worksheet)
    {
        var firstRow = worksheet.FirstRowUsed();
        var secondRow = firstRow?.RowBelow();

        if (firstRow == null || secondRow == null) return false;

        var lastColumn = worksheet.LastColumnUsed();
        if (lastColumn == null) return false;

        int textInFirst = 0;
        int textInSecond = 0;
        int totalColumns = lastColumn.ColumnNumber();

        // Compare data types in first two rows
        for (int colNum = 1; colNum <= totalColumns; colNum++)
        {
            var firstCell = firstRow.Cell(colNum);
            var secondCell = secondRow.Cell(colNum);

            if (firstCell.DataType == XLDataType.Text && !string.IsNullOrWhiteSpace(firstCell.Value.ToString()))
                textInFirst++;

            if (secondCell.DataType == XLDataType.Text && !string.IsNullOrWhiteSpace(secondCell.Value.ToString()))
                textInSecond++;
        }

        // If first row has significantly more text than second, likely headers
        return textInFirst > (totalColumns * 0.7) && textInFirst > textInSecond;
    }

    private (string Type, int? MaxLength, bool IsNullable) DetectColumnType(IXLWorksheet worksheet, int columnNumber, int startRow, int endRow)
    {
        var sampleSize = Math.Min(100, endRow - startRow + 1);
        var hasDate = false;
        var hasNumber = false;
        var hasBoolean = false;
        var hasText = false;
        var hasNull = false;
        var maxLength = 0;
        var sampleCount = 0;

        for (int row = startRow; row <= Math.Min(startRow + sampleSize - 1, endRow); row++)
        {
            var cell = worksheet.Cell(row, columnNumber);
            sampleCount++;

            if (cell.IsEmpty() || string.IsNullOrWhiteSpace(cell.Value.ToString()))
            {
                hasNull = true;
                continue;
            }

            var value = cell.Value.ToString();
            maxLength = Math.Max(maxLength, value.Length);

            switch (cell.DataType)
            {
                case XLDataType.DateTime:
                    hasDate = true;
                    break;
                case XLDataType.Number:
                    hasNumber = true;
                    break;
                case XLDataType.Boolean:
                    hasBoolean = true;
                    break;
                case XLDataType.Text:
                    hasText = true;
                    // Check if text looks like a boolean
                    if (IsBooleanText(value))
                        hasBoolean = true;
                    // Check if text looks like a number
                    else if (IsNumericText(value))
                        hasNumber = true;
                    break;
            }
        }

        // Determine the most appropriate SQL data type
        if (hasDate && !hasText && !hasNumber)
            return ("datetime2", null, hasNull);

        if (hasBoolean && !hasText && !hasNumber && !hasDate)
            return ("bit", null, hasNull);

        if (hasNumber && !hasText && !hasDate)
        {
            // Determine if it's integer or decimal
            bool hasDecimals = false;
            for (int row = startRow; row <= Math.Min(startRow + sampleSize - 1, endRow); row++)
            {
                var cell = worksheet.Cell(row, columnNumber);
                if (!cell.IsEmpty() && cell.DataType == XLDataType.Number)
                {
                    if (cell.Value.ToString().Contains("."))
                    {
                        hasDecimals = true;
                        break;
                    }
                }
            }
            return (hasDecimals ? "decimal(18,2)" : "int", null, hasNull);
        }

        // Default to text with appropriate length
        if (maxLength <= 50)
            return ("nvarchar(50)", 50, hasNull);
        else if (maxLength <= 255)
            return ("nvarchar(255)", 255, hasNull);
        else if (maxLength <= 4000)
            return ("nvarchar(4000)", 4000, hasNull);
        else
            return ("nvarchar(max)", null, hasNull);
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

    private string SanitizeSheetName(string sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName))
            return "Sheet1";

        // Remove invalid characters for SQL table names
        var sanitized = System.Text.RegularExpressions.Regex.Replace(sheetName, @"[^\w\s]", "");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\s+", "_");

        // Ensure it starts with a letter
        if (!char.IsLetter(sanitized[0]))
            sanitized = "T_" + sanitized;

        return sanitized.Length > 50 ? sanitized.Substring(0, 50) : sanitized;
    }

    private string SanitizeColumnName(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return "Column1";

        // Remove invalid characters for SQL column names
        var sanitized = System.Text.RegularExpressions.Regex.Replace(columnName, @"[^\w\s]", "");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\s+", "_");

        // Ensure it starts with a letter
        if (!char.IsLetter(sanitized[0]))
            sanitized = "C_" + sanitized;

        return sanitized.Length > 50 ? sanitized.Substring(0, 50) : sanitized;
    }

    private bool IsBooleanText(string value)
    {
        var lower = value.ToLower().Trim();
        return lower == "true" || lower == "false" || lower == "yes" || lower == "no" ||
               lower == "1" || lower == "0" || lower == "y" || lower == "n";
    }

    private bool IsNumericText(string value)
    {
        return decimal.TryParse(value, out _);
    }

    private object GetCellValue(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;

        return cell.DataType switch
        {
            XLDataType.Text => cell.Value.ToString(),
            XLDataType.Number => cell.Value.GetNumber(),
            XLDataType.Boolean => cell.Value.GetBoolean(),
            XLDataType.DateTime => cell.Value.GetDateTime(),
            _ => cell.Value.ToString()
        };
    }

    private string GenerateAlias(string tableName)
    {
        var words = tableName.Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length > 1)
        {
            // Take first letter of each word
            return string.Join("", words.Take(3).Select(w => w[0])).ToLower();
        }

        // For single words, take first few characters
        return tableName.Length >= 3 ? tableName.Substring(0, 3).ToLower() : tableName.ToLower();
    }

    private bool DetectPrimaryKey(IXLWorksheet worksheet, int columnNumber, int startRow, int endRow)
    {
        var values = new HashSet<string>();
        var sampleSize = Math.Min(50, endRow - startRow + 1);

        for (int row = startRow; row <= Math.Min(startRow + sampleSize - 1, endRow); row++)
        {
            var cell = worksheet.Cell(row, columnNumber);

            if (cell.IsEmpty() || string.IsNullOrWhiteSpace(cell.Value.ToString()))
                return false; // Nulls can't be primary key

            var value = cell.Value.ToString();

            if (values.Contains(value))
                return false; // Duplicates can't be primary key

            values.Add(value);
        }

        // If all values are unique and non-null, could be primary key
        // Additional heuristics: column name contains "id", "key", etc.
        var columnName = worksheet.Cell(1, columnNumber).Value.ToString().ToLower();
        return values.Count > 0 && (columnName.Contains("id") || columnName.Contains("key") || columnName == "pk");
    }

    public DataTable GenerateExcelMockResults(List<TableModel> tables)
    {
        var dataTable = new DataTable();
        var selectedColumns = new List<(TableModel table, ColumnModel column)>();

        // Collect all selected columns from all tables
        foreach (var table in tables.Where(t => t.IsFromExcel))
        {
            foreach (var column in table.Columns.Where(c => c.IsSelected))
            {
                var columnName = string.IsNullOrEmpty(column.QueryAlias) ? column.Name : column.QueryAlias;

                // Handle duplicate column names by prefixing with table name
                var finalColumnName = columnName;
                if (tables.Count > 1)
                {
                    finalColumnName = $"{table.Name}_{columnName}";
                }

                // Ensure unique column names
                var counter = 1;
                var originalName = finalColumnName;
                while (dataTable.Columns.Contains(finalColumnName))
                {
                    finalColumnName = $"{originalName}_{counter}";
                    counter++;
                }

                var columnType = GetSystemTypeFromSqlType(column.DataType);
                dataTable.Columns.Add(finalColumnName, columnType);
                selectedColumns.Add((table, column));
            }
        }

        if (dataTable.Columns.Count == 0)
        {
            // No columns selected, show table structure info
            dataTable.Columns.Add("Table_Name", typeof(string));
            dataTable.Columns.Add("Column_Count", typeof(int));
            dataTable.Columns.Add("Estimated_Rows", typeof(int));
            dataTable.Columns.Add("Data_Source", typeof(string));

            foreach (var table in tables.Where(t => t.IsFromExcel))
            {
                var row = dataTable.NewRow();
                row["Table_Name"] = table.Name;
                row["Column_Count"] = table.Columns.Count;
                row["Estimated_Rows"] = table.RowCount ?? 0;
                row["Data_Source"] = "Excel Sheet";
                dataTable.Rows.Add(row);
            }
        }
        else
        {
            // Generate sample rows based on data types
            var sampleRowCount = Math.Min(10, tables.Max(t => t.RowCount ?? 5));

            for (int rowIndex = 0; rowIndex < sampleRowCount; rowIndex++)
            {
                var row = dataTable.NewRow();

                for (int colIndex = 0; colIndex < selectedColumns.Count; colIndex++)
                {
                    var (table, column) = selectedColumns[colIndex];
                    row[colIndex] = GenerateSampleValue(column.DataType, rowIndex);
                }

                dataTable.Rows.Add(row);
            }
        }

        return dataTable;
    }

    public DataTable GenerateMixedMockResults(List<TableModel> tables)
    {
        var dataTable = new DataTable();

        // Add informational columns
        dataTable.Columns.Add("Query_Component", typeof(string));
        dataTable.Columns.Add("Table_Name", typeof(string));
        dataTable.Columns.Add("Data_Source", typeof(string));
        dataTable.Columns.Add("Selected_Columns", typeof(string));
        dataTable.Columns.Add("Status", typeof(string));

        foreach (var table in tables)
        {
            var selectedCols = table.Columns.Where(c => c.IsSelected).Select(c => c.Name);
            var row = dataTable.NewRow();

            row["Query_Component"] = table.IsFromExcel ? "FROM Excel" : "FROM Database";
            row["Table_Name"] = table.Name;
            row["Data_Source"] = table.IsFromExcel ? "Excel Sheet" : "SQL Server";
            row["Selected_Columns"] = string.Join(", ", selectedCols);
            row["Status"] = table.IsFromExcel ? "Structure Only" : "Requires Connection";

            dataTable.Rows.Add(row);
        }

        // Add note about mixed query
        var noteRow = dataTable.NewRow();
        noteRow["Query_Component"] = "NOTE";
        noteRow["Table_Name"] = "Mixed Data Sources";
        noteRow["Data_Source"] = "Excel + SQL Server";
        noteRow["Selected_Columns"] = "To execute this query, Excel data needs to be imported to SQL Server";
        noteRow["Status"] = "Integration Required";
        dataTable.Rows.Add(noteRow);

        return dataTable;
    }

    private Type GetSystemTypeFromSqlType(string sqlType)
    {
        var lowerType = sqlType.ToLower();

        if (lowerType.StartsWith("nvarchar") || lowerType.StartsWith("varchar") ||
            lowerType.StartsWith("char") || lowerType.StartsWith("text"))
            return typeof(string);

        if (lowerType.StartsWith("int") || lowerType == "bigint")
            return typeof(int);

        if (lowerType.StartsWith("decimal") || lowerType.StartsWith("float") || lowerType == "real")
            return typeof(decimal);

        if (lowerType.StartsWith("datetime") || lowerType == "date" || lowerType == "time")
            return typeof(DateTime);

        if (lowerType == "bit")
            return typeof(bool);

        if (lowerType == "uniqueidentifier")
            return typeof(Guid);

        return typeof(string); // Default fallback
    }

    private object GenerateSampleValue(string dataType, int rowIndex)
    {
        var lowerType = dataType.ToLower();

        if (lowerType.StartsWith("nvarchar") || lowerType.StartsWith("varchar") ||
            lowerType.StartsWith("char") || lowerType.StartsWith("text"))
            return $"Sample Text {rowIndex + 1}";

        if (lowerType.StartsWith("int") || lowerType == "bigint")
            return 1000 + rowIndex;

        if (lowerType.StartsWith("decimal") || lowerType.StartsWith("float") || lowerType == "real")
            return Math.Round(100.50m + (rowIndex * 10.25m), 2);

        if (lowerType.StartsWith("datetime") || lowerType == "date" || lowerType == "time")
            return DateTime.Now.AddDays(-rowIndex);

        if (lowerType == "bit")
            return rowIndex % 2 == 0;

        if (lowerType == "uniqueidentifier")
            return Guid.NewGuid();

        return $"Value {rowIndex + 1}";
    }
}

// Extension methods and helper classes
public static class TableModelExtensions
{
    public static void SetDefaultPositions(this List<TableModel> tables)
    {
        int x = 50, y = 50;
        int maxHeight = 0;
        const int tableWidth = 280;
        const int tableSpacing = 320;
        const int rowSpacing = 180;
        const int tablesPerRow = 4;

        for (int i = 0; i < tables.Count; i++)
        {
            var table = tables[i];

            // Set default size if not set
            if (table.Size.Width == 0)
            {
                table.Size.Width = tableWidth;
                table.Size.Height = Math.Max(150, 40 + (table.Columns.Count * 30));
            }

            table.Position = new Position { X = x, Y = y };

            x += tableSpacing;
            maxHeight = Math.Max(maxHeight, table.Size.Height);

            if ((i + 1) % tablesPerRow == 0)
            {
                x = 50;
                y += maxHeight + rowSpacing;
                maxHeight = 0;
            }
        }
    }
}

