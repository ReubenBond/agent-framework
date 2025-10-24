# OpenAI Responses API: Edge Cases Report
## conversation.id and previous_response_id Testing

**Date:** October 27, 2025
**Tested By:** Edge case analysis with cURL
**API Version:** OpenAI Responses API (gpt-4o-mini)

---

## Executive Summary

This report documents the behavior of OpenAI's Responses API when using `conversation.id` and `previous_response_id` parameters, including their interactions and edge cases. The key finding is that these parameters are **mutually exclusive** and serve different purposes in conversation management.

---

## Test Scenarios & Results

### Scenario 1: Create Response with conversation.id ✅

**Request:**
```json
{
  "model": "gpt-4o-mini",
  "input": "What is the capital of France?",
  "conversation": {
    "id": "conv_68ffe6d9b8f48193a4bfadd3f3d277450ad2d29c24eaf56b"
  },
  "max_output_tokens": 50
}
```

**Result:** SUCCESS
**Response ID:** `resp_0ad2d29c24eaf56b0068ffe707a7908193b7afc6351d80e23c`
**Key Observations:**
- Response successfully created in the specified conversation
- Response object includes `conversation.id` field
- `previous_response_id` field is `null` (first message in conversation)

---

### Scenario 2: Non-existent conversation.id ❌

**Request:**
```json
{
  "model": "gpt-4o-mini",
  "input": "Hello, test message",
  "conversation": {
    "id": "conv_nonexistent123456789abcdef"
  },
  "max_output_tokens": 30
}
```

**Result:** ERROR
**Error Message:** `"Conversation with id 'conv_nonexistent123456789abcdef' not found."`
**Error Type:** `invalid_request_error`

**Key Observations:**
- Conversations must be explicitly created via `POST /v1/conversations`
- Cannot use arbitrary conversation IDs
- OpenAI does not auto-create conversations

---

### Scenario 3: Both conversation.id AND previous_response_id ❌

**Request:**
```json
{
  "model": "gpt-4o-mini",
  "input": "What is its population?",
  "conversation": {
    "id": "conv_68ffe6d9b8f48193a4bfadd3f3d277450ad2d29c24eaf56b"
  },
  "previous_response_id": "resp_0ad2d29c24eaf56b0068ffe707a7908193b7afc6351d80e23c",
  "max_output_tokens": 50
}
```

**Result:** ERROR
**Error Message:** `"Mutually exclusive parameters: ''. Ensure you are only providing one of: 'pre..._id' or 'conversation'."`
**Error Type:** `invalid_request_error`
**Error Code:** `mutually_exclusive_parameters`

**Key Observations:**
- **CRITICAL FINDING:** Cannot specify both parameters
- Must choose one approach: explicit conversation or response chaining
- This is enforced at the API level

---

### Scenario 4: Only previous_response_id (no conversation.id) ✅

**Request:**
```json
{
  "model": "gpt-4o-mini",
  "input": "What is its population?",
  "previous_response_id": "resp_0ad2d29c24eaf56b0068ffe707a7908193b7afc6351d80e23c",
  "max_output_tokens": 50
}
```

**Result:** SUCCESS
**Response ID:** `resp_0ad2d29c24eaf56b0068ffe8516bb881938c64f3446be0cd58`

**Key Observations:**
- Works perfectly without `conversation.id`
- Context is maintained (answered about Paris population correctly)
- Response object has `previous_response_id` but `conversation` field is `null`
- Input tokens: 34 (includes prior context)

---

### Scenario 5: Manually Adding Messages to Conversations ❌

**Request:**
```bash
POST /v1/conversations/conv_68ffe6d9b8f48193a4bfadd3f3d277450ad2d29c24eaf56b/messages
```

**Result:** ERROR
**Error Message:** `"Invalid URL (POST /v1/conversations/conv_.../messages)"`
**Error Type:** `invalid_request_error`

**Key Observations:**
- No API endpoint exists for manually adding messages to conversations
- Conversations can only be built through creating responses
- Cannot inject custom messages into conversation history

---

### Scenario 6: Chaining Multiple Responses ✅

**Request Chain:**
1. First: "What is the capital of France?" → Paris
2. Second: "What is its population?" (using previous_response_id)
3. Third: "What about its famous landmarks?" (using second response_id)

