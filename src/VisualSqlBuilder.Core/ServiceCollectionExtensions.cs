using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisualSqlBuilder.Core.Services;

namespace VisualSqlBuilder.Core
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddVisualSqlBuilder(this IServiceCollection services, Action<AzureOpenAIOptions> configureOpenAI)
        {
            services.Configure(configureOpenAI);

            var options = new AzureOpenAIOptions();
            configureOpenAI(options);

            if (!string.IsNullOrEmpty(options.Endpoint) && !string.IsNullOrEmpty(options.ApiKey))
            {
                services.AddScoped<IAzureOpenAIService, AzureOpenAIService>();
            }
            else
            {
                services.AddScoped<IAzureOpenAIService, NullAzureOpenAIService>();
            }

            // Register other core services
            services.AddScoped<ILayoutStorageService, LayoutStorageService>();
            services.AddScoped<ISchemaService, SchemaService>();
            services.AddScoped<ISqlGeneratorService, SqlGeneratorService>();
            services.AddScoped<IValidationService, ValidationService>();

            return services;
        }
    }
}
