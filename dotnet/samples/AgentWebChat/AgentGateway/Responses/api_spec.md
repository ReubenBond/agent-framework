## OpenAI Responses API Specification

OpenAI's most advanced interface for generating model responses. Supports text and image inputs, and text outputs. Allows for stateful interactions and the extension of model capabilities with built-in tools (file search, web search, computer use) and function calling.

### Create a model response

**POST** `https://api.openai.com/v1/responses`

Creates a model response. Provide text or image inputs to generate text or JSON outputs. Allows the model to use custom code or built-in tools like web search or file search.

#### Request Body Parameters

| Parameter | Type | Required | Default | Description |
| :--- | :--- | :--- | :--- | :--- |
| `background` | boolean | Optional | `false` | Whether to run the model response in the background. |
| `conversation` | string or object | Optional | `null` | The conversation this response belongs to. Its items are prepended to `input_items`. Input/output items are added to this conversation upon completion. |
| `include` | array | Optional | - | Specify additional output data to include (e.g., `web_search_call.action.sources`, `message.output_text.logprobs`). |
| `input` | string or array | Optional | - | Text, image, or file inputs to the model. |
| `instructions` | string | Optional | - | A system or developer message inserted into the model's context. Overrides previous instructions when using `previous_response_id`. |
| `max_output_tokens` | integer | Optional | - | Upper bound for generated tokens (visible output + reasoning). |
| `max_tool_calls` | integer | Optional | - | The maximum number of total calls to built-in tools across the response. |
| `metadata` | map | Optional | - | Set of 16 key-value pairs (max 64 chars key, 512 chars value) for additional info and querying. |
| `model` | string | Optional | - | Model ID (e.g., `gpt-4o`, `o3`) used to generate the response. |
| `parallel_tool_calls`| boolean | Optional | `true` | Whether to allow the model to run tool calls in parallel. |
| `previous_response_id`| string | Optional | - | The ID of the previous response to create multi-turn conversations. Cannot be used with `conversation`. |
| `prompt` | object | Optional | - | Reference to a prompt template and its variables. |
| `prompt_cache_key` | string | Optional | - | Used to cache responses for similar requests (replaces `user` field). |
| `reasoning` | object | Optional | - | Configuration options for reasoning models (GPT-5 and o-series only). |
| `safety_identifier` | string | Optional | - | Stable identifier (e.g., hashed username/email) to help detect policy violations (replaces `user` field). |
| `service_tier` | string | Optional | `auto` | Specifies the processing type (`auto`, `default`, `flex`, or `priority`). Default uses project settings. |
| `store` | boolean | Optional | `true` | Whether to store the generated response for later retrieval. |
| `stream` | boolean | Optional | `false` | If `true`, streams the response data using server-sent events. |
| `stream_options` | object | Optional | `null` | Options for streaming responses (only when `stream: true`). |
| `temperature` | number | Optional | `1` | Sampling temperature between 0 and 2. Higher values are more random. Alter this or `top_p`, but not both. |
| `text` | object | Optional | - | Configuration for a text response, supporting plain text or structured JSON. |
| `tool_choice` | string or object | Optional | - | How the model selects which tool(s) to use. |
| `tools` | array | Optional | - | An array of tools the model may call (Built-in, MCP, or Function calls/Custom tools). |
| `top_logprobs` | integer | Optional | - | Integer between 0 and 20 for the number of most likely tokens to return with log probability. |
| `top_p` | number | Optional | `1` | Nucleus sampling alternative to temperature. E.g., 0.1 considers tokens with the top 10% probability mass. Alter this or `temperature`, but not both. |
| `truncation` | string | Optional | `disabled` | The truncation strategy. `auto` (drops old conversation items), or `disabled` (request fails if input exceeds context window). |
| `user` | string | **Deprecated** | - | Stable end-user identifier (replaced by `safety_identifier` and `prompt_cache_key`). |

**Returns:** A **Response object**.

**Example Request (cURL):**

```bash
curl https://api.openai.com/v1/responses \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{
    "model": "gpt-4.1",
    "input": "Tell me a three sentence bedtime story about a unicorn."
  }'
```

-----

### Get a model response

**GET** `https://api.openai.com/v1/responses/{response_id}`

Retrieves a model response with the given ID.

#### Path Parameters

| Path Parameter | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `response_id` | string | **Required** | The ID of the response to retrieve. |

#### Query Parameters

