# Tool/Function Calling (Streaming)

**Type:** Success scenario

Demonstrates streaming SSE format for function calling, showing incremental delivery of function arguments.

**API Coverage:**
- POST /v1/conversations (create)
- POST /v1/responses (with tools parameter, streaming mode)

**Key Features:**
- SSE event types specific to function calling:
  - `response.output_item.added` - Function call starts
  - `response.function_call_arguments.delta` - Incremental argument chunks
  - `response.function_call_arguments.done` - Complete arguments
  - `response.output_item.done` - Function call completed
- Each delta includes sequence_number for ordering
- Obfuscation field present in delta events (purpose unclear)
- Final arguments assembled from deltas

**Use Case:**
- Real-time function call argument streaming
- Progressive UI updates during tool invocation
- Enables early validation of function arguments before completion

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Step 1:** Create a conversation
```bash
curl -s https://api.openai.com/v1/conversations \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{"metadata": {"test_type": "tool_call_streaming"}}' | tee create_conversation_response.json
```

**Step 2:** Send streaming message with tools
```bash
CONV_ID=$(jq -r '.id' create_conversation_response.json)
curl -s --no-buffer https://api.openai.com/v1/responses \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @first_message_request.json | tee first_message_response.txt
```

Note: Request must include `"stream": true` and a `tools` array with function definitions.
