# Delete Conversation

**Type:** Success scenario

Demonstrates deleting an entire conversation and all its associated items.

**API Coverage:**
- DELETE /v1/conversations/{conversation_id}

**Key Features:**
- Removes conversation and all items permanently
- Response confirms deletion with deleted: true and object: "conversation.deleted"
- Returns conversation ID for verification

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Note:** You must have an existing conversation ID to delete.

```bash
# Replace CONV_ID with your actual conversation ID
CONV_ID="conv_68fb9837f9588193ac3da6bd57b636a50cdad19d14602ec8"

curl -s https://api.openai.com/v1/conversations/$CONV_ID \
  -X DELETE \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" | tee response.json
```

**Warning:** This operation is permanent. The conversation and all its items cannot be recovered. Subsequent DELETE requests on the same ID will return a 404 error.
