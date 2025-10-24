# Basic Conversation (Non-streaming)

**Type:** Success scenario

Demonstrates a basic multi-turn conversation workflow:
1. Create a new conversation with metadata
2. Send first message using Responses API with conversation parameter
3. Send follow-up message that relies on conversation state (contextual "What is its population?" referring to previously mentioned Paris)

**API Coverage:**
- POST /v1/conversations (create)
- POST /v1/responses (with conversation parameter, non-streaming)

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

**Step 2:** Extract conversation ID and send first message
```bash
CONV_ID=$(jq -r '.id' create_conversation_response.json)
curl -s https://api.openai.com/v1/responses \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @first_message_request.json | tee first_message_response.json
```

**Step 3:** Send follow-up message using same conversation ID
```bash
curl -s https://api.openai.com/v1/responses \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @second_message_request.json | tee second_message_response.json
```

Note: Update the `conversation` field in request files with your actual conversation ID.
