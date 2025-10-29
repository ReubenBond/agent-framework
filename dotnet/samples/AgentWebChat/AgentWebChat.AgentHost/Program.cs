// Copyright (c) Microsoft. All rights reserved.

using AgentWebChat.AgentHost.DurableAgents;
using AgentWebChat.AgentHost.DurableAgents.Utilities;
//using AgentContracts;
using AgentWebChat.AgentHost;
//using AgentWebChat.AgentHost.Options;
using AgentWebChat.AgentHost.Utilities;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
//using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.Services.AddOpenApi();

// Add services to the container.
builder.Services.AddProblemDetails();

// Configure the chat model and our agent.
builder.AddKeyedChatClient("chat-model").UseDurableFunctionInvocation();

builder.AddAIAgent("config-rollout", (sp, key) =>
{
    var chatClient = sp.GetRequiredKeyedService<IChatClient>("chat-model");
    return new ConfigRolloutAgent(chatClient, sp.GetRequiredService<ILogger<ConfigRolloutAgent>>());
});

builder.AddOpenAIResponses();

var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Agents API"));

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

app.MapOpenAIResponses();

// Map the agents HTTP endpoints
app.MapAgentDiscovery("/agents");

app.MapDefaultEndpoints();
app.Run();
