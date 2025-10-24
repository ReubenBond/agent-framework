# Error: Missing Required Field

**Type:** Error scenario (400)

Demonstrates the error response when a required field (role) is missing from an item.

**API Coverage:**
- POST /v1/conversations/{conversation_id}/items (with incomplete item object)

**Error Details:**
- Type: `invalid_request_error`
- Code: `invalid_value`
- Param: `items[0]`
- Message: "Invalid value: ''. Supported values are: 'assistant', 'system', 'developer', and 'user'."
- HTTP Status: 400 (implied)

**Use Case:**
- Validates required field enforcement
- Shows supported role values: 'assistant', 'system', 'developer', 'user'
- Helps clients understand required message structure

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

```bash
# Use a valid conversation ID but send item without required role field
CONV_ID="conv_68fb96fe1a488195bf48df8f7666551604cbf45151194822"

curl -s https://api.openai.com/v1/conversations/$CONV_ID/items \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @request.json | tee response.json
```

The request.json file contains an item missing the required `role` field, which triggers a validation error listing the supported role values.
