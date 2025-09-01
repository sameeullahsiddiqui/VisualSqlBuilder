using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VisualSqlBuilder.Core.Models;

namespace VisualSqlBuilder.Core.Services;

public interface ISqlGeneratorService
{
    string GenerateQuery(QueryModel queryModel);
    string GenerateQueryWithGrouping(QueryModel queryModel, List<string> groupByColumns, List<AggregateFunction> aggregates);
    string GenerateInsertQuery(TableModel table, Dictionary<string, object> values);
    string GenerateUpdateQuery(TableModel table, Dictionary<string, object> values, Dictionary<string, object> whereConditions);
    string GenerateDeleteQuery(TableModel table, Dictionary<string, object> whereConditions);
}

public class SqlGeneratorService : ISqlGeneratorService
{
    public string GenerateQuery(QueryModel queryModel)
    {
        try
        {
            var relevantTables = GetRelevantTables(queryModel);

            if (!relevantTables.Any())
            {
                return "-- No tables with selected columns or relationships found.\n-- Please select columns or create relationships between tables.";
            }

            var sql = new StringBuilder();

            BuildSelectClause(sql, relevantTables);
            BuildFromClause(sql, relevantTables, queryModel.Relationships);
            BuildJoinClauses(sql, relevantTables, queryModel.Relationships);
            BuildWhereClause(sql, relevantTables);

            return sql.ToString();
        }
        catch (Exception ex)
        {
            return $"-- Error generating SQL: {ex.Message}";
        }
    }

    private List<TableModel> GetRelevantTables(QueryModel queryModel)
    {
        // Include tables that have selected columns
        var tablesWithSelectedColumns = queryModel.Tables
            .Where(t => t.Columns.Any(c => c.IsSelected))
            .ToList();

        if (!tablesWithSelectedColumns.Any())
        {
            return new List<TableModel>();
        }

        var connectedTableIds = new HashSet<string>();

        // Find all tables connected through relationships to tables with selected columns
        foreach (var table in tablesWithSelectedColumns)
        {
            AddConnectedTables(table.Id, queryModel.Relationships, connectedTableIds, queryModel.Tables);
        }

        var relevantTables = queryModel.Tables
            .Where(t => tablesWithSelectedColumns.Contains(t) || connectedTableIds.Contains(t.Id))
            .ToList();

        return relevantTables;
    }

    private void AddConnectedTables(string tableId, List<RelationshipModel> relationships,
        HashSet<string> connectedTableIds, List<TableModel> allTables)
    {
        if (connectedTableIds.Contains(tableId)) return;

        connectedTableIds.Add(tableId);

        // Find directly connected tables through relationships
        var directlyConnected = relationships
            .Where(r => r.SourceTableId == tableId || r.TargetTableId == tableId)
            .SelectMany(r => new[] { r.SourceTableId, r.TargetTableId })
            .Where(id => id != tableId && !connectedTableIds.Contains(id) &&
                   allTables.Any(t => t.Id == id))
            .ToList();

        foreach (var connectedId in directlyConnected)
        {
            AddConnectedTables(connectedId, relationships, connectedTableIds, allTables);
        }
    }

    private void BuildSelectClause(StringBuilder sql, List<TableModel> tables)
    {
        sql.AppendLine("SELECT");

        var selectedColumns = new List<string>();

        foreach (var table in tables.OrderBy(t => t.Name))
        {
            var tableAlias = GetTableAlias(table);

            foreach (var column in table.Columns.Where(c => c.IsSelected).OrderBy(c => c.Name))
            {
                string columnExpression;

                if (column.IsComputed && !string.IsNullOrEmpty(column.ComputedExpression))
                {
                    // For computed columns, use the expression directly
                    columnExpression = $"({column.ComputedExpression})";
                }
                else
                {
                    columnExpression = $"{tableAlias}.{EscapeIdentifier(column.Name)}";
                }

                var columnAlias = !string.IsNullOrEmpty(column.QueryAlias) && column.QueryAlias != column.Name
                    ? $" AS {EscapeIdentifier(column.QueryAlias)}"
                    : "";

                selectedColumns.Add($"    {columnExpression}{columnAlias}");
            }
        }

        if (!selectedColumns.Any())
        {
            // Fallback: select primary keys or first column from each table
            foreach (var table in tables)
            {
                var tableAlias = GetTableAlias(table);
                var primaryKey = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
                var columnToSelect = primaryKey ?? table.Columns.FirstOrDefault();

                if (columnToSelect != null)
                {
                    selectedColumns.Add($"    {tableAlias}.{EscapeIdentifier(columnToSelect.Name)}");
                }
            }
        }

        sql.AppendLine(string.Join(",\n", selectedColumns));
    }