| Query Parameter | Type | Optional | Description |
| :--- | :--- | :--- | :--- |
| `include` | array | Optional | Additional fields to include in the response (same as in Response creation). |
| `include_obfuscation` | boolean | Optional | When `true` (default), enables stream obfuscation for security. Set to `false` to optimize bandwidth. |
| `starting_after` | integer | Optional | The sequence number of the event after which to start streaming. |
| `stream` | boolean | Optional | If `true`, the model response data will be streamed using server-sent events. |

**Returns:** The **Response object** matching the specified ID.

**Example Request (cURL):**

```bash
curl https://api.openai.com/v1/responses/resp_123 \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $OPENAI_API_KEY"
```

-----

### Delete a model response

**DELETE** `https://api.openai.com/v1/responses/{response_id}`

Deletes a model response with the given ID.

#### Path Parameters

| Path Parameter | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `response_id` | string | **Required** | The ID of the response to delete. |

**Returns:** A success message confirming deletion.

**Example Request (cURL):**

```bash
curl -X DELETE https://api.openai.com/v1/responses/resp_123 \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $OPENAI_API_KEY"
```

-----

### Cancel a response

**POST** `https://api.openai.com/v1/responses/{response_id}/cancel`

Cancels a model response with the given ID. *Only responses created with the `background` parameter set to `true` can be cancelled.*

#### Path Parameters

| Path Parameter | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `response_id` | string | **Required** | The ID of the response to cancel. |

**Returns:** A **Response object**.

**Example Request (cURL):**

```bash
curl -X POST https://api.openai.com/v1/responses/resp_123/cancel \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $OPENAI_API_KEY"
```

-----

### List input items

**GET** `https://api.openai.com/v1/responses/{response_id}/input_items`

Returns a list of input items for a given response.

#### Path Parameters

| Path Parameter | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `response_id` | string | **Required** | The ID of the response to retrieve input items for. |

#### Query Parameters

| Query Parameter | Type | Optional | Default | Description |
| :--- | :--- | :--- | :--- | :--- |
| `after` | string | Optional | - | An item ID to list items after, used in pagination. |
| `include` | array | Optional | - | Additional fields to include in the response (same as in Response creation). |
| `limit` | integer | Optional | `20` | A limit on the number of objects to be returned (range 1 to 100). |
| `order` | string | Optional | `desc` | The order to return the input items in. Options: `asc`, `desc`. |

**Returns:** A **list object** containing input item objects.

**Example Request (cURL):**

```bash
curl https://api.openai.com/v1/responses/resp_abc123/input_items \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $OPENAI_API_KEY"
```

-----

## Object Schemas

### The Response Object

| Field | Type | Description |
| :--- | :--- | :--- |
| `id` | string | Unique identifier for this Response. |
| `object` | string | The object type, always set to `response`. |
| `created_at` | number | Unix timestamp (in seconds) of when this Response was created. |
| `status` | string | The status of the response generation: `completed`, `failed`, `in_progress`, `cancelled`, `queued`, or `incomplete`. |
| `error` | object | An error object returned when generation fails. |
| `incomplete_details` | object | Details about why the response is incomplete. |
| `instructions` | string or array | System message inserted into the model's context. |
| `max_output_tokens` | integer | Upper bound for generated tokens (visible output + reasoning). |
| `max_tool_calls` | integer | Maximum number of total built-in tool calls allowed. |
| `model` | string | Model ID used to generate the response (e.g., `gpt-4o`). |
| `output` | array | An array of content items generated by the model. |
| `output_text` | string | **SDK Only**. Aggregated text output from all `output_text` items in the `output` array. |
| `parallel_tool_calls`| boolean | Whether tool calls can run in parallel. |
| `previous_response_id`| string | ID of the previous response for multi-turn conversations. |
| `reasoning` | object | Configuration options for reasoning models (GPT-5 and o-series only). |
| `store` | boolean | Whether the response is stored for later retrieval. |
| `temperature` | number | Sampling temperature (0 to 2). |
| `text` | object | Configuration options for a text response (plain text or structured JSON). |
| `tool_choice` | string or object | How the model selects which tool(s) to use. |
| `tools` | array | An array of tools the model may call. |
| `top_p` | number | Nucleus sampling probability mass (0 to 1). |
| `truncation` | string | The truncation strategy (`auto` or `disabled`). |
| `usage` | object | Token usage details (input tokens, output tokens, total tokens). |
| `metadata` | map | Set of 16 key-value pairs attached to the object. |
| `conversation` | object | The conversation this response belongs to. |
| `prompt` | object | Reference to a prompt template and its variables. |
| `prompt_cache_key` | string | Key used by OpenAI to cache responses (replaces `user`). |
| `safety_identifier` | string | Stable identifier to help detect policy violations (replaces `user`). |
| `service_tier` | string | The processing mode actually used to serve the request. |
| `user` | string | **Deprecated**. Stable end-user identifier. |

