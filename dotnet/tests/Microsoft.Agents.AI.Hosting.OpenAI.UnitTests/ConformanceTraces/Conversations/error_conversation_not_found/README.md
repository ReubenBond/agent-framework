# Error: Conversation Not Found

**Type:** Error scenario (404)

Demonstrates the error response when attempting to retrieve a non-existent conversation.

**API Coverage:**
- GET /v1/conversations/{conversation_id} (with invalid ID)

**Error Details:**
- Type: `invalid_request_error`
- Message: "Conversation with id 'conv_nonexistent123' not found."
- HTTP Status: 404 (implied)

**Use Case:**
- Validates proper error handling for deleted or never-created conversations
- Client should handle gracefully and not retry

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

```bash
# Use a non-existent conversation ID
curl -s https://api.openai.com/v1/conversations/conv_nonexistent123 \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" | tee response.json
```

This will return a 404 error response with the conversation not found message.
