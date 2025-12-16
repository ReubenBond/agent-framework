import type { WorkerStatus, WorkflowMonitoringSummary } from '../types';

/**
 * Factory functions to create test data with sensible defaults.
 * Use these to create consistent test fixtures across all tests.
 */

export function createWorkerStatus(overrides: Partial<WorkerStatus> = {}): WorkerStatus {
  return {
    id: 'worker-1',
    hostId: 'host-1',
    endpoint: 'http://localhost:5001',
    status: 'Healthy',
    lastHeartbeat: new Date().toISOString(),
    consecutiveFailures: 0,
    isDefault: false,
    activeWorkflows: 0,
    ...overrides,
  };
}

export function createWorkflowSummary(overrides: Partial<WorkflowMonitoringSummary> = {}): WorkflowMonitoringSummary {
  return {
    runId: 'run-1',
    workflowName: 'TestWorkflow',
    status: 'Running',
    createdAt: new Date().toISOString(),
    hasPendingSignal: false,
    ...overrides,
  };
}

/**
 * Creates multiple workers with unique IDs
 */
export function createWorkers(count: number, baseOverrides: Partial<WorkerStatus> = {}): WorkerStatus[] {
  return Array.from({ length: count }, (_, i) =>
    createWorkerStatus({
      id: `worker-${i + 1}`,
      hostId: `host-${i + 1}`,
      ...baseOverrides,
    })
  );
}

/**
 * Creates multiple workflow summaries with unique IDs
 */
export function createWorkflows(count: number, baseOverrides: Partial<WorkflowMonitoringSummary> = {}): WorkflowMonitoringSummary[] {
  return Array.from({ length: count }, (_, i) =>
    createWorkflowSummary({
      runId: `run-${i + 1}`,
      ...baseOverrides,
    })
  );
}
