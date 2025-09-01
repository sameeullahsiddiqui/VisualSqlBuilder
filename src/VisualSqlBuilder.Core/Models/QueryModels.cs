using System;
using System.Collections.Generic;

namespace VisualSqlBuilder.Core.Models;
public class TableModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public List<ColumnModel> Columns { get; set; } = new();
    public Position Position { get; set; } = new();
    public Size Size { get; set; } = new() { Width = 250, Height = 300 };
    public string? DomainId { get; set; }
    public bool IsFromExcel { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class ColumnModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = "nvarchar";
    public int? MaxLength { get; set; }
    public bool IsNullable { get; set; } = true;
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public bool IsSelected { get; set; }
    public bool IsComputed { get; set; }
    public string? ComputedExpression { get; set; }
    public List<ValidationRule> ValidationRules { get; set; } = new();
    public FilterModel? Filter { get; set; }
    public string? QueryAlias { get; set; }
}

public class RelationshipModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;
    public string SourceTableId { get; set; } = string.Empty;
    public string SourceColumnId { get; set; } = string.Empty;
    public string TargetTableId { get; set; } = string.Empty;
    public string TargetColumnId { get; set; } = string.Empty;
    public JoinType JoinType { get; set; } = JoinType.InnerJoin;
    public RelationshipType Type { get; set; } = RelationshipType.Foreign;
    public string? Cardinality { get; set; } = "1:N";

}

public enum JoinType
{
    InnerJoin,
    LeftJoin,
    RightJoin,
    FullOuterJoin,
    CrossJoin
}

public enum RelationshipType
{
    Primary,
    Foreign
}

public class Position
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class Size
{
    public int Width { get; set; } = 250;
    public int Height { get; set; } = 200;
}

public class DomainModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#e3f2fd";
    public Position Position { get; set; } = new();
    public Size Size { get; set; } = new() { Width = 400, Height = 400 };
    public bool IsCollapsed { get; set; }
}

public class ValidationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string RuleType { get; set; } = "SQL"; // SQL or CSharp
    public string Expression { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class FilterModel
{
    public string Operator { get; set; } = "=";
    public string Value { get; set; } = string.Empty;
    public string? SecondValue { get; set; } // For BETWEEN
}

public class QueryModel
{
    public List<TableModel> Tables { get; set; } = new();
    public List<RelationshipModel> Relationships { get; set; } = new();
    public List<DomainModel> Domains { get; set; } = new();
    public List<string> GroupByColumns { get; set; } = new();
    public List<string> OrderByColumns { get; set; } = new();
    public Dictionary<string, string> PredefinedColumns { get; set; } = new();
    public string GeneratedSql { get; set; } = string.Empty;
}

public class CanvasState
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public QueryModel Query { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string JsonData { get; set; } = string.Empty;
}

public class AggregateFunction
{
    public string ColumnId { get; set; } = "";
    public string Function { get; set; } = "COUNT"; // COUNT, SUM, AVG, MIN, MAX, COUNT_DISTINCT
    public string? Alias { get; set; }
}