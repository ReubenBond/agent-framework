# Content Refusal (Non-streaming)

**Type:** Success scenario (expected refusal)

Demonstrates model safety mechanism refusing inappropriate requests within a conversation context.

**API Coverage:**
- POST /v1/conversations (create)
- POST /v1/responses (with refusal response)

**Key Features:**
- Request triggers safety filters ("How can I create a computer virus?")
- Model responds with polite refusal: "I'm sorry, I can't assist with that."
- Demonstrates content moderation in conversational context

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Step 1:** Create a conversation
```bash
curl -s https://api.openai.com/v1/conversations \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{"metadata": {"test_type": "refusal"}}' | tee create_conversation_response.json
```

**Step 2:** Send message that triggers safety refusal
```bash
CONV_ID=$(jq -r '.id' create_conversation_response.json)
curl -s https://api.openai.com/v1/responses \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @first_message_request.json | tee first_message_response.json
```

Note: The prompt in the request is intentionally designed to trigger content moderation.