    private void BuildFromClause(StringBuilder sql, List<TableModel> tables, List<RelationshipModel> relationships)
    {
        var mainTable = FindMainTable(tables, relationships);

        if (mainTable != null)
        {
            var tableAlias = GetTableAlias(mainTable);
            var schemaPrefix = !string.IsNullOrEmpty(mainTable.Schema) ? $"{EscapeIdentifier(mainTable.Schema)}." : "";

            sql.AppendLine($"FROM {schemaPrefix}{EscapeIdentifier(mainTable.Name)} AS {tableAlias}");
        }
    }

    private void BuildJoinClauses(StringBuilder sql, List<TableModel> tables, List<RelationshipModel> relationships)
    {
        var processedTables = new HashSet<string>();
        var mainTable = FindMainTable(tables, relationships);

        if (mainTable != null)
        {
            processedTables.Add(mainTable.Id);
            BuildJoinsRecursively(sql, mainTable, tables, relationships, processedTables);
        }
    }

    private void BuildJoinsRecursively(StringBuilder sql, TableModel currentTable, List<TableModel> allTables,
        List<RelationshipModel> relationships, HashSet<string> processedTables)
    {
        var relatedRelationships = relationships
            .Where(r =>
                (r.SourceTableId == currentTable.Id || r.TargetTableId == currentTable.Id) &&
                (!processedTables.Contains(r.SourceTableId) || !processedTables.Contains(r.TargetTableId))
            )
            .OrderBy(r => r.JoinType) // Process inner joins first
            .ToList();

        foreach (var relationship in relatedRelationships)
        {
            var isSourceTable = relationship.SourceTableId == currentTable.Id;
            var joinTableId = isSourceTable ? relationship.TargetTableId : relationship.SourceTableId;
            var joinTable = allTables.FirstOrDefault(t => t.Id == joinTableId);

            if (joinTable != null && !processedTables.Contains(joinTable.Id))
            {
                processedTables.Add(joinTable.Id);

                var joinType = GetJoinTypeKeyword(relationship.JoinType);
                var joinTableAlias = GetTableAlias(joinTable);
                var currentTableAlias = GetTableAlias(currentTable);

                var schemaPrefix = !string.IsNullOrEmpty(joinTable.Schema) ? $"{EscapeIdentifier(joinTable.Schema)}." : "";

                // Build join condition
                var leftColumn = isSourceTable
                    ? $"{currentTableAlias}.{EscapeIdentifier(GetColumnName(currentTable, relationship.SourceColumnId))}"
                    : $"{joinTableAlias}.{EscapeIdentifier(GetColumnName(joinTable, relationship.SourceColumnId))}";

                var rightColumn = isSourceTable
                    ? $"{joinTableAlias}.{EscapeIdentifier(GetColumnName(joinTable, relationship.TargetColumnId))}"
                    : $"{currentTableAlias}.{EscapeIdentifier(GetColumnName(currentTable, relationship.TargetColumnId))}";

                sql.AppendLine($"{joinType} {schemaPrefix}{EscapeIdentifier(joinTable.Name)} AS {joinTableAlias}");
                sql.AppendLine($"    ON {leftColumn} = {rightColumn}");

                // Continue recursively for this table
                BuildJoinsRecursively(sql, joinTable, allTables, relationships, processedTables);
            }
        }
    }

