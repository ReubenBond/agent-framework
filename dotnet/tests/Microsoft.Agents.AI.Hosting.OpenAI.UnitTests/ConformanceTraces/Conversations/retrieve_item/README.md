# Retrieve Single Conversation Item

**Type:** Success scenario

Demonstrates fetching a specific conversation item (message) by its ID.

**API Coverage:**
- GET /v1/conversations/{conversation_id}/items/{item_id}

**Key Features:**
- Direct access to individual messages by ID
- Returns full item object with all content and metadata
- Useful for inspecting specific messages without fetching entire conversation

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Note:** You must have an existing conversation ID and item ID.

```bash
# Replace with your actual IDs
CONV_ID="conv_68fb96fe1a488195bf48df8f7666551604cbf45151194822"
ITEM_ID="msg_04cbf451511948220068fb976a9fc481959fecc62ac9644e8d"

curl -s https://api.openai.com/v1/conversations/$CONV_ID/items/$ITEM_ID \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" | tee response.json
```

You can get item IDs by first listing items in a conversation.
