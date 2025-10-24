# Image Input (Streaming)

**Type:** Success scenario

Demonstrates streaming SSE format for multi-modal image input in a conversation.

**API Coverage:**
- POST /v1/conversations (create)
- POST /v1/responses (with conversation parameter, input_image content type, streaming)

**Key Features:**
- SSE streaming with image input
- Delta events for incremental response text

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

**Step 2:** Send streaming message with image
```bash
CONV_ID=$(jq -r '.id' create_conversation_response.json)
curl -s --no-buffer https://api.openai.com/v1/responses \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @first_message_request.json | tee first_message_response.txt
```

Note: Ensure request has `"stream": true` and includes image_url in content array.
