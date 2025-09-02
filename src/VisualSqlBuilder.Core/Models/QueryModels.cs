using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VisualSqlBuilder.Core.Models;
public class TableModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Excel-specific properties
    public bool IsFromExcel { get; set; } = false;
    public int? RowCount { get; set; }
    public string? OriginalSheetName { get; set; }
    public DateTime? ImportedAt { get; set; }
    public bool IsVisible { get; set; } = true;

    // Visual properties
    public Position Position { get; set; } = new();
    public Size Size { get; set; } = new() { Width = 280, Height = 150 };

    // Domain assignment
    public string? DomainId { get; set; }

    // Columns collection
    public List<ColumnModel> Columns { get; set; } = new();

    // Constructor
    public TableModel()
    {
        ImportedAt = DateTime.Now;
    }

    // Helper methods
    public bool HasPrimaryKey => Columns.Any(c => c.IsPrimaryKey);
    public bool HasForeignKeys => Columns.Any(c => c.IsForeignKey);
    public int SelectedColumnCount => Columns.Count(c => c.IsSelected);

    public string DisplayName => !string.IsNullOrEmpty(Alias) && Alias != Name ? $"{Name} ({Alias})" : Name;

    public string FullName => !string.IsNullOrEmpty(Schema) ? $"{Schema}.{Name}" : Name;

    // Validation
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) &&
        Columns.Any() &&
        Position != null &&
        Size != null;
}

public class ColumnModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = "nvarchar";
    public int? MaxLength { get; set; }
    public bool IsNullable { get; set; } = true;
    public bool IsPrimaryKey { get; set; } = false;
    public bool IsForeignKey { get; set; } = false;
    public bool IsSelected { get; set; } = false;

    // Query properties
    public string? QueryAlias { get; set; }
    public FilterModel? Filter { get; set; }

    // Computed column properties
    public bool IsComputed { get; set; } = false;
    public string? ComputedExpression { get; set; }

    // Excel-specific properties
    public string? OriginalColumnName { get; set; }
    public int? ExcelColumnIndex { get; set; }
    public bool InferredFromData { get; set; } = false;
    public double? DataQualityScore { get; set; } // 0-1 score for data quality

    // Display properties
    public string DisplayName => !string.IsNullOrEmpty(QueryAlias) && QueryAlias != Name ? $"{Name} → {QueryAlias}" : Name;
    public string TypeDisplayName => MaxLength.HasValue ? $"{DataType}({MaxLength})" : DataType;

    // Validation
    public bool IsValidForQuery => IsSelected && !string.IsNullOrWhiteSpace(Name);
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
    public int X { get; set; } = 0;
    public int Y { get; set; } = 0;

    public Position() { }
    public Position(int x, int y) { X = x; Y = y; }

    public override string ToString() => $"({X}, {Y})";
}


public class Size
{
    public int Width { get; set; } = 280;
    public int Height { get; set; } = 150;

    public Size() { }
    public Size(int width, int height) { Width = width; Height = height; }

    public override string ToString() => $"{Width}×{Height}";
}

public class Rectangle
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class DomainModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Position Position { get; set; } = new();
    public Size Size { get; set; } = new() { Width = 400, Height = 300 };
    public string Color { get; set; } = "#e3f2fd";
    public bool IsCollapsed { get; set; } = false;

    // Excel-specific domain properties
    public bool ContainsExcelTables { get; set; } = false;
    public DateTime? CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ModifiedAt { get; set; }

    public void UpdateModifiedTime()
    {
        ModifiedAt = DateTime.Now;
    }
}

public class ValidationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string RuleType { get; set; } = "SQL"; // SQL, CSharp, etc.
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? ErrorMessage { get; internal set; }
}

public class FilterModel
{
    public string Operator { get; set; } = "=";
    public string Value { get; set; } = string.Empty;
    public string? SecondValue { get; set; } // For BETWEEN operator
    public bool IsCaseSensitive { get; set; } = false;

    public static readonly string[] SupportedOperators =
    {
        "=", "!=", "<", "<=", ">", ">=",
        "LIKE", "NOT LIKE", "IN", "NOT IN",
        "IS NULL", "IS NOT NULL", "BETWEEN"
    };

    public bool IsValid => SupportedOperators.Contains(Operator) &&
                          (Operator.EndsWith("NULL") || !string.IsNullOrWhiteSpace(Value));
}

public class ExcelImportResult
{
    public List<TableModel> Tables { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public TimeSpan ProcessingTime { get; set; }
    public long FileSize { get; set; }
    public string FileName { get; set; } = string.Empty;

    public bool IsSuccessful => Tables.Any() && !Errors.Any();
    public string Summary => $"Imported {Tables.Count} table(s) with {Warnings.Count} warning(s) and {Errors.Count} error(s)";
}


public partial class QueryModel
{
    public List<TableModel> Tables { get; set; } = new();
    public List<RelationshipModel> Relationships { get; set; } = new();
    public List<DomainModel> Domains { get; set; } = new();

    // Excel-specific query properties
    public bool HasExcelTables => Tables.Any(t => t.IsFromExcel);
    public bool HasSqlTables => Tables.Any(t => !t.IsFromExcel);
    public bool IsMixedQuery => HasExcelTables && HasSqlTables;

    // Helper methods
    public List<TableModel> GetExcelTables() => Tables.Where(t => t.IsFromExcel).ToList();
    public List<TableModel> GetSqlTables() => Tables.Where(t => !t.IsFromExcel).ToList();

    public int TotalColumns => Tables.Sum(t => t.Columns.Count);
    public int SelectedColumns => Tables.Sum(t => t.SelectedColumnCount);

    // Validation
    public bool CanGenerateQuery => Tables.Any() &&
                                   (Tables.Any(t => t.Columns.Any(c => c.IsSelected)) ||
                                    Relationships.Any());
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