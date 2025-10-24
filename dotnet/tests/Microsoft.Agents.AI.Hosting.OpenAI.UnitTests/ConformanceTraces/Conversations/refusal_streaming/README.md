# Content Refusal (Streaming)

**Type:** Success scenario (expected refusal)

Demonstrates streaming SSE format for content refusal in a conversation.

**API Coverage:**
- POST /v1/conversations (create)
- POST /v1/responses (with refusal response, streaming)

**Key Features:**
- SSE format for refusal messages
- Safety mechanism demonstration in streaming mode

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Step 1:** Create a conversation
```bash
curl -s https://api.openai.com/v1/conversations \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{"metadata": {"test_type": "refusal_streaming"}}' | tee create_conversation_response.json
```

**Step 2:** Send streaming message that triggers refusal
```bash
CONV_ID=$(jq -r '.id' create_conversation_response.json)
curl -s --no-buffer https://api.openai.com/v1/responses \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @first_message_request.json | tee first_message_response.txt
```

Note: Request must include `"stream": true` and use a prompt that triggers safety filters.
