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
    private readonly HashSet<string> _usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _tableAliasCache = new Dictionary<string, string>();

    public string GenerateQuery(QueryModel queryModel)
    {
        try
        {
            // Clear alias tracking at the very beginning
            ClearAliasTracking();

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

    private void ClearAliasTracking()
    {
        _usedAliases.Clear();
        _tableAliasCache.Clear();
    }

    
    private string GetTableAliasFromCache(string tableId)
    {
        if (_tableAliasCache.TryGetValue(tableId, out string alias))
        {
            return EscapeIdentifier(alias);
        }
        // This shouldn't happen if aliases are generated properly
        return EscapeIdentifier("unknown");
    }

    private string EnsureUniqueAlias(string baseAlias)
    {
        if (!_usedAliases.Contains(baseAlias))
        {
            return baseAlias;
        }

        // If base alias is already used, append a number
        int counter = 1;
        string uniqueAlias;

        do
        {
            uniqueAlias = $"{baseAlias}{counter}";
            counter++;
        }
        while (_usedAliases.Contains(uniqueAlias));

        return uniqueAlias;
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
        var usedColumnNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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

                string finalColumnName;
                if (!string.IsNullOrEmpty(column.QueryAlias) && column.QueryAlias != column.Name)
                {
                    finalColumnName = column.QueryAlias;
                }
                else
                {
                    finalColumnName = column.Name;
                }

                // Handle duplicate column names
                string uniqueColumnName = GetUniqueColumnName(finalColumnName, table.Name, usedColumnNames);

                // Build the column expression with alias if needed
                string columnWithAlias;
                if (uniqueColumnName != finalColumnName)
                {
                    // Need to add/modify alias due to duplication
                    columnWithAlias = $"{columnExpression} AS {EscapeIdentifier(uniqueColumnName)}";
                }
                else if (!string.IsNullOrEmpty(column.QueryAlias) && column.QueryAlias != column.Name)
                {
                    // Use existing user-defined alias
                    columnWithAlias = $"{columnExpression} AS {EscapeIdentifier(column.QueryAlias)}";
                }
                else
                {
                    // No alias needed
                    columnWithAlias = columnExpression;
                }

                selectedColumns.Add($"    {columnWithAlias}");

                // Track this column name as used
                usedColumnNames[uniqueColumnName] = usedColumnNames.GetValueOrDefault(uniqueColumnName, 0) + 1;
            }
        }

        //select primary keys or first column from each table if no columns selected
        if (!selectedColumns.Any())
        {
            foreach (var table in tables)
            {
                var tableAlias = GetTableAlias(table);
                var primaryKey = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
                var columnToSelect = primaryKey ?? table.Columns.FirstOrDefault();

                if (columnToSelect != null)
                {
                    string uniqueColumnName = GetUniqueColumnName(columnToSelect.Name, table.Name, usedColumnNames);
                    string columnExpression = $"{tableAlias}.{EscapeIdentifier(columnToSelect.Name)}";

                    if (uniqueColumnName != columnToSelect.Name)
                    {
                        columnExpression += $" AS {EscapeIdentifier(uniqueColumnName)}";
                    }

                    selectedColumns.Add($"    {columnExpression}");
                    usedColumnNames[uniqueColumnName] = usedColumnNames.GetValueOrDefault(uniqueColumnName, 0) + 1;
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
                var joinTableAlias = GetTableAlias(joinTable); // This will use cached alias
                var currentTableAlias = GetTableAlias(currentTable); // This will use cached alias

                var schemaPrefix = !string.IsNullOrEmpty(joinTable.Schema) ? $"{EscapeIdentifier(joinTable.Schema)}." : "";

                // Build join condition - FIXED: Use the table that owns each column
                var sourceTable = allTables.FirstOrDefault(t => t.Id == relationship.SourceTableId);
                var targetTable = allTables.FirstOrDefault(t => t.Id == relationship.TargetTableId);

                if (sourceTable != null && targetTable != null)
                {
                    var sourceTableAlias = GetTableAlias(sourceTable);
                    var targetTableAlias = GetTableAlias(targetTable);

                    var leftColumn = $"{sourceTableAlias}.{EscapeIdentifier(GetColumnName(sourceTable, relationship.SourceColumnId))}";
                    var rightColumn = $"{targetTableAlias}.{EscapeIdentifier(GetColumnName(targetTable, relationship.TargetColumnId))}";

                    sql.AppendLine($"{joinType} {schemaPrefix}{EscapeIdentifier(joinTable.Name)} AS {joinTableAlias}");
                    sql.AppendLine($"    ON {leftColumn} = {rightColumn}");
                }

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
            // Clear alias tracking at the very beginning
            ClearAliasTracking();

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

    private string GetUniqueColumnName(string originalName, string tableName, Dictionary<string, int> usedNames)
    {
        // Check if the original name is already used
        if (!usedNames.ContainsKey(originalName))
        {
            return originalName; // No conflict, use original name
        }

        // Try with table prefix first
        string tablePrefix = GenerateTablePrefix(tableName);
        string prefixedName = $"{tablePrefix}_{originalName}";

        if (!usedNames.ContainsKey(prefixedName))
        {
            return prefixedName;
        }

        // If table prefix doesn't work, use numbered suffix
        int counter = 1;
        string numberedName;

        do
        {
            numberedName = $"{originalName}_{counter}";
            counter++;
        }
        while (usedNames.ContainsKey(numberedName) && counter <= 100); // Prevent infinite loop

        return numberedName;
    }

    private string GenerateTablePrefix(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return "T";

        // Generate a short prefix from table name
        // Take first letter + consonants, max 3 characters
        var prefix = new StringBuilder();
        var cleanName = tableName.Replace("_", "").Replace("-", "");

        if (cleanName.Length > 0)
        {
            prefix.Append(char.ToUpper(cleanName[0])); // First letter

            // Add consonants up to 2 more characters
            var consonants = "BCDFGHJKLMNPQRSTVWXYZ";
            for (int i = 1; i < cleanName.Length && prefix.Length < 3; i++)
            {
                char c = char.ToUpper(cleanName[i]);
                if (consonants.Contains(c))
                {
                    prefix.Append(c);
                }
            }
        }

        // If we don't have enough characters, pad with the first few letters
        while (prefix.Length < 2 && prefix.Length < cleanName.Length)
        {
            for (int i = 1; i < cleanName.Length && prefix.Length < 3; i++)
            {
                if (!prefix.ToString().Contains(char.ToUpper(cleanName[i])))
                {
                    prefix.Append(char.ToUpper(cleanName[i]));
                }
            }
            break; // Prevent infinite loop
        }

        return prefix.Length > 0 ? prefix.ToString() : "T";
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
        // Check cache first to ensure consistency
        if (_tableAliasCache.TryGetValue(table.Id, out string cachedAlias))
        {
            return EscapeIdentifier(cachedAlias);
        }

        string proposedAlias;

        // Use custom alias if it's different from table name and not already used
        if (!string.IsNullOrEmpty(table.Alias) && table.Alias != table.Name)
        {
            proposedAlias = table.Alias;
            if (!_usedAliases.Contains(proposedAlias))
            {
                _usedAliases.Add(proposedAlias);
                _tableAliasCache[table.Id] = proposedAlias;
                return EscapeIdentifier(proposedAlias);
            }
        }

        // Generate short alias from table name
        proposedAlias = GenerateShortAlias(table.Name);

        // Ensure the alias is unique
        var uniqueAlias = EnsureUniqueAlias(proposedAlias);
        _usedAliases.Add(uniqueAlias);
        _tableAliasCache[table.Id] = uniqueAlias;

        return EscapeIdentifier(uniqueAlias);
    }

    private string GenerateShortAlias(string tableName)
    {
        if (string.IsNullOrEmpty(tableName))
            return "t";

        // Take first 3 characters and make lowercase
        var alias = tableName.Length >= 3
            ? tableName.Substring(0, 3).ToLowerInvariant()
            : tableName.ToLowerInvariant();

        return alias;
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