    private void BuildWhereClause(StringBuilder sql, List<TableModel> tables)
    {
        var whereConditions = new List<string>();

        foreach (var table in tables)
        {
            var tableAlias = GetTableAlias(table);

            foreach (var column in table.Columns.Where(c => c.Filter != null && c.IsSelected))
            {
                var condition = BuildFilterCondition(tableAlias, column);
                if (!string.IsNullOrEmpty(condition))
                {
                    whereConditions.Add(condition);
                }
            }
        }

        if (whereConditions.Any())
        {
            sql.AppendLine("WHERE");
            sql.AppendLine($"    {string.Join("\n    AND ", whereConditions)}");
        }
    }

    private TableModel? FindMainTable(List<TableModel> tables, List<RelationshipModel> relationships)
    {
        // Priority 1: Table with most selected columns
        var tablesWithSelectedColumns = tables
            .Where(t => t.Columns.Any(c => c.IsSelected))
            .OrderByDescending(t => t.Columns.Count(c => c.IsSelected))
            .ToList();

        if (tablesWithSelectedColumns.Any())
        {
            return tablesWithSelectedColumns.First();
        }

        // Priority 2: Table with most outgoing relationships (likely a parent table)
        var tableRelationshipCounts = tables.ToDictionary(t => t.Id, t =>
            relationships.Count(r => r.SourceTableId == t.Id));

        var mostConnectedTable = tables
            .OrderByDescending(t => tableRelationshipCounts[t.Id])
            .FirstOrDefault();

        return mostConnectedTable ?? tables.FirstOrDefault();
    }

    public string GenerateQueryWithGrouping(QueryModel queryModel, List<string> groupByColumns, List<AggregateFunction> aggregates)
    {
        try
        {
            var relevantTables = GetRelevantTables(queryModel);
            if (!relevantTables.Any()) return "-- No relevant tables found";

            var sql = new StringBuilder();

            BuildSelectClauseWithAggregates(sql, relevantTables, aggregates);
            BuildFromClause(sql, relevantTables, queryModel.Relationships);
            BuildJoinClauses(sql, relevantTables, queryModel.Relationships);
            BuildWhereClause(sql, relevantTables);

            if (groupByColumns.Any())
            {
                sql.AppendLine("GROUP BY");
                sql.AppendLine($"    {string.Join(",\n    ", groupByColumns)}");
            }

            return sql.ToString();
        }
        catch (Exception ex)
        {
            return $"-- Error generating grouped query: {ex.Message}";
        }
    }

    private void BuildSelectClauseWithAggregates(StringBuilder sql, List<TableModel> tables, List<AggregateFunction> aggregates)
    {
        sql.AppendLine("SELECT");
        var selectedColumns = new List<string>();

        // Add regular selected columns (typically for GROUP BY)
        foreach (var table in tables)
        {
            var tableAlias = GetTableAlias(table);
            foreach (var column in table.Columns.Where(c => c.IsSelected && !aggregates.Any(a => a.ColumnId == c.Id)))
            {
                var columnExpression = $"{tableAlias}.{EscapeIdentifier(column.Name)}";
                var columnAlias = !string.IsNullOrEmpty(column.QueryAlias) && column.QueryAlias != column.Name
                    ? $" AS {EscapeIdentifier(column.QueryAlias)}"
                    : "";

                selectedColumns.Add($"    {columnExpression}{columnAlias}");
            }
        }

        // Add aggregate functions
        foreach (var aggregate in aggregates)
        {
            var table = tables.FirstOrDefault(t => t.Columns.Any(c => c.Id == aggregate.ColumnId));
            var column = table?.Columns.FirstOrDefault(c => c.Id == aggregate.ColumnId);

            if (table != null && column != null)
            {
                var tableAlias = GetTableAlias(table);
                var columnRef = $"{tableAlias}.{EscapeIdentifier(column.Name)}";
                var aggregateExpression = $"{aggregate.Function}({columnRef})";
                var aggregateAlias = !string.IsNullOrEmpty(aggregate.Alias)
                    ? $" AS {EscapeIdentifier(aggregate.Alias)}"
                    : $" AS {aggregate.Function}_{column.Name}";

                selectedColumns.Add($"    {aggregateExpression}{aggregateAlias}");
            }
        }

        sql.AppendLine(string.Join(",\n", selectedColumns));
    }