**Result:** SUCCESS
**Response ID (3rd):** `resp_0ad2d29c24eaf56b0068ffe8844f848193b412724317898b3d`

**Key Observations:**
- Three-level deep chaining works perfectly
- Context preserved across all responses
- Input tokens increase with chain depth (98 tokens for 3rd response)
- Each response links to its immediate predecessor via `previous_response_id`

---

### Scenario 7: Cross-Conversation previous_response_id ✅ (Interesting!)

**Setup:**
- Created second conversation about Tokyo
- Response ID from Tokyo: `resp_0838976cbac6eab30068ffe8a1e2588194b0950d5080f6bd21`
- Then used a Paris response ID as `previous_response_id`

**Request:**
```json
{
  "model": "gpt-4o-mini",
  "input": "Tell me about its architecture",
  "previous_response_id": "resp_0ad2d29c24eaf56b0068ffe707a7908193b7afc6351d80e23c",
  "max_output_tokens": 50
}
```

**Result:** SUCCESS
**Response:** Talked about Paris architecture (not Tokyo!)

**Key Observations:**
- **CRITICAL FINDING:** `previous_response_id` determines the conversation thread
- Even after creating a new conversation, using an old response ID continues the old thread
- The conversation context follows the response chain, not any "active" conversation
- Response object has `conversation` field as `null` when using `previous_response_id`

---

### Scenario 8: Invalid previous_response_id ❌

**Request:**
```json
{
  "model": "gpt-4o-mini",
  "input": "Hello",
  "previous_response_id": "resp_nonexistent123456789",
  "max_output_tokens": 30
}
```

**Result:** ERROR
**Error Message:** `"Previous response with id 'resp_nonexistent123456789' not found."`
**Error Type:** `invalid_request_error`
**Error Code:** `previous_response_not_found`

**Key Observations:**
- Previous response IDs must be valid and exist
- Cannot use arbitrary response IDs
- Similar validation to conversation IDs

---

## Key Findings Summary

### 1. Mutual Exclusivity
- ❌ **Cannot use both** `conversation.id` and `previous_response_id` together
- ✅ Must choose one approach per request

### 2. Conversation Management
- ✅ Conversations must be explicitly created via API
- ❌ Cannot manually add messages to conversations
- ✅ Conversations are built through response creation only

### 3. Response Chaining Behavior
- ✅ `previous_response_id` works without `conversation.id`
- ✅ Context is perfectly maintained across chains
- ✅ Can chain multiple responses deeply (tested 3+ levels)
- ✅ Chained responses have `conversation` field as `null`

### 4. Context Determination
- 🔑 **The `previous_response_id` determines the conversation thread**
- 🔑 Context follows the response chain, not explicit conversations
- 🔑 Using a response ID from conversation A will continue A's context, even if you just created conversation B

### 5. Validation
- ❌ Both conversation IDs and response IDs must exist
- ❌ Cannot use made-up or non-existent IDs
- ✅ API provides clear error messages for invalid references

### 6. Conversation Items Timing & Atomicity
- 🔑 **Conversation items are updated atomically** - input and output appear together
- 🔑 **No intermediate state exposed** - cannot see input before output completes
- 🔑 **All items show "completed" status** when first visible
- ❌ Cannot use items endpoint for real-time progress monitoring
- ✅ Use response object polling or streaming for progress tracking

---

## Architectural Implications

### For conversation.id Usage:
- Use when you want to **explicitly manage conversation boundaries**
- Useful for **starting new conversation threads**
- First response in a conversation must use this approach
- Subsequent responses can either:
  - Use `previous_response_id` to chain (recommended)
  - Use `conversation.id` to add a new branch

### For previous_response_id Usage:
- Use when you want to **continue an existing conversation**
- Most flexible approach for linear conversations
- No need to track conversation IDs separately
- **Recommended for most use cases** (simpler state management)

### Mixed Scenarios:
When you have a response that was created with `conversation.id`, you can:
1. Continue with `previous_response_id` → linear chain
2. Create new response with same `conversation.id` → branching
3. But **never both at once**

---

## Edge Cases to Watch For

### 1. Orphaned Response Chains
- Responses created with `previous_response_id` have no explicit `conversation.id`
- If you lose the response ID chain, you can't retrieve the conversation
- No API to "list all responses in a conversation" if using response chaining

