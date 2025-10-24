# Error: Delete Already Deleted Conversation

**Type:** Error scenario (404)

Demonstrates the error response when attempting to delete a conversation that has already been deleted.

**API Coverage:**
- DELETE /v1/conversations/{conversation_id} (on already deleted conversation)

**Error Details:**
- Type: `invalid_request_error`
- Message: "Conversation with id 'conv_...' not found."
- HTTP Status: 404 (implied)

**Use Case:**
- Validates idempotency expectations for DELETE operations
- In this API, DELETE is NOT idempotent - returns error on second call
- Clients should track deletion state to avoid unnecessary API calls

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Step 1:** Create and delete a conversation
```bash
# Create a conversation
curl -s https://api.openai.com/v1/conversations \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{"metadata": {"test": "delete_test"}}' | tee create_response.json

# Extract ID and delete it
CONV_ID=$(jq -r '.id' create_response.json)
curl -s https://api.openai.com/v1/conversations/$CONV_ID \
  -X DELETE \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY"
```

**Step 2:** Attempt to delete the same conversation again
```bash
curl -s https://api.openai.com/v1/conversations/$CONV_ID \
  -X DELETE \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" | tee response.json
```

The second DELETE will return a 404 error because the conversation no longer exists.
