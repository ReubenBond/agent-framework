# Error: Invalid Limit Parameter

**Type:** Error scenario (400)

Demonstrates the error response when providing an out-of-range limit parameter.

**API Coverage:**
- GET /v1/conversations/{conversation_id}/items?limit=1000

**Error Details:**
- Type: `invalid_request_error`
- Code: `integer_above_max_value`
- Param: `limit`
- Message: "Invalid 'limit': integer above maximum value. Expected a value <= 100, but got 1000 instead."
- HTTP Status: 400 (implied)

**Use Case:**
- Validates parameter validation and constraints
- Maximum limit is 100 per API specification
- Clients should enforce limits or handle this error gracefully

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

```bash
# Use a valid conversation ID but invalid limit parameter
CONV_ID="conv_68fb96fe1a488195bf48df8f7666551604cbf45151194822"

curl -s "https://api.openai.com/v1/conversations/$CONV_ID/items?limit=1000" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" | tee response.json
```

The limit value exceeds the maximum allowed (100), triggering a validation error.
