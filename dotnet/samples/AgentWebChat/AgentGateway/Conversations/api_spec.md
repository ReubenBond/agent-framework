## OpenAI Conversations API Specification

The following is a clean markdown representation of the provided API specification for managing conversations and conversation items.

---

## Conversations

Conversations allow you to store and retrieve conversation state across Response API calls.

### Create a conversation

**POST** `https://api.openai.com/v1/conversations`

| Parameter  | Type           | Required | Description                                                                                         |
| :--------- | :------------- | :------- | :-------------------------------------------------------------------------------------------------- |
| `items`    | array          | Optional | Initial items to include in the conversation context. You may add up to 20 items at a time.         |
| `metadata` | object or null | Optional | Set of 16 key-value pairs (max 64 chars for key, 512 for value) for storing additional information. |

**Returns:** A **Conversation object**.

**Example Request (cURL):**

```bash
curl https://api.openai.com/v1/conversations \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{
    "metadata": {"topic": "demo"},
    "items": [
      {
        "type": "message",
        "role": "user",
        "content": "Hello!"
      }
    ]
  }'
```

**Example Response:**

```json
{
  "id": "conv_123",
  "object": "conversation",
  "created_at": 1741900000,
  "metadata": { "topic": "demo" }
}
```

---

### Retrieve a conversation

**GET** `https://api.openai.com/v1/conversations/{conversation_id}`

| Path Parameter    | Type   | Required     | Description                             |
| :---------------- | :----- | :----------- | :-------------------------------------- |
| `conversation_id` | string | **Required** | The ID of the conversation to retrieve. |

**Returns:** A **Conversation object**.

**Example Request (cURL):**

```bash
curl https://api.openai.com/v1/conversations/conv_123 \
  -H "Authorization: Bearer $OPENAI_API_KEY"
```

---

### Update a conversation

**POST** `https://api.openai.com/v1/conversations/{conversation_id}`

| Path Parameter    | Type   | Required     | Description                           |
| :---------------- | :----- | :----------- | :------------------------------------ |
| `conversation_id` | string | **Required** | The ID of the conversation to update. |

| Request Body Parameter | Type | Required     | Description                                                                                         |
| :--------------------- | :--- | :----------- | :-------------------------------------------------------------------------------------------------- |
| `metadata`             | map  | **Required** | Set of 16 key-value pairs (max 64 chars for key, 512 for value) for storing additional information. |

**Returns:** The updated **Conversation object**.

**Example Request (cURL):**

```bash
curl https://api.openai.com/v1/conversations/conv_123 \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{
    "metadata": {"topic": "project-x"}
  }'
```

**Example Response:**

```json
{
  "id": "conv_123",
  "object": "conversation",
  "created_at": 1741900000,
  "metadata": { "topic": "project-x" }
}
```

---

### Delete a conversation

**DELETE** `https://api.openai.com/v1/conversations/{conversation_id}`
_Note: Items in the conversation will **not** be deleted._

| Path Parameter    | Type   | Required     | Description                           |
| :---------------- | :----- | :----------- | :------------------------------------ |
| `conversation_id` | string | **Required** | The ID of the conversation to delete. |

**Returns:** A success message.

**Example Request (cURL):**

```bash
curl -X DELETE https://api.openai.com/v1/conversations/conv_123 \
  -H "Authorization: Bearer $OPENAI_API_KEY"
```

**Example Response:**

```json
{
  "id": "conv_123",
  "object": "conversation.deleted",
  "deleted": true
}
```

---

## Conversation Items

### List items

**GET** `https://api.openai.com/v1/conversations/{conversation_id}/items`

| Path Parameter    | Type   | Required     | Description                                   |
| :---------------- | :----- | :----------- | :-------------------------------------------- |
| `conversation_id` | string | **Required** | The ID of the conversation to list items for. |

| Query Parameter | Type    | Optional | Description                                                                                                         |
| :-------------- | :------ | :------- | :------------------------------------------------------------------------------------------------------------------ |
| `after`         | string  | Optional | An item ID to list items after, used in pagination.                                                                 |
| `include`       | array   | Optional | Specify additional output data to include (e.g., `web_search_call.action.sources`, `message.output_text.logprobs`). |
| `limit`         | integer | Optional | A limit on the number of objects to be returned. Range is 1 to 100, **default is 20**.                              |
| `order`         | string  | Optional | The order to return items in. Default is `desc`. Options: `asc`, `desc`.                                            |

