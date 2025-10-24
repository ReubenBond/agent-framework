# Error: Item Not Found

**Type:** Error scenario (404)

Demonstrates the error response when attempting to retrieve a non-existent item from a conversation.

**API Coverage:**
- GET /v1/conversations/{conversation_id}/items/{item_id} (with invalid item_id)

**Error Details:**
- Type: `invalid_request_error`
- Message: "Item with id 'msg_nonexistent123' not found in conversation."
- HTTP Status: 404 (implied)

**Use Case:**
- Validates proper error handling for deleted or never-created items
- Important for synchronization scenarios where items may be deleted by other clients

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

```bash
# Use a valid conversation ID but non-existent item ID
CONV_ID="conv_68fb96fe1a488195bf48df8f7666551604cbf45151194822"

curl -s https://api.openai.com/v1/conversations/$CONV_ID/items/msg_nonexistent123 \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" | tee response.json
```

This will return a 404 error response indicating the item was not found in the conversation.
