# Delete Conversation Item

**Type:** Success scenario

Demonstrates removing a specific item (message) from a conversation.

**API Coverage:**
- DELETE /v1/conversations/{conversation_id}/items/{item_id}

**Key Features:**
- Removes individual messages from conversation history
- Response confirms deletion with deleted: true and object: "conversation.item.deleted"
- Useful for removing unwanted or erroneous messages

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Note:** You must have an existing conversation ID and item ID to delete.

```bash
# Replace with your actual IDs
CONV_ID="conv_68fb96fe1a488195bf48df8f7666551604cbf45151194822"
ITEM_ID="msg_68fb9abf14a08195b16bb05eab82cf9d04cbf45151194822"

curl -s https://api.openai.com/v1/conversations/$CONV_ID/items/$ITEM_ID \
  -X DELETE \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" | tee response.json
```

**Warning:** This operation is permanent. The deleted item cannot be recovered.