**Returns:** A **list object** containing **Conversation items**.

**Example Request (cURL):**

```bash
curl "https://api.openai.com/v1/conversations/conv_123/items?limit=10" \
  -H "Authorization: Bearer $OPENAI_API_KEY"
```

**Example Response (Partial):**

```json
{
  "object": "list",
  "data": [
    {
      "type": "message",
      "id": "msg_abc",
      "status": "completed",
      "role": "user",
      "content": [{ "type": "input_text", "text": "Hello!" }]
    }
  ],
  "first_id": "msg_abc",
  "last_id": "msg_abc",
  "has_more": false
}
```

---

### Create items

**POST** `https://api.openai.com/v1/conversations/{conversation_id}/items`

| Path Parameter    | Type   | Required     | Description                                    |
| :---------------- | :----- | :----------- | :--------------------------------------------- |
| `conversation_id` | string | **Required** | The ID of the conversation to add the item to. |

| Query Parameter | Type  | Optional | Description                                                           |
| :-------------- | :---- | :------- | :-------------------------------------------------------------------- |
| `include`       | array | Optional | Additional fields to include in the response (same as in List items). |

| Request Body Parameter | Type  | Required     | Description                                                                     |
| :--------------------- | :---- | :----------- | :------------------------------------------------------------------------------ |
| `items`                | array | **Required** | The items to add to the conversation. You may add up to **20 items** at a time. |

**Returns:** A list of added items.

**Example Request (cURL):**

```bash
curl https://api.openai.com/v1/conversations/conv_123/items \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{
    "items": [
      { "type": "message", "role": "user", "content": [{"type": "input_text", "text": "Hello!"}] },
      { "type": "message", "role": "user", "content": [{"type": "input_text", "text": "How are you?"}] }
    ]
  }'
```

---

### Retrieve an item

**GET** `https://api.openai.com/v1/conversations/{conversation_id}/items/{item_id}`

| Path Parameter    | Type   | Required     | Description                                        |
| :---------------- | :----- | :----------- | :------------------------------------------------- |
| `conversation_id` | string | **Required** | The ID of the conversation that contains the item. |
| `item_id`         | string | **Required** | The ID of the item to retrieve.                    |

| Query Parameter | Type  | Optional | Description                                                           |
| :-------------- | :---- | :------- | :-------------------------------------------------------------------- |
| `include`       | array | Optional | Additional fields to include in the response (same as in List items). |

**Returns:** A **Conversation Item**.

**Example Request (cURL):**

```bash
curl https://api.openai.com/v1/conversations/conv_123/items/msg_abc \
  -H "Authorization: Bearer $OPENAI_API_KEY"
```

---

### Delete an item

**DELETE** `https://api.openai.com/v1/conversations/{conversation_id}/items/{item_id}`

| Path Parameter    | Type   | Required     | Description                                        |
| :---------------- | :----- | :----------- | :------------------------------------------------- |
| `conversation_id` | string | **Required** | The ID of the conversation that contains the item. |
| `item_id`         | string | **Required** | The ID of the item to delete.                      |

**Returns:** The updated **Conversation object**.

**Example Request (cURL):**

```bash
curl -X DELETE https://api.openai.com/v1/conversations/conv_123/items/msg_abc \
  -H "Authorization: Bearer $OPENAI_API_KEY"
```

---

## Object Schemas

### The Conversation Object

| Field        | Type    | Description                                                 |
| :----------- | :------ | :---------------------------------------------------------- |
| `id`         | string  | The unique ID of the conversation.                          |
| `object`     | string  | The object type, which is always `conversation`.            |
| `created_at` | integer | The time the conversation was created (Unix epoch seconds). |
| `metadata`   | object  | Set of 16 key-value pairs attached to the object.           |

### The Item List Object

| Field      | Type    | Description                                  |
| :--------- | :------ | :------------------------------------------- |
| `object`   | string  | The type of object returned, must be `list`. |
| `data`     | array   | A list of conversation items.                |
| `first_id` | string  | The ID of the first item in the list.        |
| `last_id`  | string  | The ID of the last item in the list.         |
| `has_more` | boolean | Whether there are more items available.      |
