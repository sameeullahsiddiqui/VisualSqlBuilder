using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.Data;
using System.Text;
using VisualSqlBuilder.Core.Models;

namespace VisualSqlBuilder.Core.Components;

public partial class SqlQueryBuilder
{
    // =============================
    // Excel Import State
    // =============================
    private List<TableModel> _excelTables = new();
    private List<bool> _excelTableSelections = new();
    private bool _excelProcessing = false;
    private bool _useExcelAI = true;
    private string _excelErrorMessage = string.Empty;
    private int _processedSheets = 0;
    private int _processingProgress = 0;
    private const long MaxExcelFileSize = 10 * 1024 * 1024; // 10MB

    // =============================
    // Query / SQL State
    // =============================
    private QueryModel _queryModel = new();
    private string _generatedSql = string.Empty;
    private TableModel? _selectedTable;
    private List<ValidationRule> _validationRules = new();
    private System.Data.DataTable? _queryResults;
    private double _zoomLevel = 1.0;

    // =============================
    // Blazor Parameters
    // =============================
    [Parameter] public string? ConnectionString { get; set; }
    [Parameter] public EventCallback<string> OnQueryGenerated { get; set; }

    // =============================
    // General UI State
    // =============================
    private ElementReference canvasElement;
    private bool _showConnectionModal = false;
    private bool _showExcelModal = false;
    private bool _isConnecting = false;
    private string _connectionError = string.Empty;

    // =============================
    // JS Interop
    // =============================
    private IJSObjectReference? _jsModule;
    private DotNetObjectReference<SqlQueryBuilder>? dotNetHelper;

    // =============================
    // Rename Modal
    // =============================
    private bool showRenameModal = false;
    private string currentTableIdForRename = string.Empty;
    private string newTableName = string.Empty;

    // =============================
    // Join Type Modal
    // =============================
    private bool showJoinTypeModal = false;
    private PendingRelationship? pendingRelationship;
    private JoinType selectedJoinType = JoinType.InnerJoin;
    private string selectedCardinality = "1:N";
    private string relationshipName = string.Empty;
    private string? editingRelationshipId = null;

    // =============================
    // Add Column Modal
    // =============================
    private bool showAddColumnModal = false;
    private TableModel? selectedTableForColumn;
    private string newColumnName = string.Empty;
    private string newColumnDataType = "nvarchar";
    private int? newColumnMaxLength;
    private bool newColumnIsNullable = true;
    private bool newColumnIsPrimaryKey = false;
    private bool newColumnIsForeignKey = false;
    private string newColumnExpression = string.Empty;

    // =============================
    // Column Alias Modal
    // =============================
    private bool showColumnAliasModal = false;
    private ColumnModel? selectedColumnForAlias;
    private string newColumnAlias = string.Empty;

    // =============================
    // Domain Modal
    // =============================
    private bool showCreateDomainModal = false;
    private string newDomainName = string.Empty;
    private string newDomainColor = "#e3f2fd";

    // =============================
    // Auto Arrange
    // =============================
    private bool isAutoArranging = false;

    // =============================
    // Pending relationship model
    // =============================
    public class PendingRelationship
    {
        public string SourceTableId { get; set; } = "";
        public string SourceColumnId { get; set; } = "";
        public string TargetTableId { get; set; } = "";
        public string TargetColumnId { get; set; } = "";
        public string SourceTableName { get; set; } = "";
        public string SourceColumnName { get; set; } = "";
        public string TargetTableName { get; set; } = "";
        public string TargetColumnName { get; set; } = "";
    }

    protected override async Task OnInitializedAsync()
    {
        Console.WriteLine("VisualSqlBuilder initialized");
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                dotNetHelper = DotNetObjectReference.Create(this);

                _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                "import", "./_content/VisualSqlBuilder.Core/visual-sql-builder.js");

