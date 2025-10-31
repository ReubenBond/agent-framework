// Copyright (c) Microsoft. All rights reserved.

using AgentWebChat.AgentHost.DurableAgents.Utilities;
using AgentContracts;
using AgentWebChat.AgentHost;
using AgentWebChat.AgentHost.Options;
using AgentWebChat.AgentHost.Utilities;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Add a singleton capturing this worker process metadata (instance id + host id)
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<WorkerOptions>>().Value;
    string hostId = options.HostId ?? Environment.MachineName;
    return new WorkerProcessMetadata { InstanceId = Guid.NewGuid(), HostId = hostId };
});

bool enableWorkerRegistration = builder.Configuration.GetValue<bool>("AgentRuntime:RegisterWorker");
if (enableWorkerRegistration)
{
    // Register worker registration background service
    builder.Services.AddHostedService<WorkerRegistrationService>();
}

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.Services.AddOpenApi();

// Configure chat message store using Conversations API via AgentGateway
// The gateway base address is provided by Aspire's service discovery
var gatewayBaseAddress = builder.Configuration["Worker:GatewayBaseAddress"];
if (!string.IsNullOrWhiteSpace(gatewayBaseAddress))
{
    builder.Services.AddHttpClient<ConversationsApiClient>(client => client.BaseAddress = new Uri(gatewayBaseAddress));
    builder.Services.AddConversationsChatMessageStore();
}

// Add services to the container.
builder.Services.AddProblemDetails();

// Configure the chat model and our agent.
builder.AddKeyedChatClient("chat-model").UseDurableFunctionInvocation();
builder.AddAIAgent("config-rollout", (sp, key) =>
{
    var chatClient = sp.GetRequiredKeyedService<IChatClient>("chat-model");
    var messageStoreFactory = sp.GetRequiredService<Func<string, ChatMessageStore>>();
    return new ConfigRolloutAgent(
        chatClient,
        sp.GetRequiredService<ILogger<ConfigRolloutAgent>>(),
        messageStoreFactory);
});

builder.AddAIAgent("pirate", instructions: "Speak like a pirate in all responses.", chatClientServiceKey: "chat-model");

builder.AddOpenAIResponses();

var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Agents API"));

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

app.MapOpenAIResponses();

// Map the agents HTTP endpoints
app.MapAgentDiscovery("/agents");

// Worker meta endpoint used by gateway to uniquely identify this process
app.MapGet("/worker/meta", (WorkerProcessMetadata meta) => Results.Ok(meta));

app.MapDefaultEndpoints();
app.Run();
