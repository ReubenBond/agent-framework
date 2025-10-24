# Update Conversation Metadata

**Type:** Success scenario

Demonstrates updating conversation metadata without affecting conversation items.

**API Coverage:**
- POST /v1/conversations/{conversation_id} (update)

**Key Features:**
- Partial update of metadata fields
- Preserves existing metadata while adding/updating specific fields
- Response shows updated metadata merged with existing fields

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Note:** You must have an existing conversation ID to update.

```bash
# Replace CONV_ID with your actual conversation ID
CONV_ID="conv_68fb96fe1a488195bf48df8f7666551604cbf45151194822"

curl -s https://api.openai.com/v1/conversations/$CONV_ID \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @request.json | tee response.json
```

The request body should contain the metadata fields you want to add or update.