### 2. Context Switching
- Be careful when switching between conversations
- Always verify which response ID you're using
- Using wrong response ID will continue wrong conversation thread

### 3. Conversation Branching
- Multiple responses can share the same `conversation.id` but different `previous_response_id`
- Creates a tree structure rather than linear chain
- Need to track which branch you're on

---

## Recommendations

### For Simple Linear Conversations:
```javascript
// Pattern: Just use previous_response_id
let responseId = null;

// First message
const resp1 = await createResponse({
  model: "gpt-4o-mini",
  input: "First message",
  conversation: { id: conversationId }  // Only for first message
});
responseId = resp1.id;

// Subsequent messages
const resp2 = await createResponse({
  model: "gpt-4o-mini",
  input: "Follow-up",
  previous_response_id: responseId  // No conversation.id needed
});
responseId = resp2.id;
```

### For Complex Multi-Branch Conversations:
```javascript
// Pattern: Use conversation.id to track branches
const conversationId = await createConversation();

// Branch 1
const branch1 = await createResponse({
  model: "gpt-4o-mini",
  input: "Branch 1 message",
  conversation: { id: conversationId }
});

// Branch 2 (separate from branch 1)
const branch2 = await createResponse({
  model: "gpt-4o-mini",
  input: "Branch 2 message",
  conversation: { id: conversationId }
});

// Continue branch 1
const branch1Next = await createResponse({
  model: "gpt-4o-mini",
  input: "Continue branch 1",
  previous_response_id: branch1.id  // NOT conversation.id
});
```

---

## Testing Details

All tests performed using:
- **Model:** gpt-4o-mini-2024-07-18
- **Tool:** cURL
- **Date:** October 27, 2025
- **Test Files:** Available in `/tmp/test*.json`

---

## Scenario 9: Conversation Items Timing - Input/Output Visibility ⏱️

**Question:** When creating a long-running response, can we observe the input message in the conversation before the output completes?

**Test Setup:**
1. Created conversation and started long-running response (1000+ word essay)
2. Polled `/conversations/{id}/items` endpoint at intervals
3. Tested both synchronous and background responses

**Results:**

**Synchronous Response Test:**
- Check 1 (immediate): `data: []` - Empty list
- Check 2 (0.5s later): `data: [assistant_message, user_message]` - Both present
- Both messages had `status: "completed"` when they appeared

**Background Response Test** (`background: true`):
- Initial response status: `"queued"`
- Conversation items immediately showed: `data: [assistant_message, user_message]`
- Both messages with `status: "completed"` from first observation

**Key Observations:**
- 🔑 **Conversation items appear ATOMICALLY** - Both input and output messages appear together
- 🔑 **No intermediate state is exposed** - Never observed input without output
- 🔑 **Both messages show as "completed"** when they first appear
- 🔑 **Items appear only after response completion** - Not during processing
- Even with `background: true`, items appear in final completed state

**Architectural Implication:**
OpenAI does not expose partial conversation state through the items API. The conversation history is updated as a **transaction** - all messages from a response operation appear simultaneously in their completed state. This means:
- Cannot observe "typing indicators" or partial responses via items API
- Cannot see user input before assistant output is ready
- Conversation items represent a **consistent snapshot** of completed interactions only

**For Progress Monitoring:**
To track in-progress responses, you must:
- Poll the response object itself (`GET /responses/{id}`) to check `status`
- Use streaming (`stream: true`) to get incremental output
- **Not** rely on conversation items endpoint for real-time updates

---

## Conclusion

The OpenAI Responses API provides two distinct, mutually exclusive approaches for conversation management:

1. **conversation.id** - Explicit conversation scoping
2. **previous_response_id** - Implicit response chaining

The key insights are:
- `previous_response_id` determines the conversation context regardless of any conversation IDs, making it the primary mechanism for maintaining conversational state
- `conversation.id` is primarily useful for creating initial messages and managing conversation boundaries at a higher level
- **Conversation items are updated atomically** - input and output messages appear together only after completion

For most implementations, **using `previous_response_id` for linear conversations is simpler and more robust** than tracking conversation IDs, as it naturally maintains context through the response chain.

For monitoring response progress, use the response object status or streaming, not the conversation items endpoint.
