/**
 * Streaming State Persistence
 * 
 * Manages browser storage of streaming response state to enable:
 * - Resume interrupted streams after page refresh
 * - Replay cached events before fetching new ones
 * - Graceful recovery from network disconnections
 */

import type { ExtendedResponseStreamEvent } from "@/types/openai";

export interface StreamingState {
  conversationId: string;
  responseId: string;
  lastMessageId?: string;
  lastSequenceNumber: number;
  events: ExtendedResponseStreamEvent[];
  timestamp: number; // When this state was last updated
  completed: boolean; // Whether the stream completed successfully
  accumulatedText?: string; // Accumulated text content for quick restoration
}

const STORAGE_KEY_PREFIX = "devui_streaming_state_";
const STATE_EXPIRY_MS = 24 * 60 * 60 * 1000; // 24 hours

/**
 * Storage key for a specific conversation
 */
function getStorageKey(conversationId: string): string {
  return `${STORAGE_KEY_PREFIX}${conversationId}`;
}

/**
 * Extract accumulated text from events (for quick restoration)
 */
function extractAccumulatedText(events: ExtendedResponseStreamEvent[]): string {
  let text = "";
  for (const event of events) {
    if (event.type === "response.output_text.delta" && "delta" in event) {
      text += event.delta;
    }
  }
  return text;
}

/**
 * Save streaming state to browser storage
 */
export function saveStreamingState(state: StreamingState): void {
  try {
    const key = getStorageKey(state.conversationId);
    const data = JSON.stringify(state);
    localStorage.setItem(key, data);
  } catch (error) {
    console.error("Failed to save streaming state:", error);
    // If storage is full, try to clear old states
    try {
      clearExpiredStreamingStates();
      // Try again
      const key = getStorageKey(state.conversationId);
      const data = JSON.stringify(state);
      localStorage.setItem(key, data);
    } catch {
      console.error("Failed to save streaming state even after cleanup");
    }
  }
}

/**
 * Load streaming state from browser storage
 */
export function loadStreamingState(conversationId: string): StreamingState | null {
  try {
    console.log(`[StreamingState] Loading state for conversation ${conversationId}`);
    const key = getStorageKey(conversationId);
    const data = localStorage.getItem(key);
    
    if (!data) {
      console.log(`[StreamingState] No data found in localStorage for key ${key}`);
      return null;
    }

    const state: StreamingState = JSON.parse(data);
    console.log(`[StreamingState] Parsed state:`, state);

    // Check if state has expired
    const age = Date.now() - state.timestamp;
    if (age > STATE_EXPIRY_MS) {
      console.log(`[StreamingState] State expired (age: ${age}ms > ${STATE_EXPIRY_MS}ms)`);
      clearStreamingState(conversationId);
      return null;
    }

    // If stream was completed, no need to resume
    if (state.completed) {
      console.log(`[StreamingState] Stream already completed`);
      return null;
    }

    console.log(`[StreamingState] Returning valid incomplete state`);
    return state;
  } catch (error) {
    console.error("Failed to load streaming state:", error);
    return null;
  }
}

/**
 * Update streaming state with a new event
 */
export function updateStreamingState(
  conversationId: string,
  event: ExtendedResponseStreamEvent,
  responseId: string,
  lastMessageId?: string
): void {
  try {
    console.log(`[StreamingState] Updating state for conversation ${conversationId}, responseId: ${responseId}, event type: ${event.type}`);
    const existing = loadStreamingState(conversationId);
    const sequenceNumber = "sequence_number" in event ? event.sequence_number : undefined;
    
    const newEvents = existing ? [...existing.events, event] : [event];
    
    const state: StreamingState = {
      conversationId,
      responseId,
      lastMessageId,
      lastSequenceNumber: sequenceNumber ?? (existing?.lastSequenceNumber ?? -1),
      events: newEvents,
      timestamp: Date.now(),
      completed: event.type === "response.completed" || event.type === "response.failed",
      accumulatedText: extractAccumulatedText(newEvents),
    };

    saveStreamingState(state);
    console.log(`[StreamingState] Saved state with ${newEvents.length} events, completed: ${state.completed}`);
  } catch (error) {
    console.error("Failed to update streaming state:", error);
  }
}

/**
 * Mark streaming state as completed
 */
export function markStreamingCompleted(conversationId: string): void {
  try {
    const existing = loadStreamingState(conversationId);
    if (existing) {
      existing.completed = true;
      existing.timestamp = Date.now();
      saveStreamingState(existing);
    }
  } catch (error) {
    console.error("Failed to mark streaming as completed:", error);
  }
}

/**
 * Clear streaming state for a conversation
 */
export function clearStreamingState(conversationId: string): void {
  try {
    const key = getStorageKey(conversationId);
    localStorage.removeItem(key);
  } catch (error) {
    console.error("Failed to clear streaming state:", error);
  }
}

/**
 * Clear all expired streaming states
 */
export function clearExpiredStreamingStates(): void {
  try {
    const keys = Object.keys(localStorage);
    const now = Date.now();

    for (const key of keys) {
      if (key.startsWith(STORAGE_KEY_PREFIX)) {
        try {
          const data = localStorage.getItem(key);
          if (data) {
            const state: StreamingState = JSON.parse(data);
            const age = now - state.timestamp;
            
            if (age > STATE_EXPIRY_MS || state.completed) {
              localStorage.removeItem(key);
            }
          }
        } catch {
          // Invalid state, remove it
          localStorage.removeItem(key);
        }
      }
    }
  } catch (error) {
    console.error("Failed to clear expired streaming states:", error);
  }
}

/**
 * Initialize streaming state management (call on app startup)
 */
export function initStreamingState(): void {
  // Clear expired states on startup
  clearExpiredStreamingStates();
}
