using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using System.Text;
using VisualSqlBuilder.Core.Models;

namespace VisualSqlBuilder.Core.Services;

public interface IAzureOpenAIService
{
    Task<List<RelationshipModel>> SuggestRelationshipsAsync(List<TableModel> tables);
    Task<string> SuggestQueryAsync(List<TableModel> tables, string userIntent);
}

public class AzureOpenAIOptions
{
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string DeploymentName { get; set; } = "";
}


public class AzureOpenAIService : IAzureOpenAIService
{
    private readonly AzureOpenAIOptions _options;
    private readonly OpenAIClient _client;

    
    public AzureOpenAIService(IOptions<AzureOpenAIOptions> options)
    {
        _options = options.Value;

        if(!string.IsNullOrEmpty(_options.Endpoint))
            _client = new OpenAIClient(new Uri(_options.Endpoint), new AzureKeyCredential(_options.ApiKey));
    }

    public async Task<List<RelationshipModel>> SuggestRelationshipsAsync(List<TableModel> tables)
    {
        if(_options == null || _client == null)
            throw new InvalidOperationException("Azure OpenAI options are not configured properly.");

        var relationships = new List<RelationshipModel>();

        var prompt = BuildRelationshipPrompt(tables);

        var completionsOptions = new ChatCompletionsOptions
        {
            DeploymentName = _options.DeploymentName,
            Messages =
            {
                new ChatRequestSystemMessage("You are a database expert. Analyze the tables and suggest foreign key relationships."),
                new ChatRequestUserMessage(prompt)
            },
            Temperature = 0.3f,
            MaxTokens = 1000
        };

        try
        {
            var response = await _client.GetChatCompletionsAsync(completionsOptions);
            var suggestion = response.Value.Choices[0].Message.Content;

            // Parse the AI response and create relationships
            relationships = ParseRelationshipSuggestions(suggestion, tables);
        }
        catch (Exception ex)
        {
            // Log error
            Console.WriteLine($"Azure OpenAI error: {ex.Message}");
        }

        return relationships;
    }

    public async Task<string> SuggestQueryAsync(List<TableModel> tables, string userIntent)
    {
        if (_options == null || _client == null)
            throw new InvalidOperationException("Azure OpenAI options are not configured properly.");

        var prompt = $"Given these tables:\n{BuildTableSchema(tables)}\n\nGenerate a SQL query for: {userIntent}";

        var completionsOptions = new ChatCompletionsOptions
        {
            DeploymentName = _options.DeploymentName,
            Messages =
            {
                new ChatRequestSystemMessage("You are a SQL expert. Generate T-SQL queries based on user requirements."),
                new ChatRequestUserMessage(prompt)
            },
            Temperature = 0.3f,
            MaxTokens = 500
        };

        try
        {
            var response = await _client.GetChatCompletionsAsync(completionsOptions);
            return response.Value.Choices[0].Message.Content;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Azure OpenAI error: {ex.Message}");
            return string.Empty;
        }
    }

    private string BuildRelationshipPrompt(List<TableModel> tables)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analyze these Excel sheets and suggest relationships:");

        foreach (var table in tables)
        {
            sb.AppendLine($"\nTable: {table.Name}");
            sb.AppendLine("Columns:");
            foreach (var col in table.Columns)
            {
                sb.AppendLine($"  - {col.Name} ({col.DataType})");
            }
        }

        sb.AppendLine("\nSuggest foreign key relationships in format: TableA.ColumnA -> TableB.ColumnB");
        return sb.ToString();
    }

    private string BuildTableSchema(List<TableModel> tables)
    {
        var sb = new StringBuilder();
        foreach (var table in tables)
        {
            sb.AppendLine($"Table: {table.Name}");
            foreach (var col in table.Columns)
            {
                sb.AppendLine($"  {col.Name} ({col.DataType})");
            }
        }
        return sb.ToString();
    }

    private List<RelationshipModel> ParseRelationshipSuggestions(string suggestion, List<TableModel> tables)
    {
        var relationships = new List<RelationshipModel>();
        var lines = suggestion.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Contains("->"))
            {
                var parts = line.Split("->");
                if (parts.Length == 2)
                {
                    var source = parts[0].Trim().Split('.');
                    var target = parts[1].Trim().Split('.');

                    if (source.Length == 2 && target.Length == 2)
                    {
                        var sourceTable = tables.FirstOrDefault(t => t.Name.Equals(source[0], StringComparison.OrdinalIgnoreCase));
                        var targetTable = tables.FirstOrDefault(t => t.Name.Equals(target[0], StringComparison.OrdinalIgnoreCase));

                        if (sourceTable != null && targetTable != null)
                        {
                            var sourceCol = sourceTable.Columns.FirstOrDefault(c => c.Name.Equals(source[1], StringComparison.OrdinalIgnoreCase));
                            var targetCol = targetTable.Columns.FirstOrDefault(c => c.Name.Equals(target[1], StringComparison.OrdinalIgnoreCase));

                            if (sourceCol != null && targetCol != null)
                            {
                                relationships.Add(new RelationshipModel
                                {
                                    SourceTableId = sourceTable.Id,
                                    SourceColumnId = sourceCol.Id,
                                    TargetTableId = targetTable.Id,
                                    TargetColumnId = targetCol.Id,
                                    JoinType = JoinType.InnerJoin,
                                    Type = RelationshipType.Foreign
                                });
                            }
                        }
                    }
                }
            }
        }

        return relationships;
    }
}