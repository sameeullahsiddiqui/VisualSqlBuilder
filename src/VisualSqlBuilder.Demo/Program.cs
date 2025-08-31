using VisualSqlBuilder.Core;

var builder = WebApplication.CreateBuilder(args);

// Enhanced logging for debugging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(30);
    options.DisconnectedCircuitMaxRetained = 100;
    options.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds(60);
    options.MaxBufferedUnacknowledgedRenderBatches = 10;
}).AddHubOptions(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 32 * 1024; // 32KB
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

// Add SignalR logging
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

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
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}


app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Add detailed logging middleware
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogDebug($"Request: {context.Request.Method} {context.Request.Path}");
    await next();
    logger.LogDebug($"Response: {context.Response.StatusCode}");
});

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.MapRazorPages();

// Add test endpoint
app.MapGet("/test", () => "Server is running");

app.Logger.LogInformation("Application starting on URLs: {urls}", string.Join(", ", app.Urls));

app.Run();


//// Add services to the container.
//builder.Services.AddRazorComponents()
//    .AddInteractiveServerComponents();


//builder.Services.AddSignalR(options =>
//{
//    options.EnableDetailedErrors = true;
//});

//builder.Logging.AddConsole();
//builder.Logging.SetMinimumLevel(LogLevel.Debug);

//var app = builder.Build();

//// Configure the HTTP request pipeline.
//if (!app.Environment.IsDevelopment())
//{
//    app.UseExceptionHandler("/Error", createScopeForErrors: true);
//    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
//    app.UseHsts();
//}
//else
//{
//    app.UseDeveloperExceptionPage();
//}

//app.UseHttpsRedirection();
//app.UseStaticFiles();
//app.UseRouting();
//app.UseAntiforgery();

//app.MapRazorComponents<App>()
//   .AddInteractiveServerRenderMode();

//app.Run();
