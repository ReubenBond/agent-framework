# Add Items to Conversation

**Type:** Success scenario

Demonstrates adding multiple items (messages) to an existing conversation in a single API call.

**API Coverage:**
- POST /v1/conversations/{conversation_id}/items

**Key Features:**
- Bulk item addition with items array
- Each item has type, role, and content
- Response returns list of added items with generated IDs
- Useful for importing conversation history or batch operations

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Note:** You must have an existing conversation ID to add items to.

```bash
# Replace CONV_ID with your actual conversation ID
CONV_ID="conv_68fb96fe1a488195bf48df8f7666551604cbf45151194822"

curl -s https://api.openai.com/v1/conversations/$CONV_ID/items \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @request.json | tee response.json
```

The request body contains an `items` array where each item must have:
- `type`: "message"
- `role`: "user", "assistant", "system", or "developer"
- `content`: Array of content objects with type and text/data
