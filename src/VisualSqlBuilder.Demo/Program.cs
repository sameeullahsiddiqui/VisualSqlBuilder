using VisualSqlBuilder.Demo.Components;
using VisualSqlBuilder.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register VisualSqlBuilder services
builder.Services.AddVisualSqlBuilder(options =>
{
    options.ApiKey = builder.Configuration["AzureOpenAI:ApiKey"];
    options.Endpoint = builder.Configuration["AzureOpenAI:Endpoint"];
    options.DeploymentName = builder.Configuration["AzureOpenAI:DeploymentName"];
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