    public string GenerateInsertQuery(TableModel table, Dictionary<string, object> values)
    {
        try
        {
            var sql = new StringBuilder();
            var schemaPrefix = !string.IsNullOrEmpty(table.Schema) ? $"{EscapeIdentifier(table.Schema)}." : "";

            sql.AppendLine($"INSERT INTO {schemaPrefix}{EscapeIdentifier(table.Name)}");

            var insertableColumns = values.Keys
                .Where(k => table.Columns.Any(c => c.Name == k && !c.IsComputed))
                .ToList();

            if (!insertableColumns.Any())
            {
                return "-- No insertable columns found (computed columns cannot be inserted)";
            }

            var columnList = string.Join(", ", insertableColumns.Select(EscapeIdentifier));
            var valueList = string.Join(", ", insertableColumns.Select(c => FormatValue(values[c])));

            sql.AppendLine($"    ({columnList})");
            sql.AppendLine($"VALUES");
            sql.AppendLine($"    ({valueList});");

            return sql.ToString();
        }
        catch (Exception ex)
        {
            return $"-- Error generating INSERT: {ex.Message}";
        }
    }

    public string GenerateUpdateQuery(TableModel table, Dictionary<string, object> values, Dictionary<string, object> whereConditions)
    {
        try
        {
            var sql = new StringBuilder();
            var schemaPrefix = !string.IsNullOrEmpty(table.Schema) ? $"{EscapeIdentifier(table.Schema)}." : "";

            sql.AppendLine($"UPDATE {schemaPrefix}{EscapeIdentifier(table.Name)}");
            sql.AppendLine("SET");

            var updatableColumns = values.Keys
                .Where(k => table.Columns.Any(c => c.Name == k && !c.IsComputed && !c.IsPrimaryKey))
                .ToList();

            if (!updatableColumns.Any())
            {
                return "-- No updatable columns found (computed columns and primary keys cannot be updated)";
            }

            var setValues = updatableColumns
                .Select(k => $"    {EscapeIdentifier(k)} = {FormatValue(values[k])}")
                .ToList();

            sql.AppendLine(string.Join(",\n", setValues));

            if (whereConditions.Any())
            {
                sql.AppendLine("WHERE");
                var whereClause = whereConditions
                    .Select(kvp => $"    {EscapeIdentifier(kvp.Key)} = {FormatValue(kvp.Value)}")
                    .ToList();
                sql.AppendLine(string.Join("\n    AND ", whereClause));
            }
            else
            {
                sql.AppendLine("-- WARNING: No WHERE clause specified. This will update ALL records!");
            }

            return sql.ToString();
        }
        catch (Exception ex)
        {
            return $"-- Error generating UPDATE: {ex.Message}";
        }
    }

    public string GenerateDeleteQuery(TableModel table, Dictionary<string, object> whereConditions)
    {
        try
        {
            var sql = new StringBuilder();
            var schemaPrefix = !string.IsNullOrEmpty(table.Schema) ? $"{EscapeIdentifier(table.Schema)}." : "";

            sql.AppendLine($"DELETE FROM {schemaPrefix}{EscapeIdentifier(table.Name)}");

            if (whereConditions.Any())
            {
                sql.AppendLine("WHERE");
                var whereClause = whereConditions
                    .Select(kvp => $"    {EscapeIdentifier(kvp.Key)} = {FormatValue(kvp.Value)}")
                    .ToList();
                sql.AppendLine(string.Join("\n    AND ", whereClause));
            }
            else
            {
                sql.AppendLine("-- WARNING: No WHERE clause specified. This will delete ALL records!");
                sql.AppendLine("-- Uncomment the line below to proceed:");
                sql.AppendLine("-- WHERE 1=1;");
            }

            return sql.ToString();
        }
        catch (Exception ex)
        {
            return $"-- Error generating DELETE: {ex.Message}";
        }
    }

