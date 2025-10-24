# Image Input (Non-streaming)

**Type:** Success scenario

Demonstrates multi-modal input with image URL processing in a conversation context.

**API Coverage:**
- POST /v1/conversations (create)
- POST /v1/responses (with conversation parameter, input_image content type)

**Key Features:**
- Image processing using image_url parameter
- Combined text and image input in content array
- High input token count due to image processing (36k+ tokens)

## How to Recreate

**Prerequisites:** Set your OpenAI API key in environment variable `OPENAI_API_KEY`

**Step 1:** Create a conversation
```bash
curl -s https://api.openai.com/v1/conversations \
  -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @create_conversation_request.json | tee create_conversation_response.json
```

**Step 2:** Send message with image input
```bash
CONV_ID=$(jq -r '.id' create_conversation_response.json)
curl -s https://api.openai.com/v1/responses \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d @first_message_request.json | tee first_message_response.json
```

Note: The request uses `input_image` content type with `image_url` parameter pointing to a publicly accessible image.
