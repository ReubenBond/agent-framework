# List Conversation Items

**Type:** Success scenario

Demonstrates paginated retrieval of conversation items (messages) with filtering options.

**API Coverage:**
- GET /v1/conversations/{conversation_id}/items (with query parameters: limit, order)

**Key Features:**
- Pagination with limit parameter
- Ordering control (asc/desc)
- Returns list object with data array, first_id, last_id, has_more
- Each item includes id, type, status, content, and role
- Shows full conversation history with both user and assistant messages

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Note:** You must have an existing conversation with items to list.

```bash
# Replace CONV_ID with your actual conversation ID
CONV_ID="conv_68fb96fe1a488195bf48df8f7666551604cbf45151194822"

curl -s "https://api.openai.com/v1/conversations/$CONV_ID/items?limit=10&order=asc" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" | tee response.json
```

**Query Parameters:**
- `limit`: Maximum number of items to return (1-100, default 20)
- `order`: Sort order - `asc` or `desc` (default `asc`)
- `after`: Pagination cursor for items after this ID
- `before`: Pagination cursor for items before this ID
