// Copyright (c) Microsoft. All rights reserved.

using AgentContracts;
using AgentGateway;
using AgentGateway.Conversations;
using AgentGateway.Entities;
using AgentGateway.Health;
using AgentGateway.Responses;
using AgentGateway.Threads;
using AgentGateway.Utilities;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Extensions.AI;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedAzureBlobServiceClient("state");
builder.AddKeyedAzureTableServiceClient("reminders");
builder.AddKeyedAzureTableServiceClient("clustering");

builder.AddServiceDefaults();

// Add custom health check for Agent Gateway
builder.Services.AddHealthChecks()
    .AddCheck<AgentGatewayHealthCheck>("agent_gateway");
builder.Services.AddHttpForwarder();
builder.Services.AddSingleton<ForwardingHttpClientProvider>();

// Configure Orleans
builder.Host.UseOrleans(siloBuilder =>
{
    // Configure System.Text.Json serialization for all Microsoft.Agents.* and AgentGateway types
    // This uses AgentGatewayJsonUtilities which chains together all the necessary type resolvers
    // including OpenAIJsonUtilities (OpenAI Hosting types), AIJsonUtilities (Microsoft.Extensions.AI), and grain states
    siloBuilder.Services.AddSerializer(serializerBuilder =>
    {
        serializerBuilder.AddJsonSerializer(
            isSupported: type => type.Namespace?.StartsWith("Microsoft.Agents", StringComparison.Ordinal) == true ||
                                type.Namespace?.StartsWith("AgentContracts", StringComparison.Ordinal) == true ||
                                type.Namespace?.StartsWith("AgentGateway", StringComparison.Ordinal) == true,
            jsonSerializerOptions: AgentGatewayJsonUtilities.DefaultOptions);
    });

    // Register System.Text.Json-based grain storage serializer
    siloBuilder.Services.AddSingleton<IGrainStorageSerializer>(sp =>
        new SystemTextJsonGrainStorageSerializer(AgentGatewayJsonUtilities.DefaultOptions));
    siloBuilder.Configure<ClusterMembershipOptions>(o =>
    {
        o.NumMissedTableIAmAliveLimit = 2;
        o.IAmAliveTablePublishTimeout = TimeSpan.FromSeconds(10);
    });
});

// Register conversation storage - choose between in-memory and Orleans-backed
// For production use, uncomment the Orleans implementation:
builder.Services.AddSingleton<IConversationStorage, OrleansConversationStorage>();
builder.Services.AddSingleton<IAgentConversationIndex, OrleansAgentConversationIndex>();

// Register IChatClient for responses service
// Configure via connection strings (supports OpenAI, Azure OpenAI, Ollama, Azure AI Inference)
builder.AddChatClient("chat-model");

// Register responses service
builder.Services.AddSingleton<ResponsesService>();

// Configure JSON serialization to use snake_case naming (web conventions)
// Uses source-generated JSON serializer context for better performance and trimming support
// Chains with AgentContractsJsonUtilities for worker registration and conversation types
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Start with AgentContracts options which includes WorkerHostJsonContext and AIJsonUtilities
    var contractsOptions = AgentContractsJsonUtilities.DefaultOptions;
    options.SerializerOptions.TypeInfoResolverChain.Add(contractsOptions.TypeInfoResolver!);

    // Add Gateway-specific context at the front for priority
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AgentGatewayJsonContext.Default);
});

builder.Services.AddOpenApi();

// Registry + background processing for worker heartbeat & health checks
builder.Services.AddHttpClient();
builder.Services.AddSingleton(sp =>
{
    var clientProvider = sp.GetRequiredService<ForwardingHttpClientProvider>();
    var logger = sp.GetRequiredService<ILogger<WorkerDiscoveryCache>>();
    return new WorkerDiscoveryCache(clientProvider.HttpClient, logger);
});
builder.Services.AddSingleton(sp => new WorkerRegistry(sp.GetRequiredService<WorkerDiscoveryCache>()));
builder.Services.AddHostedService<WorkerHealthCheckService>();
builder.Services.AddSingleton<WorkerHttpForwarder>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Enable static files middleware - required for DevUI assets
// DevUI assets are automatically available at /_content/Microsoft.Agents.AI.DevUI/
app.UseStaticFiles();

// Map Entities API endpoints (DevUI-compatible)
app.MapEntitiesApi();

// Map Threads API endpoints (DevUI-compatible)
app.MapThreadsApi();

app.MapWorkerManagement();
app.MapDiscovery();

// Map forwarding endpoints to workers (eg, A2A) via IHttpForwarder
app.MapA2AForwarder();

// Map Conversations API endpoints
app.MapConversations();

// Map Responses API endpoints
app.MapResponses();

// Map DevUI
app.MapDevUI();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Redirect root to DevUI
app.MapGet("/", () => Results.Redirect("/devui"));

app.Run();
