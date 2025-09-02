using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisualSqlBuilder.Core.Models;


public class ExcelDataTypeInfo
{
    public string SqlType { get; set; } = "nvarchar(255)";
    public int? MaxLength { get; set; }
    public bool IsNullable { get; set; } = true;
    public bool IsNumeric { get; set; } = false;
    public bool IsDate { get; set; } = false;
    public bool IsBoolean { get; set; } = false;
    public int SampleCount { get; set; } = 0;
    public int NullCount { get; set; } = 0;
}

public static class ExcelTypeDetector
{
    public static ExcelDataTypeInfo AnalyzeColumn(IXLWorksheet worksheet, int columnNumber, int startRow, int endRow, int maxSamples = 100)
    {
        var info = new ExcelDataTypeInfo();
        var actualSamples = Math.Min(maxSamples, endRow - startRow + 1);

        var numericCount = 0;
        var dateCount = 0;
        var boolCount = 0;
        var textCount = 0;
        var maxTextLength = 0;
        var hasDecimals = false;

        for (int row = startRow; row <= Math.Min(startRow + actualSamples - 1, endRow); row++)
        {
            var cell = worksheet.Cell(row, columnNumber);
            info.SampleCount++;

            if (cell.IsEmpty() || string.IsNullOrWhiteSpace(cell.Value.ToString()))
            {
                info.NullCount++;
                continue;
            }

            var cellValue = cell.Value.ToString().Trim();
            maxTextLength = Math.Max(maxTextLength, cellValue.Length);

            switch (cell.DataType)
            {
                case XLDataType.Number:
                    numericCount++;
                    if (cellValue.Contains(".") || cellValue.Contains(","))
                        hasDecimals = true;
                    break;
                case XLDataType.DateTime:
                    dateCount++;
                    break;
                case XLDataType.Boolean:
                    boolCount++;
                    break;
                case XLDataType.Text:
                    // Try to infer type from text content
                    if (DateTime.TryParse(cellValue, out _))
                        dateCount++;
                    else if (decimal.TryParse(cellValue, out var decVal))
                    {
                        numericCount++;
                        if (decVal != Math.Truncate(decVal))
                            hasDecimals = true;
                    }
                    else if (IsBooleanValue(cellValue))
                        boolCount++;
                    else
                        textCount++;
                    break;
            }
        }

        // Determine the best SQL type based on analysis
        var totalDataValues = info.SampleCount - info.NullCount;
        info.IsNullable = info.NullCount > 0;

        if (dateCount > 0 && dateCount >= totalDataValues * 0.8)
        {
            info.SqlType = "datetime2";
            info.IsDate = true;
        }
        else if (boolCount > 0 && boolCount >= totalDataValues * 0.9)
        {
            info.SqlType = "bit";
            info.IsBoolean = true;
        }
        else if (numericCount > 0 && numericCount >= totalDataValues * 0.8)
        {
            info.IsNumeric = true;
            if (hasDecimals)
            {
                info.SqlType = "decimal(18,4)";
            }
            else
            {
                // Check if values fit in int range
                info.SqlType = "int";
            }
        }
        else
        {
            // Text data - determine appropriate length
            if (maxTextLength <= 50)
            {
                info.SqlType = "nvarchar(50)";
                info.MaxLength = 50;
            }
            else if (maxTextLength <= 255)
            {
                info.SqlType = "nvarchar(255)";
                info.MaxLength = 255;
            }
            else if (maxTextLength <= 4000)
            {
                info.SqlType = "nvarchar(4000)";
                info.MaxLength = 4000;
            }
            else
            {
                info.SqlType = "nvarchar(max)";
            }
        }

        return info;
    }

    private static bool IsBooleanValue(string value)
    {
        var lower = value.ToLower().Trim();
        return new[] { "true", "false", "yes", "no", "1", "0", "y", "n", "on", "off" }.Contains(lower);
    }
}

public class ExcelWorkbookInfo
{
    public string FileName { get; set; } = string.Empty;
    public List<ExcelSheetInfo> Sheets { get; set; } = new();
    public DateTime LastModified { get; set; }
    public long FileSize { get; set; }
}

public class ExcelSheetInfo
{
    public string Name { get; set; } = string.Empty;
    public int ColumnCount { get; set; }
    public int RowCount { get; set; }
    public bool HasHeader { get; set; }
    public List<string> ColumnNames { get; set; } = new();
    public List<string> DataTypes { get; set; } = new();
}