    private string GetTableAlias(TableModel table)
    {
        // Use alias if it's different from table name, otherwise use table name
        if (!string.IsNullOrEmpty(table.Alias) && table.Alias != table.Name)
        {
            return EscapeIdentifier(table.Alias);
        }

        // Generate short alias from table name if no custom alias
        var alias = GenerateShortAlias(table.Name);
        return EscapeIdentifier(alias);
    }

    private string GenerateShortAlias(string tableName)
    {
        // Generate meaningful short aliases
        var words = tableName.Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length > 1)
        {
            // Take first letter of each word
            return string.Join("", words.Select(w => w[0])).ToLower();
        }

        // For single words, take first few characters
        return tableName.Length >= 3 ? tableName.Substring(0, 3).ToLower() : tableName.ToLower();
    }

    private string GetColumnName(TableModel table, string columnId)
    {
        var column = table.Columns.FirstOrDefault(c => c.Id == columnId);
        return column?.Name ?? "UnknownColumn";
    }

    private string GetJoinTypeKeyword(JoinType joinType)
    {
        return joinType switch
        {
            JoinType.InnerJoin => "INNER JOIN",
            JoinType.LeftJoin => "LEFT JOIN",
            JoinType.RightJoin => "RIGHT JOIN",
            JoinType.FullOuterJoin => "FULL OUTER JOIN",
            JoinType.CrossJoin => "CROSS JOIN",
            _ => "INNER JOIN"
        };
    }

    private string BuildFilterCondition(string tableAlias, ColumnModel column)
    {
        if (column.Filter == null) return "";

        var columnRef = $"{tableAlias}.{EscapeIdentifier(column.Name)}";

        return column.Filter.Operator switch
        {
            "=" => $"{columnRef} = {FormatValue(column.Filter.Value)}",
            "!=" => $"{columnRef} != {FormatValue(column.Filter.Value)}",
            ">" => $"{columnRef} > {FormatValue(column.Filter.Value)}",
            "<" => $"{columnRef} < {FormatValue(column.Filter.Value)}",
            ">=" => $"{columnRef} >= {FormatValue(column.Filter.Value)}",
            "<=" => $"{columnRef} <= {FormatValue(column.Filter.Value)}",
            "LIKE" => $"{columnRef} LIKE '%{EscapeString(column.Filter.Value)}%'",
            "NOT LIKE" => $"{columnRef} NOT LIKE '%{EscapeString(column.Filter.Value)}%'",
            "IS NULL" => $"{columnRef} IS NULL",
            "IS NOT NULL" => $"{columnRef} IS NOT NULL",
            "IN" => $"{columnRef} IN ({column.Filter.Value})", // Assumes comma-separated values
            "NOT IN" => $"{columnRef} NOT IN ({column.Filter.Value})",
            "BETWEEN" => column.Filter.SecondValue != null
                ? $"{columnRef} BETWEEN {FormatValue(column.Filter.Value)} AND {FormatValue(column.Filter.SecondValue)}"
                : $"{columnRef} = {FormatValue(column.Filter.Value)}",
            _ => $"{columnRef} = {FormatValue(column.Filter.Value)}"
        };
    }

    private string FormatValue(object value)
    {
        return value switch
        {
            null => "NULL",
            string s => $"'{EscapeString(s)}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            DateOnly d => $"'{d:yyyy-MM-dd}'",
            TimeOnly t => $"'{t:HH:mm:ss}'",
            bool b => b ? "1" : "0",
            decimal or double or float => value.ToString(),
            int or long or short or byte => value.ToString(),
            Guid g => $"'{g}'",
            _ => $"'{EscapeString(value.ToString())}'"
        };
    }

    private string EscapeIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]")}]";
    }

    private string EscapeString(string value)
    {
        return value?.Replace("'", "''") ?? "";
    }
}


