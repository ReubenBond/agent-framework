# Basic Conversation (Streaming)

**Type:** Success scenario

Demonstrates streaming response format with Server-Sent Events (SSE) in a conversation:
1. Create a new conversation
2. Send message and receive streaming SSE response with delta events

**API Coverage:**
- POST /v1/conversations (create)
- POST /v1/responses (with conversation parameter, streaming mode)

**Key Features:**
- SSE format with event types: response.created, response.output_text.delta, response.done
- Sequence numbers for ordering

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Step 1:** Create a conversation
```bash
curl -s https://api.openai.com/v1/conversations \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @create_conversation_request.json | tee create_conversation_response.json
```

**Step 2:** Send streaming message (note: --no-buffer flag for real-time streaming)
```bash
CONV_ID=$(jq -r '.id' create_conversation_response.json)
curl -s --no-buffer https://api.openai.com/v1/responses \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @first_message_request.json | tee first_message_response.txt
```

Note: Update the `conversation` field in first_message_request.json with your actual conversation ID.
