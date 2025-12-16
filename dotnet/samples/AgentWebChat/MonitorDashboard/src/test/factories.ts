import type { WorkerStatus, WorkflowMonitoringSummary } from '../types';

/**
 * Factory functions to create test data with sensible defaults.
 * Use these to create consistent test fixtures across all tests.
 */

export function createWorkerStatus(overrides: Partial<WorkerStatus> = {}): WorkerStatus {
  return {
    workerId: 'worker-1',
    address: 'http://localhost:5001',
    health: 'Healthy',
    lastHealthCheck: new Date().toISOString(),
    registeredAt: new Date().toISOString(),
    activeWorkflows: 0,
    supportedWorkflows: ['workflow-1'],
    supportedAgents: ['agent-1'],
    isDraining: false,
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
      workerId: `worker-${i + 1}`,
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
