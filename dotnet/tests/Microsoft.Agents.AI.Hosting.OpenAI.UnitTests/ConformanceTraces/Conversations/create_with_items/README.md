# Create Conversation with Initial Items

**Type:** Success scenario

Demonstrates creating a conversation with pre-populated items in a single API call, avoiding the need for separate item addition.

**API Coverage:**
- POST /v1/conversations (with items array in request body)

**Key Features:**
- Items array contains initial conversation history
- Useful for restoring or importing conversation state

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

```bash
curl -s https://api.openai.com/v1/conversations \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @create_request.json | tee create_response.json
```

The request includes an `items` array with initial conversation messages that will be stored immediately upon conversation creation.
