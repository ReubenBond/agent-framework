# Tool/Function Calling (Non-streaming)

**Type:** Success scenario

Demonstrates function calling integration within a conversation context, where the model invokes a tool to fulfill the user's request.

**API Coverage:**
- POST /v1/conversations (create)
- POST /v1/responses (with tools parameter and function calling)

**Key Features:**
- Tool definition with name, description, and JSON schema parameters
- Model generates function_call output with call_id, name, and arguments
- Arguments are returned as JSON string
- Response includes tool definitions in echo for verification

**Tool Schema Format:**
The Responses API uses a flattened tool structure:
```json
{
  "type": "function",
  "name": "get_weather",
  "description": "...",
  "parameters": { /* JSON Schema */ }
}
```

Note: Unlike Chat Completions API, there is no nested `function` object.

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Step 1:** Create a conversation
```bash
curl -s https://api.openai.com/v1/conversations \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{"metadata": {"test_type": "tool_call"}}' | tee create_conversation_request.json
```

**Step 2:** Send message with tools parameter
```bash
CONV_ID=$(jq -r '.id' create_conversation_response.json)
curl -s https://api.openai.com/v1/responses \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @first_message_request.json | tee first_message_response.json
```

Note: The request includes a `tools` array with function definitions. The model will decide whether to call the function based on the user's input.
