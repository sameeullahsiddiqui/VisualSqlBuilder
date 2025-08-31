using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VisualSqlBuilder.Core.Models;

namespace VisualSqlBuilder.Core.Services;

public interface ISqlGeneratorService
{
    string GenerateQuery(QueryModel model);
    string GenerateCreateTableScript(TableModel table);
}

public class SqlGeneratorService : ISqlGeneratorService
{
    public string GenerateQuery(QueryModel model)
    {
        if (model.Tables == null || !model.Tables.Any())
            return string.Empty;

        var sb = new StringBuilder();

        // SELECT clause
        sb.AppendLine("SELECT");
        var selectColumns = new List<string>();

        foreach (var table in model.Tables)
        {
            var tableAlias = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.Name;

            foreach (var column in table.Columns.Where(c => c.IsSelected))
            {
                if (column.IsComputed && !string.IsNullOrEmpty(column.ComputedExpression))
                {
                    selectColumns.Add($"    ({column.ComputedExpression}) AS [{column.Alias ?? column.Name}]");
                }
                else
                {
                    var columnAlias = !string.IsNullOrEmpty(column.Alias) ? column.Alias : column.Name;
                    selectColumns.Add($"    [{tableAlias}].[{column.Name}] AS [{columnAlias}]");
                }
            }
        }

        // Add predefined columns
        foreach (var predefined in model.PredefinedColumns)
        {
            selectColumns.Add($"    {predefined.Value} AS [{predefined.Key}]");
        }

        if (!selectColumns.Any())
        {
            selectColumns.Add("    *");
        }

        sb.AppendLine(string.Join(",\n", selectColumns));

        // FROM clause
        sb.AppendLine("FROM");
        var firstTable = model.Tables.First();
        var firstTableAlias = !string.IsNullOrEmpty(firstTable.Alias) ? firstTable.Alias : firstTable.Name;
        sb.AppendLine($"    [{firstTable.Schema}].[{firstTable.Name}] AS [{firstTableAlias}]");

        // JOIN clauses
        var processedTables = new HashSet<string> { firstTable.Id };

        foreach (var relationship in model.Relationships)
        {
            var sourceTable = model.Tables.FirstOrDefault(t => t.Id == relationship.SourceTableId);
            var targetTable = model.Tables.FirstOrDefault(t => t.Id == relationship.TargetTableId);

            if (sourceTable != null && targetTable != null)
            {
                var sourceColumn = sourceTable.Columns.FirstOrDefault(c => c.Id == relationship.SourceColumnId);
                var targetColumn = targetTable.Columns.FirstOrDefault(c => c.Id == relationship.TargetColumnId);

                if (sourceColumn != null && targetColumn != null)
                {
                    var joinType = GetJoinTypeString(relationship.JoinType);
                    var sourceAlias = !string.IsNullOrEmpty(sourceTable.Alias) ? sourceTable.Alias : sourceTable.Name;
                    var targetAlias = !string.IsNullOrEmpty(targetTable.Alias) ? targetTable.Alias : targetTable.Name;

                    if (!processedTables.Contains(targetTable.Id))
                    {
                        sb.AppendLine($"{joinType} [{targetTable.Schema}].[{targetTable.Name}] AS [{targetAlias}]");
                        sb.AppendLine($"    ON [{sourceAlias}].[{sourceColumn.Name}] = [{targetAlias}].[{targetColumn.Name}]");
                        processedTables.Add(targetTable.Id);
                    }
                }
            }
        }

        // WHERE clause
        var whereConditions = new List<string>();
        foreach (var table in model.Tables)
        {
            var tableAlias = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.Name;

            foreach (var column in table.Columns.Where(c => c.Filter != null))
            {
                var condition = GenerateFilterCondition(tableAlias, column.Name, column.Filter);
                if (!string.IsNullOrEmpty(condition))
                {
                    whereConditions.Add(condition);
                }
            }
        }

        if (whereConditions.Any())
        {
            sb.AppendLine("WHERE");
            sb.AppendLine("    " + string.Join(" AND\n    ", whereConditions));
        }

        // GROUP BY clause
        if (model.GroupByColumns != null && model.GroupByColumns.Any())
        {
            sb.AppendLine("GROUP BY");
            sb.AppendLine("    " + string.Join(", ", model.GroupByColumns));
        }

        // ORDER BY clause
        if (model.OrderByColumns != null && model.OrderByColumns.Any())
        {
            sb.AppendLine("ORDER BY");
            sb.AppendLine("    " + string.Join(", ", model.OrderByColumns));
        }

        return sb.ToString();
    }

    private string GetJoinTypeString(JoinType joinType)
    {
        return joinType switch
        {
            JoinType.Inner => "INNER JOIN",
            JoinType.Left => "LEFT JOIN",
            JoinType.Right => "RIGHT JOIN",
            JoinType.FullOuter => "FULL OUTER JOIN",
            _ => "INNER JOIN"
        };
    }

    private string GenerateFilterCondition(string tableAlias, string columnName, FilterCondition filter)
    {
        var column = $"[{tableAlias}].[{columnName}]";

        return filter.Operator.ToUpper() switch
        {
            "=" => $"{column} = '{filter.Value}'",
            "!=" or "<>" => $"{column} <> '{filter.Value}'",
            ">" => $"{column} > '{filter.Value}'",
            ">=" => $"{column} >= '{filter.Value}'",
            "<" => $"{column} < '{filter.Value}'",
            "<=" => $"{column} <= '{filter.Value}'",
            "LIKE" => $"{column} LIKE '{filter.Value}'",
            "NOT LIKE" => $"{column} NOT LIKE '{filter.Value}'",
            "IN" => $"{column} IN ({filter.Value})",
            "NOT IN" => $"{column} NOT IN ({filter.Value})",
            "BETWEEN" => $"{column} BETWEEN '{filter.Value}' AND '{filter.SecondValue}'",
            "IS NULL" => $"{column} IS NULL",
            "IS NOT NULL" => $"{column} IS NOT NULL",
            _ => $"{column} = '{filter.Value}'"
        };
    }

    public string GenerateCreateTableScript(TableModel table)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE [{table.Schema}].[{table.Name}] (");

        var columnDefs = new List<string>();
        foreach (var column in table.Columns)
        {
            var def = $"    [{column.Name}] {column.DataType}";

            if (column.MaxLength.HasValue)
            {
                def += $"({column.MaxLength})";
            }

            if (!column.IsNullable)
            {
                def += " NOT NULL";
            }

            if (column.IsPrimaryKey)
            {
                def += " PRIMARY KEY";
            }

            columnDefs.Add(def);
        }

        // Add metadata columns
        columnDefs.Add("    [CreatedBy] NVARCHAR(255)");
        columnDefs.Add("    [CreatedAt] DATETIME2 DEFAULT GETDATE()");
        columnDefs.Add("    [ModifiedBy] NVARCHAR(255)");
        columnDefs.Add("    [ModifiedAt] DATETIME2 DEFAULT GETDATE()");

        sb.AppendLine(string.Join(",\n", columnDefs));
        sb.AppendLine(");");

        return sb.ToString();
    }
}