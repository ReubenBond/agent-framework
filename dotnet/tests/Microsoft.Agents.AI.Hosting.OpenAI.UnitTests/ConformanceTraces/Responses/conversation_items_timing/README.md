# Conversation Items Timing Test

This trace documents the behavior of conversation items during response creation.

## Key Finding

Conversation items appear **atomically** - both input and output messages appear together only after the response completes. There is no intermediate state where the input is visible but the output is still processing.

## Test Methodology

1. Created a conversation
2. Started a long-running response (1000+ word essay)
3. Polled `/conversations/{id}/items` at intervals
4. Observed that items list was empty until response completed
5. Once response completed, both input and output messages appeared simultaneously with status "completed"

## Files

- `items_before_completion.json` - Empty items list during response processing
- `items_after_completion.json` - Both messages present with completed status
- `response_final.json` - The completed response object

## Implications

- Cannot use items endpoint for real-time progress monitoring
- All messages in items list are always in completed state
- Conversation items represent a consistent snapshot of completed interactions
- For progress tracking, use response object polling or streaming instead
