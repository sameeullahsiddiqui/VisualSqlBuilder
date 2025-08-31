using Microsoft.Extensions.DependencyInjection;
using VisualSqlBuilder.Core.Services;

namespace VisualSqlBuilder.Core
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddVisualSqlBuilder(this IServiceCollection services, Action<AzureOpenAIOptions> configureOpenAI)
        {
            // Register all core services here
            services.Configure(configureOpenAI);

            services.AddScoped<IAzureOpenAIService, AzureOpenAIService>();
            services.AddScoped<ILayoutStorageService, LayoutStorageService>();
            services.AddScoped<ISchemaService, SchemaService>();
            services.AddScoped<ISqlGeneratorService, SqlGeneratorService>();
            services.AddScoped<IValidationService, ValidationService>();

            return services;
        }
    }
}
