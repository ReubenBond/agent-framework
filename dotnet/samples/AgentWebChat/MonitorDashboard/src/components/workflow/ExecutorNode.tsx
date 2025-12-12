import { memo } from 'react';
import { Handle, Position, type NodeProps } from '@xyflow/react';
import './ExecutorNode.css';

export type ExecutorState = 'pending' | 'running' | 'completed' | 'failed' | 'cancelled';

export interface ExecutorNodeData extends Record<string, unknown> {
  executorId: string;
  executorName?: string;
  state: ExecutorState;
  durationMs?: number;
  outputData?: unknown;
  error?: string;
  isStartNode?: boolean;
  layoutDirection?: 'LR' | 'TB';
}

const getStateConfig = (state: ExecutorState) => {
  switch (state) {
    case 'running':
      return {
        borderClass: 'executor-node--running',
        badgeClass: 'badge--running',
        label: 'Running',
      };
    case 'completed':
      return {
        borderClass: 'executor-node--completed',
        badgeClass: 'badge--completed',
        label: 'Completed',
      };
    case 'failed':
      return {
        borderClass: 'executor-node--failed',
        badgeClass: 'badge--failed',
        label: 'Failed',
      };
    case 'cancelled':
      return {
        borderClass: 'executor-node--cancelled',
        badgeClass: 'badge--cancelled',
        label: 'Cancelled',
      };
    case 'pending':
    default:
      return {
        borderClass: 'executor-node--pending',
        badgeClass: 'badge--pending',
        label: 'Pending',
      };
  }
};

export const ExecutorNode = memo(({ data, selected }: NodeProps) => {
  const nodeData = data as ExecutorNodeData;
  const config = getStateConfig(nodeData.state);
  const isRunning = nodeData.state === 'running';

  // Determine handle positions based on layout direction
  const isVertical = nodeData.layoutDirection === 'TB';
  const targetPosition = isVertical ? Position.Top : Position.Left;
  const sourcePosition = isVertical ? Position.Bottom : Position.Right;

  // Format duration
  const formatDuration = (ms: number) => {
    if (ms < 1000) return `${ms}ms`;
    if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
    return `${Math.floor(ms / 60000)}m ${Math.floor((ms % 60000) / 1000)}s`;
  };

  return (
    <div className={`executor-node ${config.borderClass} ${selected ? 'executor-node--selected' : ''}`}>
      {/* Connection handles */}
      <Handle
        type="target"
        position={targetPosition}
        id="target"
        className="executor-handle"
      />
      <Handle
        type="source"
        position={sourcePosition}
        id="source"
        className="executor-handle"
      />

      <div className="executor-node__content">
        {/* Header with icon and title */}
        <div className="executor-node__header">
          <div className="executor-node__icon">
            {nodeData.isStartNode ? (
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
                <polyline points="9 22 9 12 15 12 15 22" />
              </svg>
            ) : (
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
                <line x1="9" y1="9" x2="15" y2="15" />
                <line x1="15" y1="9" x2="9" y2="15" />
              </svg>
            )}
          </div>
          <div className="executor-node__info">
            <h3 className="executor-node__name">
              {nodeData.executorName || nodeData.executorId}
            </h3>
            {isRunning && <span className="executor-node__spinner" />}
          </div>
        </div>

        {/* Status badge and duration */}
        <div className="executor-node__footer">
          <span className={`executor-node__badge ${config.badgeClass}`}>
            {config.label}
          </span>
          {nodeData.durationMs !== undefined && nodeData.state !== 'pending' && (
            <span className="executor-node__duration">
              {formatDuration(nodeData.durationMs)}
            </span>
          )}
        </div>

        {/* Error message if failed */}
        {nodeData.error && (
          <div className="executor-node__error">
            {nodeData.error.substring(0, 100)}
            {nodeData.error.length > 100 ? '...' : ''}
          </div>
        )}
      </div>

      {/* Running animation overlay */}
      {isRunning && <div className="executor-node__pulse" />}
    </div>
  );
});

ExecutorNode.displayName = 'ExecutorNode';