                await InitializeCanvas();
                await JSRuntime.InvokeVoidAsync("console.log", "Canvas initialization completed");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing JavaScript: {ex.Message}");
                await JSRuntime.InvokeVoidAsync("console.error", $"Canvas initialization failed: {ex.Message}");
            }
        }
    }

    private async Task InitializeCanvas()
    {
        try
        {
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("initializeSqlCanvas", canvasElement, dotNetHelper);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing canvas: {ex.Message}");
            await JSRuntime.InvokeVoidAsync("console.error", $"Canvas initialization failed: {ex.Message}");
        }
    }

    // JavaScript-invokable methods
    [JSInvokable]
    public void ShowRenameModal(string tableId, string currentName)
    {
        currentTableIdForRename = tableId;
        newTableName = currentName;
        showRenameModal = true;
        StateHasChanged();
    }

    [JSInvokable]
    public void ShowJoinTypeSelection(string sourceTableId, string sourceColumnId, string targetTableId, string targetColumnId)
    {
        var sourceTable = _queryModel.Tables.FirstOrDefault(t => t.Id == sourceTableId);
        var targetTable = _queryModel.Tables.FirstOrDefault(t => t.Id == targetTableId);
        var sourceColumn = sourceTable?.Columns.FirstOrDefault(c => c.Id == sourceColumnId);
        var targetColumn = targetTable?.Columns.FirstOrDefault(c => c.Id == targetColumnId);

        if (sourceTable != null && targetTable != null && sourceColumn != null && targetColumn != null)
        {
            pendingRelationship = new PendingRelationship
            {
                SourceTableId = sourceTableId,
                SourceColumnId = sourceColumnId,
                TargetTableId = targetTableId,
                TargetColumnId = targetColumnId,
                SourceTableName = sourceTable.Name,
                SourceColumnName = sourceColumn.Name,
                TargetTableName = targetTable.Name,
                TargetColumnName = targetColumn.Name
            };

            selectedJoinType = JoinType.InnerJoin;
            selectedCardinality = "1:N";
            relationshipName = $"{sourceTable.Name}_{targetTable.Name}";
            editingRelationshipId = null;
            showJoinTypeModal = true;
            StateHasChanged();
        }
    }

    [JSInvokable]
    public void ShowJoinTypeModal(string relationshipId)
    {
        var relationship = _queryModel.Relationships.FirstOrDefault(r => r.Id == relationshipId);
        if (relationship != null)
        {
            var sourceTable = _queryModel.Tables.FirstOrDefault(t => t.Id == relationship.SourceTableId);
            var targetTable = _queryModel.Tables.FirstOrDefault(t => t.Id == relationship.TargetTableId);
            var sourceColumn = sourceTable?.Columns.FirstOrDefault(c => c.Id == relationship.SourceColumnId);
            var targetColumn = targetTable?.Columns.FirstOrDefault(c => c.Id == relationship.TargetColumnId);

            if (sourceTable != null && targetTable != null && sourceColumn != null && targetColumn != null)
            {
                pendingRelationship = new PendingRelationship
                {
                    SourceTableId = relationship.SourceTableId,
                    SourceColumnId = relationship.SourceColumnId,
                    TargetTableId = relationship.TargetTableId,
                    TargetColumnId = relationship.TargetColumnId,
                    SourceTableName = sourceTable.Name,
                    SourceColumnName = sourceColumn.Name,
                    TargetTableName = targetTable.Name,
                    TargetColumnName = targetColumn.Name
                };

                selectedJoinType = relationship.JoinType;
                selectedCardinality = relationship.Cardinality;
                relationshipName = relationship.Name ?? "";
                editingRelationshipId = relationshipId;
                showJoinTypeModal = true;
                StateHasChanged();
            }
        }
    }



    [JSInvokable]
    public void ShowColumnAliasModal(string tableId, string columnId)
    {
        var table = _queryModel.Tables.FirstOrDefault(t => t.Id == tableId);
        selectedColumnForAlias = table?.Columns.FirstOrDefault(c => c.Id == columnId);

        if (selectedColumnForAlias != null)
        {
            newColumnAlias = selectedColumnForAlias.QueryAlias ?? "";
            showColumnAliasModal = true;
            StateHasChanged();
        }
    }

    [JSInvokable]
    public void ToggleColumnSelection(string tableId, string columnId)
    {
        var table = _queryModel.Tables.FirstOrDefault(t => t.Id == tableId);
        var column = table?.Columns.FirstOrDefault(c => c.Id == columnId);

        if (column != null)
        {
            column.IsSelected = !column.IsSelected;
            UpdateSqlPreview();
            StateHasChanged();
        }
    }



    [JSInvokable]
    public async Task UpdateRelationshipLines()
    {
        // This method is called when tables are moved to update relationship line positions
        await RefreshRelationshipDiagram();
        StateHasChanged();
    }

    [JSInvokable]
    public async Task AutoArrangeTables()
    {
        if (isAutoArranging) return;

        isAutoArranging = true;
        StateHasChanged();

        try
        {
            // Get all tables that have relationships
            var connectedTables = _queryModel.Tables
            .Where(t => _queryModel.Relationships.Any(r => r.SourceTableId == t.Id || r.TargetTableId == t.Id))
            .ToList();

            // Arrange connected tables in a hierarchical layout
            if (connectedTables.Any())
            {
                await ArrangeConnectedTables(connectedTables);
            }

            // Arrange orphan tables separately
            var orphanTables = _queryModel.Tables.Except(connectedTables).ToList();
            if (orphanTables.Any())
            {
                ArrangeOrphanTables(orphanTables, connectedTables.Any() ? 800 : 50);
            }

            UpdateSqlPreview();
            await RefreshRelationshipDiagram();
            StateHasChanged();
        }
        finally
        {
            isAutoArranging = false;
        }
    }

    // Helper methods and remaining functionality
    private void HandleRenameSubmit()
    {
        if (!string.IsNullOrWhiteSpace(newTableName))
        {
            var table = _queryModel.Tables.FirstOrDefault(t => t.Id == currentTableIdForRename);
            if (table != null)
            {
                table.Name = newTableName;
                UpdateSqlPreview();
            }
            CloseRenameModal();
        }
    }

    private void CloseRenameModal()
    {
        showRenameModal = false;
        StateHasChanged();
    }

    private void ConfirmRelationship()
    {
        if (pendingRelationship != null)
        {
            if (editingRelationshipId != null)
            {
                // Update existing relationship
                var existingRelationship = _queryModel.Relationships.FirstOrDefault(r => r.Id == editingRelationshipId);
                if (existingRelationship != null)
                {
                    existingRelationship.JoinType = selectedJoinType;
                    existingRelationship.Cardinality = selectedCardinality;
                    existingRelationship.Name = string.IsNullOrWhiteSpace(relationshipName) ? null : relationshipName;
                }
            }
            else
            {
                // Create new relationship
                var newRelationship = new RelationshipModel
                {
                    Id = Guid.NewGuid().ToString(),
                    SourceTableId = pendingRelationship.SourceTableId,
                    SourceColumnId = pendingRelationship.SourceColumnId,
                    TargetTableId = pendingRelationship.TargetTableId,
                    TargetColumnId = pendingRelationship.TargetColumnId,
                    JoinType = selectedJoinType,
                    Cardinality = selectedCardinality,
                    Name = string.IsNullOrWhiteSpace(relationshipName) ? null : relationshipName
                };

                _queryModel.Relationships.Add(newRelationship);
            }

            UpdateSqlPreview();
            CloseJoinTypeModal();
            StateHasChanged();
        }
    }

    private void CloseJoinTypeModal()
    {
        showJoinTypeModal = false;
        pendingRelationship = null;
        editingRelationshipId = null;
        StateHasChanged();
    }

    private void CloseColumnAliasModal()
    {
        showColumnAliasModal = false;
        selectedColumnForAlias = null;
        newColumnAlias = "";
        StateHasChanged();
    }

    private void ConfirmColumnAlias()
    {
        if (selectedColumnForAlias != null)
        {
            selectedColumnForAlias.QueryAlias = string.IsNullOrWhiteSpace(newColumnAlias) ? null : newColumnAlias;
            UpdateSqlPreview();
            CloseColumnAliasModal();
        }
    }

    private async Task ArrangeConnectedTables(List<TableModel> connectedTables)
    {
        // Simple hierarchical layout algorithm
        var positioned = new HashSet<string>();
        var currentX = 50;
        var currentY = 50;
        var maxHeightInRow = 0;
        var tablesPerRow = 3;
        var tableSpacing = 320;
        var rowSpacing = 200;

        // Build a graph of relationships
        var graph = new Dictionary<string, List<string>>();
        foreach (var table in connectedTables)
        {
            graph[table.Id] = new List<string>();
        }

        foreach (var rel in _queryModel.Relationships)
        {
            if (graph.ContainsKey(rel.SourceTableId) && graph.ContainsKey(rel.TargetTableId))
            {
                graph[rel.SourceTableId].Add(rel.TargetTableId);
                graph[rel.TargetTableId].Add(rel.SourceTableId);
            }
        }

        // Find root tables (tables with most connections or primary key tables)
        var rootTables = connectedTables
        .OrderByDescending(t => graph[t.Id].Count)
        .ThenByDescending(t => t.Columns.Any(c => c.IsPrimaryKey))
        .Take(tablesPerRow)
        .ToList();

        // Position root tables first
        foreach (var table in rootTables)
        {
            table.Position = new Position { X = currentX, Y = currentY };
            positioned.Add(table.Id);

            currentX += tableSpacing;
            maxHeightInRow = Math.Max(maxHeightInRow, (int)table.Size.Height);

            if (currentX > tableSpacing * tablesPerRow)
            {
                currentX = 50;
                currentY += maxHeightInRow + rowSpacing;
                maxHeightInRow = 0;
            }
        }

        // Position remaining tables
        var remaining = connectedTables.Where(t => !positioned.Contains(t.Id)).ToList();
        foreach (var table in remaining)
        {
            table.Position = new Position { X = currentX, Y = currentY };

            currentX += tableSpacing;
            maxHeightInRow = Math.Max(maxHeightInRow, (int)table.Size.Height);

            if (currentX > tableSpacing * tablesPerRow)
            {
                currentX = 50;
                currentY += maxHeightInRow + rowSpacing;
                maxHeightInRow = 0;
            }
        }

        await Task.Delay(100); // Small delay for visual effect
    }

    private void ArrangeOrphanTables(List<TableModel> orphanTables, int startX)
    {
        var currentX = startX;
        var currentY = 50;
        var maxHeightInRow = 0;
        var tableSpacing = 320;
        var rowSpacing = 200;
        var tablesPerRow = 3;

        foreach (var table in orphanTables)
        {
            table.Position = new Position { X = currentX, Y = currentY };

            currentX += tableSpacing;
            maxHeightInRow = Math.Max(maxHeightInRow, (int)table.Size.Height);

            if (currentX > startX + (tableSpacing * tablesPerRow))
            {
                currentX = startX;
                currentY += maxHeightInRow + rowSpacing;
                maxHeightInRow = 0;
            }
        }
    }

    private void OnJoinTypeChanged(ChangeEventArgs e)
    {
        if (e.Value != null && Enum.TryParse<JoinType>(e.Value.ToString(), out var joinType))
        {
            selectedJoinType = joinType;
            StateHasChanged();
        }
    }

    // Helper methods for join type display
    private string GetJoinTypeColor(JoinType joinType)
    {
        return joinType switch
        {
            JoinType.InnerJoin => "#dc3545",
            JoinType.LeftJoin => "#0d6efd",
            JoinType.RightJoin => "#fd7e14",
            JoinType.FullOuterJoin => "#6f42c1",
            JoinType.CrossJoin => "#20c997",
            _ => "#6c757d"
        };
    }

    private string GetJoinTypeDashArray(JoinType joinType)
    {
        return joinType switch
        {
            JoinType.InnerJoin => "none",
            JoinType.LeftJoin => "5,5",
            JoinType.RightJoin => "10,5",
            JoinType.FullOuterJoin => "15,5,5,5",
            JoinType.CrossJoin => "2,2",
            _ => "none"
        };
    }

    private string GetJoinTypeDisplayName(JoinType joinType)
    {
        return joinType switch
        {
            JoinType.InnerJoin => "INNER",
            JoinType.LeftJoin => "LEFT",
            JoinType.RightJoin => "RIGHT",
            JoinType.FullOuterJoin => "FULL",
            JoinType.CrossJoin => "CROSS",
            _ => joinType.ToString().ToUpper()
        };
    }

    private string GetJoinTypeDescription(JoinType joinType)
    {
        return joinType switch
        {
            JoinType.InnerJoin => "Returns only matching records from both tables",
            JoinType.LeftJoin => "Returns all records from left table and matching from right",
            JoinType.RightJoin => "Returns all records from right table and matching from left",
            JoinType.FullOuterJoin => "Returns all records from both tables",
            JoinType.CrossJoin => "Returns cartesian product of both tables",
            _ => ""
        };
    }

    // Connection and database methods
    private void ShowConnectionDialog()
    {
        _showConnectionModal = true;
        _connectionError = "";
        StateHasChanged();
    }

    private void CloseConnectionDialog()
    {
        _showConnectionModal = false;
        _connectionError = "";
        _isConnecting = false;
        StateHasChanged();
    }

    public async Task ShowExcelUpload()
    {
        _showExcelModal = true;
        StateHasChanged();

        // Focus the modal when it opens
        try
        {
            await Task.Delay(100); // Small delay to ensure modal is rendered
            await JSRuntime.InvokeVoidAsync("console.log", "Excel upload modal opened");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ShowExcelUpload: {ex}");
        }
    }

    private async Task HandleExcelFileSelected(InputFileChangeEventArgs e)
    {
        _excelErrorMessage = string.Empty;
        _excelTables.Clear();
        _excelTableSelections.Clear();
        _processedSheets = 0;
        _processingProgress = 0;

        var file = e.File;
        if (file == null) return;

        if (file.Size > MaxExcelFileSize)
        {
            _excelErrorMessage = "File size exceeds 10MB limit.";
            return;
        }

        if (!file.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !file.Name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            _excelErrorMessage = "Please select a valid Excel file (.xlsx or .xls).";
            return;
        }

        _excelProcessing = true;
        StateHasChanged();

        try
        {
            using var stream = file.OpenReadStream(MaxExcelFileSize);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            _processingProgress = 30;
            StateHasChanged();
            await Task.Delay(100);

            _excelTables = SchemaService.LoadTablesFromExcel(ms);

            _processingProgress = 80;
            StateHasChanged();

            // Initialize selections - all selected by default
            _excelTableSelections = new List<bool>();
            for (int i = 0; i < _excelTables.Count; i++)
            {
                _excelTableSelections.Add(true);
                _processedSheets++;
            }

            _processingProgress = 100;
            await Task.Delay(300);
        }
        catch (Exception ex)
        {
            _excelErrorMessage = $"Error processing file: {ex.Message}";
            Console.WriteLine($"Excel processing error: {ex}");
        }
        finally
        {
            _excelProcessing = false;
            StateHasChanged();
        }
    }


    private void SelectAllExcelTables()
    {
        for (int i = 0; i < _excelTableSelections.Count; i++)
        {
            _excelTableSelections[i] = true;
        }
        StateHasChanged();
    }

    private void SelectNoExcelTables()
    {
        for (int i = 0; i < _excelTableSelections.Count; i++)
        {
            _excelTableSelections[i] = false;
        }
        StateHasChanged();
    }

    private bool HasSelectedExcelTables()
    {
        return _excelTableSelections.Any(selected => selected);
    }

    private int GetSelectedExcelTableCount()
    {
        return _excelTableSelections.Count(selected => selected);
    }

    private async Task ImportExcelTables()
    {
        var selectedTablesList = new List<TableModel>();

        for (int i = 0; i < _excelTables.Count; i++)
        {
            if (i < _excelTableSelections.Count && _excelTableSelections[i])
            {
                selectedTablesList.Add(_excelTables[i]);
            }
        }

        if (!selectedTablesList.Any())
        {
            _excelErrorMessage = "Please select at least one sheet to import.";
            return;
        }

        // Import the tables
        await HandleExcelTablesImported(selectedTablesList);

        // Handle AI relationship suggestions if enabled
        if (_useExcelAI && OpenAIService != null && selectedTablesList.Count > 1)
        {
            _excelProcessing = true;
            StateHasChanged();

            try
            {
                var relationships = await OpenAIService.SuggestRelationshipsAsync(selectedTablesList);
                await HandleRelationshipsSuggested(relationships);
            }
            catch (Exception ex)
            {
                _excelErrorMessage = $"AI suggestion error: {ex.Message}";
            }
            finally
            {
                _excelProcessing = false;
                StateHasChanged();
            }
        }

        CloseExcelDialog();
    }


    private void CloseExcelDialog()
    {
        _showExcelModal = false;
        _excelTables.Clear();
        _excelTableSelections.Clear();
        _excelErrorMessage = string.Empty;
        _excelProcessing = false;
        _processedSheets = 0;
        _processingProgress = 0;
        StateHasChanged();
    }
    private async Task ConnectToDatabase()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            _connectionError = "Please enter a connection string.";
            return;
        }

        _isConnecting = true;
        _connectionError = "";
        StateHasChanged();

        try
        {
            var tables = await SchemaService.LoadTablesFromSqlServerAsync(ConnectionString);
            var relationships = await SchemaService.LoadRelationshipsFromSqlServerAsync(ConnectionString, tables);

            _queryModel.Tables.Clear();
            _queryModel.Relationships.Clear();

            _queryModel.Tables.AddRange(tables);
            _queryModel.Relationships.AddRange(relationships);

            PositionTables();
            CloseConnectionDialog();
            UpdateSqlPreview();
            await RefreshRelationshipDiagram();
        }
        catch (Exception ex)
        {
            _connectionError = $"Connection failed: {ex.Message}";
        }
        finally
        {
            _isConnecting = false;
            StateHasChanged();
        }
    }

    private async Task HandleExcelTablesImported(List<TableModel> tables)
    {
        if (tables?.Any() == true)
        {
            // Set positions for new Excel tables
            PositionExcelTables(tables);

            // Mark tables as Excel tables for styling
            foreach (var table in tables)
            {
                table.IsFromExcel = true;
                // Ensure proper sizing for Excel tables
                if (table.Size.Width == 0)
                    table.Size.Width = 280;
                if (table.Size.Height == 0)
                    table.Size.Height = Math.Max(150, 40 + (table.Columns.Count * 30));
            }

            // Add Excel tables to the query model
            _queryModel.Tables.AddRange(tables);

            // Update SQL preview
            UpdateSqlPreview();

            // Refresh the canvas
            await RefreshRelationshipDiagram();

            // Close the Excel modal
            CloseExcelDialog();

            // Show success message
            try
            {
                if (_jsModule != null)
                {
                    await _jsModule.InvokeVoidAsync("showDomainFeedback",
                        $"Successfully imported {tables.Count} Excel sheet(s) to canvas", "success");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing feedback: {ex.Message}");
            }

            StateHasChanged();
        }
    }

    private void PositionExcelTables(List<TableModel> excelTables)
    {
        if (!excelTables.Any()) return;

        // Find the rightmost position of existing tables to avoid overlap
        int startX = 50;
        int startY = 50;

        if (_queryModel.Tables.Any())
        {
            var existingTables = _queryModel.Tables.Where(t => !excelTables.Contains(t)).ToList();
            if (existingTables.Any())
            {
                startX = existingTables.Max(t => t.Position.X + t.Size.Width) + 100;
            }
        }

        int currentX = startX;
        int currentY = startY;
        int maxHeight = 0;
        const int tableSpacing = 320;
        const int rowSpacing = 200;
        const int tablesPerRow = 3;

        for (int i = 0; i < excelTables.Count; i++)
        {
            var table = excelTables[i];

            table.Position = new Position { X = currentX, Y = currentY };

            currentX += tableSpacing;
            maxHeight = Math.Max(maxHeight, table.Size.Height);

            if ((i + 1) % tablesPerRow == 0)
            {
                currentX = startX;
                currentY += maxHeight + rowSpacing;
                maxHeight = 0;
            }
        }
    }

    private bool GetExcelTableSelected(int index)
    {
        return index >= 0 && index < _excelTableSelections.Count ? _excelTableSelections[index] : false;
    }

    private void SetExcelTableSelected(int index, bool value)
    {
        if (index >= 0 && index < _excelTableSelections.Count)
        {
            _excelTableSelections[index] = value;
            StateHasChanged();
        }
    }

    private async Task HandleRelationshipsSuggested(List<RelationshipModel> relationships)
    {
        if (relationships?.Any() == true)
        {
            _queryModel.Relationships.AddRange(relationships);
            UpdateSqlPreview();
            await RefreshRelationshipDiagram();

            try
            {
                if (_jsModule != null)
                {
                    await _jsModule.InvokeVoidAsync("showDomainFeedback",
                        $"AI suggested {relationships.Count} potential relationship(s)", "success");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing AI feedback: {ex.Message}");
            }

            StateHasChanged();
        }
    }

    private void PositionTables()
    {
        int x = 50, y = 50;
        int maxHeight = 0;

        foreach (var table in _queryModel.Tables)
        {
            table.Position = new Position { X = x, Y = y };

            x += 320; // Increased spacing to prevent overlap
            maxHeight = Math.Max(maxHeight, (int)table.Size.Height);

            if (x > 1200)
            {
                x = 50;
                y += maxHeight + 80; // Increased vertical spacing
                maxHeight = 0;
            }
        }
    }

    private void AddNewTable()
    {
        var newTable = new TableModel
        {
            Name = "NewTable",
            Alias = "nt",
            Schema = "dbo",
            Position = new Position { X = 100, Y = 100 }
        };

        newTable.Columns.Add(new ColumnModel { Name = "Id", DataType = "int", IsPrimaryKey = true });
        newTable.Columns.Add(new ColumnModel { Name = "CreatedBy", DataType = "nvarchar" });
        newTable.Columns.Add(new ColumnModel { Name = "CreatedAt", DataType = "datetime2" });
        newTable.Columns.Add(new ColumnModel { Name = "ModifiedBy", DataType = "nvarchar" });
        newTable.Columns.Add(new ColumnModel { Name = "ModifiedAt", DataType = "datetime2" });

        _queryModel.Tables.Add(newTable);
        UpdateSqlPreview();
    }

    private void SelectTable(TableModel table)
    {
        _selectedTable = table;
        StateHasChanged();
    }

    private void RemoveTable(TableModel table)
    {
        _queryModel.Tables.Remove(table);
        _queryModel.Relationships.RemoveAll(r =>
        r.SourceTableId == table.Id || r.TargetTableId == table.Id);
        UpdateSqlPreview();
        StateHasChanged();
    }

    private void EditRelationship(RelationshipModel relationship)
    {
        ShowJoinTypeModal(relationship.Id);
    }

    private async Task ToggleDomain(DomainModel domain)
    {
        domain.IsCollapsed = !domain.IsCollapsed;

        await UpdateTableVisibilityForDomain(domain.Id, !domain.IsCollapsed);

        StateHasChanged();
    }

    // Method to update table visibility for a specific domain
    private async Task UpdateTableVisibilityForDomain(string domainId, bool isVisible)
    {
        try
        {
            var tablesInDomain = _queryModel.Tables.Where(t => t.DomainId == domainId).ToList();

            foreach (var table in tablesInDomain)
            {
                // Update table visibility state (you might need to add this property to TableModel)
                table.IsVisible = isVisible;
            }

            // Update the DOM through JavaScript
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("updateTableVisibilityForDomain", domainId, isVisible);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in UpdateTableVisibilityForDomain (domainId={domainId}): {ex}");
            await JSRuntime.InvokeVoidAsync("console.error", $"UpdateTableVisibilityForDomain failed: {ex}");
        }

        StateHasChanged();
    }

    // JavaScript callable method for domain collapse/expand
    [JSInvokable]
    public async Task ToggleDomainFromJS(string domainId)
    {
        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == domainId);
        if (domain != null)
        {
            await ToggleDomain(domain);
        }
    }

    // Method to show/hide all tables in domain
    [JSInvokable]
    public async Task SetDomainCollapsed(string domainId, bool collapsed)
    {
        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == domainId);
        if (domain != null)
        {
            domain.IsCollapsed = collapsed;
            await UpdateTableVisibilityForDomain(domainId, !collapsed);
        }
    }


    private void AddValidationRule()
    {
        _validationRules.Add(new ValidationRule
        {
            Name = "New Rule",
            RuleType = "SQL",
            IsActive = true
        });
    }

    private void RemoveValidationRule(ValidationRule rule)
    {
        _validationRules.Remove(rule);
    }

    private void UpdateSqlPreview(QueryModel? customQueryModel = null)
    {
        try
        {
            var modelToUse = customQueryModel ?? _queryModel;
            _generatedSql = SqlGenerator.GenerateQuery(modelToUse);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _generatedSql = $"-- Error generating SQL: {ex.Message}";
        }
    }

    private void GenerateQuery()
    {
        // Include both SQL and Excel tables that have relationships or selected columns
        var relevantTables = _queryModel.Tables.Where(t =>
            t.Columns.Any(c => c.IsSelected) ||
            _queryModel.Relationships.Any(r => r.SourceTableId == t.Id || r.TargetTableId == t.Id)
        ).ToList();

        if (!relevantTables.Any())
        {
            _generatedSql = @"-- No tables selected or connected.
                            -- Please select columns or create relationships between tables.
                            -- For Excel tables: Select columns to include in your query.
                            -- For SQL Server tables: Connect to database and select tables.";
            StateHasChanged();
            return;
        }

        // Separate Excel and SQL tables for different handling if needed
        var excelTables = relevantTables.Where(t => t.IsFromExcel).ToList();
        var sqlTables = relevantTables.Where(t => !t.IsFromExcel).ToList();

        // If we have Excel tables, add a comment explaining the generated query
        if (excelTables.Any())
        {
            var hasConnections = _queryModel.Relationships.Any();
            var prefix = hasConnections
                ? "-- Query combining Excel data with database tables\n"
                : "-- Query based on Excel sheet structure\n-- Note: This shows the SQL structure. Excel data would need to be imported to a database to execute.\n\n";

            var query = SqlGenerator.GenerateQuery(_queryModel);
            _generatedSql = prefix + query;
        }
        else
        {
            UpdateSqlPreview();
        }
        StateHasChanged();
    }

    private async Task ClearExcelTables()
    {
        var excelTables = _queryModel.Tables.Where(t => t.IsFromExcel).ToList();

        if (excelTables.Any())
        {
            // Remove relationships that involve Excel tables
            var relationshipsToRemove = _queryModel.Relationships.Where(r =>
                excelTables.Any(t => t.Id == r.SourceTableId) ||
                excelTables.Any(t => t.Id == r.TargetTableId)
            ).ToList();

            foreach (var rel in relationshipsToRemove)
            {
                _queryModel.Relationships.Remove(rel);
            }

            // Remove Excel tables
            foreach (var table in excelTables)
            {
                _queryModel.Tables.Remove(table);
            }

            UpdateSqlPreview();
            await RefreshRelationshipDiagram();
            StateHasChanged();

            try
            {
                if (_jsModule != null)
                {
                    await _jsModule.InvokeVoidAsync("showDomainFeedback",
                        $"Removed {excelTables.Count} Excel table(s) from canvas", "success");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing clear feedback: {ex.Message}");
            }
        }
    }

    private async Task ShowExcelTableActions()
    {
        var excelTableCount = _queryModel.Tables.Count(t => t.IsFromExcel);

        if (excelTableCount == 0)
        {
            await ShowExcelUpload();
        }
        else
        {
            // Show context menu or actions for existing Excel tables
            // This could be expanded to show options like "Add More Excel Files", "Clear Excel Tables", etc.
            await ShowExcelUpload(); // For now, just show upload dialog
        }
    }


    [JSInvokable]
    public async Task UpdateTablePosition(string tableId, double x, double y)
    {
        var table = _queryModel.Tables.FirstOrDefault(t => t.Id == tableId);
        if (table != null)
        {
            table.Position.X = (int)x;
            table.Position.Y = (int)y;
            UpdateSqlPreview();

            // Update relationship lines after table position change
            await RefreshRelationshipDiagram();
        }
    }


    private async Task GenerateAndExecuteQuery()
    {
        GenerateQuery();

        if (string.IsNullOrEmpty(_generatedSql))
            return;

        try
        {
            // Check if we have Excel tables in the query
            var hasExcelTables = _queryModel.Tables.Any(t => t.IsFromExcel);
            var hasSqlTables = _queryModel.Tables.Any(t => !t.IsFromExcel);
            var relevantTables = _queryModel.Tables.Where(t =>
                t.Columns.Any(c => c.IsSelected) ||
                _queryModel.Relationships.Any(r => r.SourceTableId == t.Id || r.TargetTableId == t.Id)
            ).ToList();

            if (!relevantTables.Any())
            {
                _queryResults = null;
                StateHasChanged();
                return;
            }

            if (hasExcelTables && !hasSqlTables)
            {
                // Pure Excel query - generate mock results from Excel structure
                _queryResults = SchemaService.GenerateExcelMockResults(relevantTables);
            }
            else if (!hasExcelTables && hasSqlTables && !string.IsNullOrEmpty(ConnectionString))
            {
                // Pure SQL Server query
                _queryResults = await SchemaService.ExecuteQueryAsync(ConnectionString, _generatedSql, 100);
            }
            else if (hasExcelTables && hasSqlTables)
            {
                // Mixed query - show structure but indicate it needs data integration
                _queryResults = SchemaService.GenerateMixedMockResults(relevantTables);
            }
            else
            {
                // No connection string for SQL tables
                _queryResults = null;
                if (hasSqlTables)
                {
                    ShowConnectionDialog();
                    return;
                }
            }

            if (OnQueryGenerated.HasDelegate)
                await OnQueryGenerated.InvokeAsync(_generatedSql);

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Query execution error: {ex.Message}");

            // Create error result
            _queryResults = new DataTable();
            _queryResults.Columns.Add("Error", typeof(string));
            var errorRow = _queryResults.NewRow();
            errorRow["Error"] = $"Query execution failed: {ex.Message}";
            _queryResults.Rows.Add(errorRow);

            StateHasChanged();
        }
    }

    private async Task SaveLayout()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_queryModel);

            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("saveToLocalStorage", "sqlBuilderLayout", json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SaveLayout: {ex}");
            await JSRuntime.InvokeVoidAsync("console.error", $"SaveLayout failed: {ex}");
        }

    }

    private async Task LoadLayout()
    {
        try
        {
            if (_jsModule != null)
            {
                var json = await _jsModule.InvokeAsync<string>("loadFromLocalStorage", "sqlBuilderLayout");
                if (!string.IsNullOrEmpty(json))
                {
                    _queryModel = System.Text.Json.JsonSerializer.Deserialize<QueryModel>(json) ?? new QueryModel();
                    StateHasChanged();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading layout: {ex.Message}");
        }
    }

    private async Task ZoomIn()
    {
        try
        {
            _zoomLevel = Math.Min(_zoomLevel * 1.2, 3.0);
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("setCanvasZoom", _zoomLevel);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ZoomIn: {ex}");
            await JSRuntime.InvokeVoidAsync("console.error", $"ZoomIn failed: {ex}");
        }

    }

    private async Task ZoomOut()
    {
        try
        {
            _zoomLevel = Math.Max(_zoomLevel / 1.2, 0.3);
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("setCanvasZoom", _zoomLevel);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ZoomOut: {ex}");
            await JSRuntime.InvokeVoidAsync("console.error", $"ZoomOut failed: {ex}");
        }

    }

    private async Task ResetZoom()
    {
        try
        {
            _zoomLevel = 1.0;
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("setCanvasZoom", _zoomLevel);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ResetZoom: {ex}");
            await JSRuntime.InvokeVoidAsync("console.error", $"ResetZoom failed: {ex}");
        }

    }

    // Modal event handlers
    private void ShowAddColumnModal(TableModel table)
    {
        selectedTableForColumn = table;
        newColumnName = "";
        newColumnDataType = "nvarchar";
        newColumnMaxLength = null;
        newColumnIsNullable = true;
        newColumnIsPrimaryKey = false;
        newColumnIsForeignKey = false;
        newColumnExpression = "";
        showAddColumnModal = true;
        StateHasChanged();
    }

    private void CloseAddColumnModal()
    {
        showAddColumnModal = false;
        selectedTableForColumn = null;
        StateHasChanged();
    }

    private void ConfirmAddColumn()
    {
        if (selectedTableForColumn != null && !string.IsNullOrWhiteSpace(newColumnName))
        {
            var newColumn = new ColumnModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = newColumnName,
                DataType = newColumnDataType,
                MaxLength = newColumnMaxLength,
                IsNullable = newColumnIsNullable,
                IsPrimaryKey = newColumnIsPrimaryKey,
                IsForeignKey = newColumnIsForeignKey,
                IsComputed = newColumnDataType == "computed",
                ComputedExpression = newColumnDataType == "computed" ? newColumnExpression : null
            };

            selectedTableForColumn.Columns.Add(newColumn);
            UpdateSqlPreview();
            CloseAddColumnModal();
        }
    }

    //Calculate connector position for precise dot-to-dot connections
    private Position GetConnectorPosition(TableModel table, ColumnModel column, string side)
    {
        var columnIndex = table.Columns.IndexOf(column);
        var headerHeight = 40;
        var rowHeight = 30;
        var connectorOffset = side == "right" ? table.Size.Width : 0;

        return new Position
        {
            X = table.Position.X + connectorOffset,
            Y = table.Position.Y + headerHeight + (columnIndex * rowHeight) + (rowHeight / 2)
        };
    }

    //Refresh relationship diagram
    private async Task RefreshRelationshipDiagram()
    {
        try
        {
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("refreshAllRelationshipLines");
            }
        }
        catch (JSDisconnectedException)
        {
            // Ignore, circuit is gone
        }
        catch (ObjectDisposedException)
        {
            // Ignore, cleanup already in progress
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in RefreshRelationshipDiagram: {ex}");
            await JSRuntime.InvokeVoidAsync("console.error", $"RefreshRelationshipDiagram failed: {ex}");
        }

        StateHasChanged();
    }

    [JSInvokable]
    public void DeleteRelationship(string relationshipId)
    {
        var relationship = _queryModel.Relationships.FirstOrDefault(r => r.Id == relationshipId);
        if (relationship != null)
        {
            _queryModel.Relationships.Remove(relationship);
            UpdateSqlPreview();
            StateHasChanged();
        }
    }

    private void ShowCreateDomainModal()
    {
        newDomainName = "";
        newDomainColor = "#e3f2fd";
        showCreateDomainModal = true;
        StateHasChanged();
    }

    private void CloseCreateDomainModal()
    {
        showCreateDomainModal = false;
        StateHasChanged();
    }

    private void ConfirmCreateDomain()
    {
        if (!string.IsNullOrWhiteSpace(newDomainName))
        {
            CreateDomain(newDomainName, new Position { X = 100, Y = 100 },
            new Size { Width = 400, Height = 300 }, newDomainColor);
            CloseCreateDomainModal();
        }
    }

    // Add these methods to your SqlQueryBuilder.razor component

    [JSInvokable]
    public async Task AssignTableToDomain(string tableId, string domainId)
    {
        var table = _queryModel.Tables.FirstOrDefault(t => t.Id == tableId);
        if (table != null)
        {
            var oldDomainId = table.DomainId;
            table.DomainId = domainId;

            if (!string.IsNullOrEmpty(domainId))
            {
                await MoveTableToDomain(table, domainId);
            }

            // Adjust old domain if table was previously assigned
            if (!string.IsNullOrEmpty(oldDomainId))
            {
                await AdjustDomainSize(oldDomainId);
            }

            // Adjust new domain
            if (!string.IsNullOrEmpty(domainId))
            {
                await AdjustDomainSize(domainId);
            }

            StateHasChanged();
            await RefreshRelationshipDiagram();
        }
    }

    private async Task MoveTableToDomain(TableModel table, string domainId)
    {
        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == domainId);
        if (domain == null) return;

        // Find optimal position within domain
        var newPosition = FindOptimalPositionInDomain(table, domain);

        table.Position.X = newPosition.X;
        table.Position.Y = newPosition.Y;

        try
        {
            // Update the DOM element position
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("updateTablePosition", table.Id, newPosition.X, newPosition.Y);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in MoveTableToDomain: {ex}");
            await JSRuntime.InvokeVoidAsync("console.error", $"MoveTableToDomain failed: {ex}");
        }

    }

    private Position FindOptimalPositionInDomain(TableModel table, DomainModel domain)
    {
        const int padding = 20;
        const int headerHeight = 40;
        const int tableSpacing = 20;

        // Get tables already in this domain
        var tablesInDomain = _queryModel.Tables
        .Where(t => t.DomainId == domain.Id && t.Id != table.Id)
        .ToList();

        // Start position (top-left of domain with padding)
        var startX = domain.Position.X + padding;
        var startY = domain.Position.Y + headerHeight + padding;

        if (!tablesInDomain.Any())
        {
            return new Position { X = startX, Y = startY };
        }

        // Try to place table in a grid pattern within domain
        var currentX = startX;
        var currentY = startY;
        var maxHeightInRow = 0;
        var tablesPerRow = Math.Max(1, (domain.Size.Width - (padding * 2)) / (table.Size.Width + tableSpacing));

        // Find next available position
        var occupiedPositions = tablesInDomain.Select(t => new Rectangle
        {
            X = t.Position.X,
            Y = t.Position.Y,
            Width = t.Size.Width,
            Height = t.Size.Height
        }).ToList();

        while (IsPositionOccupied(currentX, currentY, table.Size.Width, table.Size.Height, occupiedPositions))
        {
            currentX += table.Size.Width + tableSpacing;

            if (currentX + table.Size.Width > domain.Position.X + domain.Size.Width - padding)
            {
                currentX = startX;
                currentY += maxHeightInRow + tableSpacing;
                maxHeightInRow = 0;
            }

            maxHeightInRow = Math.Max(maxHeightInRow, table.Size.Height);
        }

        return new Position { X = currentX, Y = currentY };
    }

    private bool IsPositionOccupied(int x, int y, int width, int height, List<Rectangle> occupiedPositions)
    {
        var testRect = new Rectangle { X = x, Y = y, Width = width, Height = height };

        return occupiedPositions.Any(occupied =>
        testRect.X < occupied.X + occupied.Width + 10 &&
        testRect.X + testRect.Width + 10 > occupied.X &&
        testRect.Y < occupied.Y + occupied.Height + 10 &&
        testRect.Y + testRect.Height + 10 > occupied.Y
        );
    }

    private async Task AdjustDomainSize(string domainId)
    {
        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == domainId);
        if (domain == null) return;

        var tablesInDomain = _queryModel.Tables.Where(t => t.DomainId == domainId).ToList();

        if (!tablesInDomain.Any())
        {
            // If no tables, set minimum size
            domain.Size.Width = 300;
            domain.Size.Height = 200;
            return;
        }

        const int padding = 20;
        const int headerHeight = 40;

        // Calculate bounds needed to contain all tables
        var minX = tablesInDomain.Min(t => t.Position.X);
        var minY = tablesInDomain.Min(t => t.Position.Y);
        var maxX = tablesInDomain.Max(t => t.Position.X + t.Size.Width);
        var maxY = tablesInDomain.Max(t => t.Position.Y + t.Size.Height);

        // Ensure domain position encompasses all tables
        var newDomainX = Math.Min(domain.Position.X, minX - padding);
        var newDomainY = Math.Min(domain.Position.Y, minY - headerHeight - padding);

        // Calculate required size
        var requiredWidth = maxX - newDomainX + padding;
        var requiredHeight = maxY - newDomainY + padding;

        // Update domain bounds
        domain.Position.X = newDomainX;
        domain.Position.Y = newDomainY;
        domain.Size.Width = Math.Max(300, requiredWidth); // Minimum width
        domain.Size.Height = Math.Max(200, requiredHeight); // Minimum height

        StateHasChanged();
    }

    // Update the table assignment in properties panel
    private async Task OnTableDomainChanged(ChangeEventArgs e)
    {
        if (_selectedTable != null && e.Value != null)
        {
            var newDomainId = e.Value.ToString();
            await AssignTableToDomain(_selectedTable.Id, newDomainId);
        }
    }

    // Add these methods to your SqlQueryBuilder.razor component

    // Domain selection for properties editing
    private DomainModel? _selectedDomain;

    private async Task SelectDomain(DomainModel domain)
    {
        _selectedDomain = domain;
        // Clear table selection when domain is selected
        _selectedTable = null;
        StateHasChanged();

        try
        {
            // Optional: Highlight the domain visually
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("highlightDomain", domain.Id);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SelectDomain: {ex}");
            await JSRuntime.InvokeVoidAsync("console.error", $"SelectDomain failed: {ex}");
        }
    }

    private async Task DeleteDomain(string domainId)
    {
        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == domainId);
        if (domain == null) return;

        // Get tables currently assigned to this domain
        var tablesInDomain = _queryModel.Tables.Where(t => t.DomainId == domainId).ToList();

        // Confirm deletion if domain contains tables
        if (tablesInDomain.Any())
        {
            // You might want to show a confirmation modal here
            // For now, we'll just unassign the tables
            foreach (var table in tablesInDomain)
            {
                table.DomainId = null;
            }
        }

        // Remove the domain
        _queryModel.Domains.Remove(domain);

        // Clear selection if deleted domain was selected
        if (_selectedDomain?.Id == domainId)
        {
            _selectedDomain = null;
        }

        StateHasChanged();
        UpdateSqlPreview();
    }

    // Enhanced delete with confirmation modal
    private string? _domainIdToDelete;
    private bool _showDeleteDomainConfirmation = false;
    private string _deleteDomainWarning = "";

    private void ShowDeleteDomainConfirmation(string domainId)
    {
        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == domainId);
        if (domain == null) return;

        var tablesInDomain = _queryModel.Tables.Count(t => t.DomainId == domainId);

        _domainIdToDelete = domainId;
        _deleteDomainWarning = tablesInDomain > 0
        ? $"This will remove the domain '{domain.Name}' and unassign {tablesInDomain} table(s). This action cannot be undone."
        : $"This will permanently delete the domain '{domain.Name}'. This action cannot be undone.";

        _showDeleteDomainConfirmation = true;
        StateHasChanged();
    }

    private async Task ConfirmDeleteDomain()
    {
        if (!string.IsNullOrEmpty(_domainIdToDelete))
        {
            await DeleteDomain(_domainIdToDelete);
            CloseDeleteDomainConfirmation();
        }
    }

    private void CloseDeleteDomainConfirmation()
    {
        _domainIdToDelete = null;
        _deleteDomainWarning = "";
        _showDeleteDomainConfirmation = false;
        StateHasChanged();
    }


    private void OnDomainNameChanged()
    {
        if (_selectedDomain != null)
        {
            StateHasChanged();
        }
    }

    private async Task OnDomainColorChanged()
    {
        if (_selectedDomain != null)
        {
            StateHasChanged();

            try
            {
                // Update the visual domain color in real-time
                if (_jsModule != null)
                {
                    await _jsModule.InvokeVoidAsync("updateDomainColor", _selectedDomain.Id, _selectedDomain.Color);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnDomainColorChanged: {ex}");
                await JSRuntime.InvokeVoidAsync("console.error", $"OnDomainColorChanged failed: {ex}");
            }

        }
    }

    // JavaScript-callable method for domain selection from canvas
    [JSInvokable]
    public void SelectDomainFromCanvas(string domainId)
    {
        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == domainId);
        if (domain != null)
        {
            SelectDomain(domain);
        }
    }

    private async Task UnassignTableFromDomain(string tableId)
    {
        var table = _queryModel.Tables.FirstOrDefault(t => t.Id == tableId);
        if (table != null)
        {
            var oldDomainId = table.DomainId;
            table.DomainId = null;

            // Adjust the domain size after removing table
            if (!string.IsNullOrEmpty(oldDomainId))
            {
                await AdjustDomainSize(oldDomainId);
            }

            StateHasChanged();
            UpdateSqlPreview();
            await RefreshRelationshipDiagram();
        }
    }

    // Add these final helper methods to your SqlQueryBuilder.razor component

    [JSInvokable]
    public async Task<string?> GetTableDomain(string tableId)
    {
        var table = _queryModel.Tables.FirstOrDefault(t => t.Id == tableId);
        return table?.DomainId;
    }

    [JSInvokable]
    public async Task<object> GetTableDomainConstraints(string tableId, double newX, double newY)
    {
        var table = _queryModel.Tables.FirstOrDefault(t => t.Id == tableId);
        if (table == null || string.IsNullOrEmpty(table.DomainId))
        {
            return new { x = newX, y = newY }; // No constraints
        }

        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == table.DomainId);
        if (domain == null)
        {
            return new { x = newX, y = newY }; // No constraints
        }

        const int padding = 20;
        const int headerHeight = 40;

        // Calculate domain boundaries
        var minX = domain.Position.X + padding;
        var minY = domain.Position.Y + headerHeight + padding;
        var maxX = domain.Position.X + domain.Size.Width - table.Size.Width - padding;
        var maxY = domain.Position.Y + domain.Size.Height - table.Size.Height - padding;

        // Constrain the position within domain bounds
        var constrainedX = Math.Max(minX, Math.Min(maxX, newX));
        var constrainedY = Math.Max(minY, Math.Min(maxY, newY));

        return new { x = constrainedX, y = constrainedY };
    }

    [JSInvokable]
    public async Task<object> GetDomainConstraints(string tableId)
    {
        var table = _queryModel.Tables.FirstOrDefault(t => t.Id == tableId);
        if (table == null || string.IsNullOrEmpty(table.DomainId))
        {
            return new { hasDomain = false };
        }

        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == table.DomainId);
        if (domain == null)
        {
            return new { hasDomain = false };
        }

        const int padding = 20;
        const int headerHeight = 40;

        return new
        {
            hasDomain = true,
            domainId = domain.Id,
            domainName = domain.Name,
            minX = domain.Position.X + padding,
            minY = domain.Position.Y + headerHeight + padding,
            maxX = domain.Position.X + domain.Size.Width - table.Size.Width - padding,
            maxY = domain.Position.Y + domain.Size.Height - table.Size.Height - padding
        };
    }

    // Enhanced domain creation with positioning
    private async Task CreateDomainAtPosition(string name, int x, int y, string color)
    {
        var domain = new DomainModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Position = new Position { X = x, Y = y },
            Size = new Size { Width = 400, Height = 300 },
            Color = color,
            IsCollapsed = false
        };

        _queryModel.Domains.Add(domain);

        try
        {
            // Show success feedback
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("showDomainFeedback", $"Domain '{name}' created successfully", "success");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in CreateDomainAtPosition: {ex}");
            await JSRuntime.InvokeVoidAsync("console.error", $"CreateDomainAtPosition failed: {ex}");
        }


        StateHasChanged();
    }

    // Method to handle domain creation from canvas right-click
    [JSInvokable]
    public void CreateDomainAtMousePosition(int x, int y)
    {
        var domainName = $"Domain {_queryModel.Domains.Count + 1}";
        CreateDomainAtPosition(domainName, x - 200, y - 150, "#e3f2fd");
    }

    [JSInvokable]
    public async Task AutoArrangeDomainsAndTables()
    {
        if (isAutoArranging) return;

        isAutoArranging = true;
        StateHasChanged();

        try
        {
            // Step 1: Auto-arrange domains first
            await AutoArrangeDomains();
            await Task.Delay(100); // Small delay for visual effect

            // Step 2: Auto-arrange tables within their domains
            await AutoArrangeTablesWithinDomains();
            await Task.Delay(100);

            // Step 3: Auto-arrange orphan tables (not in domains)
            await AutoArrangeOrphanTables();

            // Step 4: Update relationships
            await RefreshRelationshipDiagram();

            UpdateSqlPreview();
            StateHasChanged();

            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("showDomainFeedback",
                    "All domains and tables auto-arranged successfully", "success");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during auto-arrangement: {ex.Message}");

            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("showDomainFeedback",
                    "Error during auto-arrangement", "error");
            }
        }
        finally
        {
            isAutoArranging = false;
            StateHasChanged();
        }
    }

    // Auto-arrange tables within their assigned domains
    private async Task AutoArrangeTablesWithinDomains()
    {
        var domainsWithTables = _queryModel.Domains
            .Where(d => _queryModel.Tables.Any(t => t.DomainId == d.Id))
            .ToList();

        foreach (var domain in domainsWithTables)
        {
            var tablesInDomain = _queryModel.Tables
                .Where(t => t.DomainId == domain.Id)
                .ToList();

            if (!tablesInDomain.Any()) continue;

            const int tablePadding = 20;
            const int headerHeight = 40;
            const int tablesPerRow = 2;

            int currentX = domain.Position.X + tablePadding;
            int currentY = domain.Position.Y + headerHeight + tablePadding;
            int maxHeightInRow = 0;
            int tableIndex = 0;

            foreach (var table in tablesInDomain)
            {
                table.Position.X = currentX;
                table.Position.Y = currentY;

                currentX += table.Size.Width + tablePadding;
                maxHeightInRow = Math.Max(maxHeightInRow, table.Size.Height);

                tableIndex++;
                if (tableIndex % tablesPerRow == 0)
                {
                    currentX = domain.Position.X + tablePadding;
                    currentY += maxHeightInRow + tablePadding;
                    maxHeightInRow = 0;
                }
            }

            // Adjust domain size to fit all tables
            await AdjustDomainSizeWithCollisionDetection(domain.Id);
        }
    }

    // Auto-arrange tables that don't belong to any domain
    private async Task AutoArrangeOrphanTables()
    {
        var orphanTables = _queryModel.Tables
            .Where(t => string.IsNullOrEmpty(t.DomainId))
            .ToList();

        if (!orphanTables.Any()) return;

        // Find available space to the right of domains
        int startX = 50;
        if (_queryModel.Domains.Any())
        {
            startX = _queryModel.Domains.Max(d => d.Position.X + d.Size.Width) + 100;
        }

        int currentX = startX;
        int currentY = 50;
        int maxHeightInRow = 0;
        const int tableSpacing = 320;
        const int rowSpacing = 200;
        const int tablesPerRow = 3;

        foreach (var table in orphanTables)
        {
            table.Position.X = currentX;
            table.Position.Y = currentY;

            currentX += tableSpacing;
            maxHeightInRow = Math.Max(maxHeightInRow, table.Size.Height);

            if (currentX > startX + (tableSpacing * tablesPerRow))
            {
                currentX = startX;
                currentY += maxHeightInRow + rowSpacing;
                maxHeightInRow = 0;
            }
        }
    }

    // Method to detect and resolve domain overlaps
    private async Task ResolveDomainOverlaps()
    {
        var overlaps = DetectDomainOverlaps();

        foreach (var overlap in overlaps)
        {
            var adjustedPosition = FindNearestNonCollidingDomainPosition(
                overlap.Domain, overlap.Domain.Position);

            overlap.Domain.Position = adjustedPosition;

            try
            {
                if (_jsModule != null)
                {
                    await _jsModule.InvokeVoidAsync("updateDomainBoundsAnimated",
                        overlap.Domain.Id, adjustedPosition.X, adjustedPosition.Y,
                        overlap.Domain.Size.Width, overlap.Domain.Size.Height, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ResolveDomainOverlaps: {ex}");
                await JSRuntime.InvokeVoidAsync("console.error", $"ResolveDomainOverlaps failed: {ex}");
            }
        }

        StateHasChanged();
    }

    // Detect overlapping domains
    private List<DomainOverlap> DetectDomainOverlaps()
    {
        var overlaps = new List<DomainOverlap>();

        for (int i = 0; i < _queryModel.Domains.Count; i++)
        {
            for (int j = i + 1; j < _queryModel.Domains.Count; j++)
            {
                var domain1 = _queryModel.Domains[i];
                var domain2 = _queryModel.Domains[j];

                var rect1 = new Rectangle
                {
                    X = domain1.Position.X,
                    Y = domain1.Position.Y,
                    Width = domain1.Size.Width,
                    Height = domain1.Size.Height
                };

                var rect2 = new Rectangle
                {
                    X = domain2.Position.X,
                    Y = domain2.Position.Y,
                    Width = domain2.Size.Width,
                    Height = domain2.Size.Height
                };

                if (DomainsOverlap(rect1, rect2))
                {
                    overlaps.Add(new DomainOverlap
                    {
                        Domain = domain1,
                        OverlapsWith = domain2,
                        OverlapArea = CalculateOverlapArea(rect1, rect2)
                    });
                }
            }
        }

        return overlaps.OrderByDescending(o => o.OverlapArea).ToList();
    }

    // Calculate overlap area between two rectangles
    private int CalculateOverlapArea(Rectangle rect1, Rectangle rect2)
    {
        int overlapX = Math.Max(0, Math.Min(rect1.X + rect1.Width, rect2.X + rect2.Width) -
                                  Math.Max(rect1.X, rect2.X));
        int overlapY = Math.Max(0, Math.Min(rect1.Y + rect1.Height, rect2.Y + rect2.Height) -
                                  Math.Max(rect1.Y, rect2.Y));
        return overlapX * overlapY;
    }

    // Helper class for domain overlap detection
    public class DomainOverlap
    {
        public DomainModel Domain { get; set; }
        public DomainModel OverlapsWith { get; set; }
        public int OverlapArea { get; set; }
    }

    // JavaScript callable method for updating domain size
    [JSInvokable]
    public async Task UpdateDomainSize(string domainId, double width, double height)
    {
        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == domainId);
        if (domain != null)
        {
            domain.Size.Width = (int)width;
            domain.Size.Height = (int)height;
            StateHasChanged();
        }
    }

    // Method to show domain collision statistics in properties panel
    private object GetDomainCollisionStats()
    {
        var overlaps = DetectDomainOverlaps();
        var orphanTables = _queryModel.Tables.Count(t => string.IsNullOrEmpty(t.DomainId));
        var totalDomains = _queryModel.Domains.Count;

        return new
        {
            TotalOverlaps = overlaps.Count,
            OrphanTables = orphanTables,
            TotalDomains = totalDomains,
            HasCollisions = overlaps.Any()
        };
    }

    // Add these methods to your SqlQueryBuilder.razor component for domain collision detection

    private bool CheckDomainCollision(DomainModel movingDomain, Position newPosition)
    {
        var movingRect = new Rectangle
        {
            X = newPosition.X,
            Y = newPosition.Y,
            Width = movingDomain.Size.Width,
            Height = movingDomain.Size.Height
        };

        foreach (var domain in _queryModel.Domains)
        {
            if (domain.Id == movingDomain.Id) continue;

            var existingRect = new Rectangle
            {
                X = domain.Position.X,
                Y = domain.Position.Y,
                Width = domain.Size.Width,
                Height = domain.Size.Height
            };

            if (DomainsOverlap(movingRect, existingRect))
            {
                return true;
            }
        }

        return false;
    }

    private bool DomainsOverlap(Rectangle rect1, Rectangle rect2)
    {
        const int buffer = 30; // Minimum spacing between domains

        return !(rect1.X + rect1.Width + buffer < rect2.X ||
                 rect2.X + rect2.Width + buffer < rect1.X ||
                 rect1.Y + rect1.Height + buffer < rect2.Y ||
                 rect2.Y + rect2.Height + buffer < rect1.Y);
    }

    private Position FindNearestNonCollidingDomainPosition(DomainModel domain, Position targetPosition)
    {
        const int step = 20;
        const int maxAttempts = 25;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int radius = attempt * step;

            // Try positions in a spiral pattern around the target
            for (int angle = 0; angle < 360; angle += 45)
            {
                double radians = angle * Math.PI / 180;
                int testX = targetPosition.X + (int)(Math.Cos(radians) * radius);
                int testY = targetPosition.Y + (int)(Math.Sin(radians) * radius);

                var testPosition = new Position { X = testX, Y = testY };

                if (!CheckDomainCollision(domain, testPosition))
                {
                    return testPosition;
                }
            }
        }

        // If no position found, return original with offset
        return new Position
        {
            X = targetPosition.X + 50,
            Y = targetPosition.Y + 50
        };
    }

    // Enhanced domain creation with collision detection
    private async Task CreateDomainWithCollisionDetection(string name, Position position, Size size, string color)
    {
        var domain = new DomainModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Position = position,
            Size = size,
            Color = color,
            IsCollapsed = false
        };

        // Check for collision and adjust position if needed
        if (CheckDomainCollision(domain, position))
        {
            var adjustedPosition = FindNearestNonCollidingDomainPosition(domain, position);
            domain.Position = adjustedPosition;

            try
            {
                // Show feedback about position adjustment
                if (_jsModule != null)
                {
                    await _jsModule.InvokeVoidAsync("showDomainFeedback",
                        $"Domain '{name}' positioned to avoid overlap", "success");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CreateDomainWithCollisionDetection: {ex}");
                await JSRuntime.InvokeVoidAsync("console.error", $"CreateDomainWithCollisionDetection failed: {ex}");
            }

        }

        _queryModel.Domains.Add(domain);
        StateHasChanged();
    }

    // Updated CreateDomain method to use collision detection
    private void CreateDomain(string name, Position position, Size size, string color)
    {
        CreateDomainWithCollisionDetection(name, position, size, color);
    }

    // Auto-arrange domains to prevent overlaps
    private async Task AutoArrangeDomains()
    {
        if (!_queryModel.Domains.Any()) return;

        const int padding = 50;
        const int domainsPerRow = 2;

        int currentX = 50;
        int currentY = 50;
        int maxHeightInRow = 0;
        int domainIndex = 0;

        foreach (var domain in _queryModel.Domains.OrderBy(d => d.Name))
        {
            domain.Position.X = currentX;
            domain.Position.Y = currentY;

            currentX += domain.Size.Width + padding;
            maxHeightInRow = Math.Max(maxHeightInRow, domain.Size.Height);

            domainIndex++;
            if (domainIndex % domainsPerRow == 0)
            {
                currentX = 50;
                currentY += maxHeightInRow + padding;
                maxHeightInRow = 0;
            }
        }

        StateHasChanged();

        try
        {
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("showDomainFeedback", "Domains rearranged to prevent overlaps", "success");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in AutoArrangeDomains: {ex}");
            await JSRuntime.InvokeVoidAsync("console.error", $"AutoArrangeDomains failed: {ex}");
        }

    }

    // JavaScript callable method for domain position updates with collision detection
    [JSInvokable]
    public async Task<object> UpdateDomainPosition(string domainId, double x, double y)
    {
        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == domainId);
        if (domain == null)
        {
            return new { success = false, message = "Domain not found" };
        }

        var newPosition = new Position { X = (int)x, Y = (int)y };

        // Check for collision
        if (CheckDomainCollision(domain, newPosition))
        {
            var adjustedPosition = FindNearestNonCollidingDomainPosition(domain, newPosition);
            domain.Position = adjustedPosition;

            try
            {
                // Update domain bounds in JavaScript
                if (_jsModule != null)
                {
                    await _jsModule.InvokeVoidAsync("updateDomainBoundsAnimated",
                        domainId, adjustedPosition.X, adjustedPosition.Y,
                        domain.Size.Width, domain.Size.Height, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateDomainPosition: {ex}");
                await JSRuntime.InvokeVoidAsync("console.error", $"UpdateDomainPosition failed: {ex}");
            }


            StateHasChanged();

            return new
            {
                success = true,
                adjusted = true,
                x = adjustedPosition.X,
                y = adjustedPosition.Y,
                message = "Position adjusted to prevent overlap"
            };
        }
        else
        {
            domain.Position = newPosition;
            StateHasChanged();

            return new
            {
                success = true,
                adjusted = false,
                x = newPosition.X,
                y = newPosition.Y,
                message = "Position updated"
            };
        }
    }

    // Enhanced domain auto-sizing with collision awareness
    private async Task AdjustDomainSizeWithCollisionDetection(string domainId)
    {
        var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == domainId);
        if (domain == null) return;

        var tablesInDomain = _queryModel.Tables.Where(t => t.DomainId == domainId).ToList();

        if (!tablesInDomain.Any())
        {
            domain.Size.Width = 300;
            domain.Size.Height = 200;
            return;
        }

        const int padding = 20;
        const int headerHeight = 40;

        // Calculate required bounds
        var minX = tablesInDomain.Min(t => t.Position.X);
        var minY = tablesInDomain.Min(t => t.Position.Y);
        var maxX = tablesInDomain.Max(t => t.Position.X + t.Size.Width);
        var maxY = tablesInDomain.Max(t => t.Position.Y + t.Size.Height);

        var originalPosition = new Position { X = domain.Position.X, Y = domain.Position.Y };
        var originalSize = new Size { Width = domain.Size.Width, Height = domain.Size.Height };

        // Calculate new bounds
        var newDomainX = Math.Min(domain.Position.X, minX - padding);
        var newDomainY = Math.Min(domain.Position.Y, minY - headerHeight - padding);
        var requiredWidth = maxX - newDomainX + padding;
        var requiredHeight = maxY - newDomainY + padding;

        // Create temporary domain for collision testing
        var testDomain = new DomainModel
        {
            Id = domain.Id,
            Position = new Position { X = newDomainX, Y = newDomainY },
            Size = new Size
            {
                Width = Math.Max(300, requiredWidth),
                Height = Math.Max(200, requiredHeight)
            }
        };

        // Check if new size would cause collision
        if (CheckDomainCollision(testDomain, testDomain.Position))
        {
            // Try to find alternative positioning
            var adjustedPosition = FindNearestNonCollidingDomainPosition(testDomain, testDomain.Position);

            // If adjusted position is too far from tables, keep original bounds
            var distanceFromTables = Math.Sqrt(
                Math.Pow(adjustedPosition.X - minX, 2) +
                Math.Pow(adjustedPosition.Y - minY, 2));

            if (distanceFromTables > 100) // Too far, keep original
            {
                return;
            }

            domain.Position = adjustedPosition;
        }
        else
        {
            domain.Position.X = newDomainX;
            domain.Position.Y = newDomainY;
        }

        domain.Size.Width = Math.Max(300, requiredWidth);
        domain.Size.Height = Math.Max(200, requiredHeight);

        // Update UI if bounds changed
        if (domain.Position.X != originalPosition.X ||
            domain.Position.Y != originalPosition.Y ||
            domain.Size.Width != originalSize.Width ||
            domain.Size.Height != originalSize.Height)
        {
            try
            {
                if (_jsModule != null)
                {
                    await _jsModule.InvokeVoidAsync("updateDomainBoundsAnimated",
                        domainId, domain.Position.X, domain.Position.Y,
                        domain.Size.Width, domain.Size.Height, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AdjustDomainSizeWithCollisionDetection - updateDomainBoundsAnimated: {ex}");
                await JSRuntime.InvokeVoidAsync("console.error", $"AdjustDomainSizeWithCollisionDetection - updateDomainBoundsAnimated failed: {ex}");
            }

        }

        StateHasChanged();
    }

    private List<TableModel> GetVisibleTables()
    {
        return _queryModel.Tables.Where(table =>
        {
            if (string.IsNullOrEmpty(table.DomainId))
                return true; // Orphan tables are always visible

            var domain = _queryModel.Domains.FirstOrDefault(d => d.Id == table.DomainId);
            return domain == null || !domain.IsCollapsed;
        }).ToList();
    }

    private void GenerateQueryWithVisibilityFilter()
    {
        var visibleTables = GetVisibleTables();

        var relevantTables = visibleTables.Where(t =>
            t.Columns.Any(c => c.IsSelected) ||
            _queryModel.Relationships.Any(r => r.SourceTableId == t.Id || r.TargetTableId == t.Id)
        ).ToList();

        if (!relevantTables.Any())
        {
            _generatedSql = "-- No visible tables selected or connected. Expand domains or select columns.";
            StateHasChanged();
            return;
        }

        // Create query model with only visible tables
        var queryModelForGeneration = new QueryModel
        {
            Tables = relevantTables,
            Relationships = _queryModel.Relationships.Where(r =>
                relevantTables.Any(t => t.Id == r.SourceTableId) &&
                relevantTables.Any(t => t.Id == r.TargetTableId)
            ).ToList()
        };

        UpdateSqlPreview(queryModelForGeneration);
    }

    // Add these methods to your SqlQueryBuilder.razor @code section

    private string GetColumnTypeDisplay(Type columnType)
    {
        if (columnType == typeof(string)) return "Text";
        if (columnType == typeof(int)) return "Number";
        if (columnType == typeof(decimal)) return "Decimal";
        if (columnType == typeof(DateTime)) return "Date/Time";
        if (columnType == typeof(bool)) return "Boolean";
        if (columnType == typeof(Guid)) return "GUID";
        return columnType.Name;
    }

    private string FormatCellValue(object value)
    {
        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            decimal d => d.ToString("N2"),
            double d => d.ToString("N2"),
            float f => f.ToString("N2"),
            bool b => b ? "Yes" : "No",
            _ => value.ToString()
        };
    }

    private async Task ExportResultsToCsv()
    {
        if (_queryResults == null || _queryResults.Rows.Count == 0)
            return;

        try
        {
            var csv = new StringBuilder();

            // Headers
            var headers = _queryResults.Columns.Cast<DataColumn>()
                .Select(column => $"\"{column.ColumnName}\"");
            csv.AppendLine(string.Join(",", headers));

            // Data rows
            foreach (DataRow row in _queryResults.Rows)
            {
                var values = row.ItemArray.Select(field =>
                    field == null || field == DBNull.Value ? "" : $"\"{field.ToString().Replace("\"", "\"\"")}\"");
                csv.AppendLine(string.Join(",", values));
            }

            // Download via JavaScript
            var fileName = $"query_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var content = csv.ToString();

            await JSRuntime.InvokeVoidAsync("downloadFile", fileName, content, "text/csv");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Export error: {ex.Message}");
        }
    }


    private async Task CopyResultsToClipboard()
    {
        if (_queryResults == null)
            return;

        try
        {
            var result = new StringBuilder();
            result.AppendLine($"Query Results ({_queryResults.Rows.Count} rows)");
            result.AppendLine(new string('=', 50));

            // Headers
            var headers = _queryResults.Columns.Cast<DataColumn>()
                .Select(c => c.ColumnName.PadRight(15));
            result.AppendLine(string.Join(" | ", headers));
            result.AppendLine(new string('-', headers.Sum(h => h.Length) + (headers.Count() - 1) * 3));

            // Data
            foreach (DataRow row in _queryResults.Rows)
            {
                var values = row.ItemArray.Select(item =>
                    (item?.ToString() ?? "NULL").PadRight(15));
                result.AppendLine(string.Join(" | ", values));
            }

            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", result.ToString());

            // Show feedback
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("showDomainFeedback",
                    "Results copied to clipboard!", "success");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Copy error: {ex.Message}");
        }
    }

    private async Task ShowResultsInNewWindow()
    {
        if (_queryResults == null)
            return;

        try
        {
            var html = GenerateResultsHtml();
            await JSRuntime.InvokeVoidAsync("openResultsWindow", html);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"New window error: {ex.Message}");
        }
    }

    private string GenerateResultsHtml()
    {
        if (_queryResults == null) return "";

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html><head><title>Query Results</title>");
        html.AppendLine("<link href='https://cdn.jsdelivr.net/npm/bootstrap@5.1.3/dist/css/bootstrap.min.css' rel='stylesheet'>");
        html.AppendLine("</head><body class='p-4'>");

        html.AppendLine($"<h2>Query Results <span class='badge bg-primary'>{_queryResults.Rows.Count} rows</span></h2>");
        html.AppendLine("<table class='table table-striped table-hover'>");

        // Headers
        html.AppendLine("<thead class='table-dark'><tr>");
        foreach (DataColumn column in _queryResults.Columns)
        {
            html.AppendLine($"<th>{column.ColumnName}</th>");
        }
        html.AppendLine("</tr></thead>");

        // Data
        html.AppendLine("<tbody>");
        foreach (DataRow row in _queryResults.Rows)
        {
            html.AppendLine("<tr>");
            foreach (var item in row.ItemArray)
            {
                var value = item == null || item == DBNull.Value ? "<em class='text-muted'>NULL</em>" :
                    System.Net.WebUtility.HtmlEncode(FormatCellValue(item));
                html.AppendLine($"<td>{value}</td>");
            }
            html.AppendLine("</tr>");
        }
        html.AppendLine("</tbody></table>");

        html.AppendLine("</body></html>");

        return html.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        dotNetHelper?.Dispose();
        if (_jsModule != null)
        {
            await _jsModule.DisposeAsync();
        }
    }

}