### The Input Item List Object

| Field | Type | Description |
| :--- | :--- | :--- |
| `object` | string | The type of object returned, always `list`. |
| `data` | array | A list of items used to generate this response. |
| `first_id` | string | The ID of the first item in the list. |
| `last_id` | string | The ID of the last item in the list. |
| `has_more` | boolean | Whether there are more items available. |

## Streaming Events for Response Generation

When a Response is created with `stream` set to `true`, the server emits **server-sent events (SSE)** as the response is generated. The following sections detail the events and their properties for an implementer.

### Response Lifecycle Events

These events track the overall status of the response object.

| Event Type | Description | Key Properties | Example Status |
| :--- | :--- | :--- | :--- |
| `response.created` | Emitted when a response object is **created**. | `response` (object), `sequence_number` (integer) | `"status": "in_progress"` |
| `response.queued` | Emitted when a response is **queued** and waiting to be processed. | `response` (object), `sequence_number` (integer) | `"status": "queued"` |
| `response.in_progress` | Emitted when the response is **in progress**. | `response` (object), `sequence_number` (integer) | `"status": "in_progress"` |
| `response.completed` | Emitted when the model response is **complete**. | `response` (object with completed properties), `sequence_number` (integer) | `"status": "completed"` |
| `response.incomplete` | Emitted when a response finishes as **incomplete** (e.g., due to `max_tokens`). | `response` (object, includes `incomplete_details`), `sequence_number` (integer) | `"status": "incomplete"` |
| `response.failed` | Emitted when a response **fails**. | `response` (object, includes `error` object), `sequence_number` (integer) | `"status": "failed"` |
| `error` | Emitted when an **error** occurs. | `code` (string), `message` (string), `param` (string), `sequence_number` (integer) | `"type": "error"` |

***

### Output Item and Content Events

These events relate to the generation of individual output items (like messages) and their content parts (like text or tool calls).

| Event Type | Description | Key Properties |
| :--- | :--- | :--- |
| `response.output_item.added` | A new output item (e.g., a message) is **added**. | `item` (object), `output_index` (integer), `sequence_number` (integer) |
| `response.output_item.done` | An output item is marked **done**. | `item` (object), `output_index` (integer), `sequence_number` (integer) |
| `response.content_part.added` | A new content part (e.g., a text block) is **added** to an output item. | `item_id` (string), `output_index` (integer), `content_index` (integer), `part` (object) |
| `response.content_part.done` | A content part is **done**. | `item_id` (string), `output_index` (integer), `content_index` (integer), `part` (object) |
| `response.output_text.delta` | An **additional text delta** (chunk) is available. | `item_id` (string), `output_index` (integer), `content_index` (integer), `delta` (string) |
| `response.output_text.done` | The text content is **finalized**. | `item_id` (string), `output_index` (integer), `content_index` (integer), `text` (string) |
| `response.output_text.annotation.added` | An **annotation is added** to output text content. | `item_id` (string), `output_index` (integer), `content_index` (integer), `annotation_index` (integer), `annotation` (object) |
| `response.refusal.delta` | A **partial refusal text** is streamed. | `item_id` (string), `output_index` (integer), `content_index` (integer), `delta` (string) |
| `response.refusal.done` | The **refusal text is finalized**. | `item_id` (string), `output_index` (integer), `content_index` (integer), `refusal` (string) |

***

### Tool Call Events

These events relate to the invocation and status of various tools (function, web search, code interpreter, etc.).

#### Function Calls

| Event Type | Description | Key Properties |
| :--- | :--- | :--- |
| `response.function_call_arguments.delta` | A **partial function-call arguments delta** is available. | `item_id` (string), `output_index` (integer), `delta` (string) |
| `response.function_call_arguments.done` | The **function-call arguments are finalized**. | `item_id` (string), `output_index` (integer), `name` (string), `arguments` (string) |

#### Web Search Calls 🔎

| Event Type | Description | Key Properties |
| :--- | :--- | :--- |
| `response.web_search_call.in_progress` | A web search call is **initiated**. | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |
| `response.web_search_call.searching` | The web search call is actively **executing**. | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |
| `response.web_search_call.completed` | The web search call is **completed**. | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |

#### File Search Calls

