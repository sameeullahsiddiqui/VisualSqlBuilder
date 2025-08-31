using VisualSqlBuilder.Core.Models;

namespace VisualSqlBuilder.Core.Services;

public class NullAzureOpenAIService : IAzureOpenAIService
{
    public Task<List<RelationshipModel>> SuggestRelationshipsAsync(List<TableModel> tables)
    {
        return Task.FromResult(new List<RelationshipModel>());
    }

    public Task<string> SuggestQueryAsync(List<TableModel> tables, string userIntent)
    {
        return Task.FromResult(string.Empty);
    }
}