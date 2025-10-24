# Retrieve Conversation

**Type:** Success scenario

Demonstrates fetching an existing conversation by ID to inspect its state and metadata.

**API Coverage:**
- GET /v1/conversations/{conversation_id}

**Key Features:**
- Returns conversation object with id, created_at, and metadata
- Useful for verifying conversation state

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Note:** You must have an existing conversation ID to retrieve. Create one first if needed.

```bash
# Replace CONV_ID with your actual conversation ID
CONV_ID="conv_68fb96fe1a488195bf48df8f7666551604cbf45151194822"

curl -s https://api.openai.com/v1/conversations/$CONV_ID \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" | tee response.json
```