| Event Type | Description | Key Properties |
| :--- | :--- | :--- |
| `response.file_search_call.in_progress` | A file search call is **initiated**. | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |
| `response.file_search_call.searching` | The file search is currently **searching**. | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |
| `response.file_search_call.completed` | The file search call is **completed** (results found). | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |

#### Code Interpreter Calls 🧑‍💻

| Event Type | Description | Key Properties |
| :--- | :--- | :--- |
| `response.code_interpreter_call.in_progress` | A code interpreter call is **in progress**. | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |
| `response.code_interpreter_call.interpreting` | The code interpreter is actively **interpreting** the code snippet. | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |
| `response.code_interpreter_call.completed` | The code interpreter call is **completed**. | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |
| `response.code_interpreter_call_code.delta` | A **partial code snippet** is streamed. | `item_id` (string), `output_index` (integer), `delta` (string) |
| `response.code_interpreter_call_code.done` | The **code snippet is finalized**. | `item_id` (string), `output_index` (integer), `code` (string) |

#### Image Generation Calls 🖼️

| Event Type | Description | Key Properties |
| :--- | :--- | :--- |
| `response.image_generation_call.in_progress` | An image generation tool call is **in progress**. | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |
| `response.image_generation_call.generating` | The image generation call is actively **generating** an image (intermediate state). | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |
| `response.image_generation_call.partial_image` | A **partial image** is available during streaming. | `item_id` (string), `output_index` (integer), `partial_image_b64` (string), `partial_image_index` (integer) |
| `response.image_generation_call.completed` | The image generation tool call has **completed** and the final image is available. | `item_id` (string), `output_index` (integer), `sequence_number` (integer) |

#### MCP (Model Context Protocol) Calls

| Event Type | Description | Key Properties |
| :--- | :--- | :--- |
| `response.mcp_call.in_progress` | An **Model Context Protocol** tool call is **in progress**. | `item_id` (string), `output_index` (integer) |
| `response.mcp_call.completed` | An **Model Context Protocol** tool call has **completed successfully**. | `item_id` (string), `output_index` (integer) |
| `response.mcp_call.failed` | An **Model Context Protocol** tool call has **failed**. | `item_id` (string), `output_index` (integer) |
| `response.mcp_call_arguments.delta` | A **partial update (delta)** to the arguments of an **Model Context Protocol** tool call. | `item_id` (string), `output_index` (integer), `delta` (string) |
| `response.mcp_call_arguments.done` | The **arguments for an Model Context Protocol tool call are finalized**. | `item_id` (string), `output_index` (integer), `arguments` (string) |
| `response.mcp_list_tools.in_progress` | The system is retrieving the list of available **Model Context Protocol** tools. | `item_id` (string), `output_index` (integer) |
| `response.mcp_list_tools.completed` | The list of available **Model Context Protocol** tools has been **successfully retrieved**. | `item_id` (string), `output_index` (integer) |
| `response.mcp_list_tools.failed` | The attempt to list available **Model Context Protocol** tools has **failed**. | `item_id` (string), `output_index` (integer) |

#### Custom Tool Calls

| Event Type | Description | Key Properties |
| :--- | :--- | :--- |
| `response.custom_tool_call_input.delta` | A **delta (partial update)** to the input of a custom tool call. | `item_id` (string), `output_index` (integer), `delta` (string) |
| `response.custom_tool_call_input.done` | The **input for a custom tool call is complete**. | `item_id` (string), `output_index` (integer), `input` (string) |

***

### Reasoning and Summary Events

These events provide streaming updates for the model's internal reasoning process.

| Event Type | Description | Key Properties |
| :--- | :--- | :--- |
| `response.reasoning_summary_part.added` | A new **reasoning summary part is added**. | `item_id` (string), `output_index` (integer), `summary_index` (integer), `part` (object) |
| `response.reasoning_summary_part.done` | A **reasoning summary part is completed**. | `item_id` (string), `output_index` (integer), `summary_index` (integer), `part` (object) |
| `response.reasoning_summary_text.delta` | A **delta** is added to a reasoning summary text. | `item_id` (string), `output_index` (integer), `summary_index` (integer), `delta` (string) |
| `response.reasoning_summary_text.done` | The **reasoning summary text is completed**. | `item_id` (string), `output_index` (integer), `summary_index` (integer), `text` (string) |
| `response.reasoning_text.delta` | A **delta** is added to a reasoning text. | `item_id` (string), `output_index` (integer), `content_index` (integer), `delta` (string) |
| `response.reasoning_text.done` | The **reasoning text is completed**. | `item_id` (string), `output_index` (integer), `content_index` (integer), `text` (string) |