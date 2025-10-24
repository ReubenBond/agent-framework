# Error: Invalid JSON

**Type:** Error scenario (400)

Demonstrates the error response when sending malformed JSON in the request body.

**API Coverage:**
- POST /v1/conversations (with malformed JSON)

**Error Details:**
- Type: `invalid_request_error`
- Code: `invalid_json`
- Message: "Invalid body: failed to parse JSON value. Please check the value to ensure it is valid JSON. (Common errors include trailing commas, missing closing brackets, missing quotation marks, etc.)"
- HTTP Status: 400 (implied)

**Use Case:**
- Validates proper error handling for client-side JSON serialization issues
- Helpful for debugging request formation problems

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

```bash
# Send malformed JSON (missing closing brace, has invalid syntax)
curl -s https://api.openai.com/v1/conversations \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{"metadata": {"test": "value"} invalid}' | tee response.json
```

The malformed JSON will trigger a parsing error with code `invalid_json`.
