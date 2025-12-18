import { useState, useMemo, useRef, useEffect, useCallback } from 'react';
import type { WorkflowMonitoringSummary } from '../types';
import { Skeleton } from './Skeleton';
import './WorkflowsWidget.css';

interface WorkflowStats {
  active: number;
  queued: number;
}

interface WorkflowsWidgetProps {
  activeWorkflows: WorkflowMonitoringSummary[];
  recentWorkflows: WorkflowMonitoringSummary[];
  stats: WorkflowStats | null;
  isLoading: boolean;
  error: Error | null;
  onRefresh: () => void;
  onSelectWorkflow: (runId: string) => void;
  onDeleteWorkflow?: (runId: string) => void;
  // Pagination props
  hasMoreWorkflows?: boolean;
  isLoadingMore?: boolean;
  onLoadMore?: () => void;
}

// Check if a workflow is in terminal status (can be deleted)
function isTerminalStatus(status: string): boolean {
  const lower = status.toLowerCase();
  return (
    lower.includes('completed') ||
    lower.includes('failed') ||
    lower.includes('cancelled') ||
    lower.includes('canceled') ||
    lower.includes('aborted')
  );
}

// Icon components
function InspectIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="11" cy="11" r="8" />
      <line x1="21" y1="21" x2="16.65" y2="16.65" />
      <line x1="11" y1="8" x2="11" y2="14" />
      <line x1="8" y1="11" x2="14" y2="11" />
    </svg>
  );
}

function DeleteIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
      <line x1="10" y1="11" x2="10" y2="17" />
      <line x1="14" y1="11" x2="14" y2="17" />
    </svg>
  );
}

// Check if a workflow is considered "active" based on its status
function isActiveWorkflow(wf: WorkflowMonitoringSummary): boolean {
  const lower = wf.status.toLowerCase();
  return (
    lower.includes('running') ||
    lower.includes('executing') ||
    lower.includes('pending') ||
    lower.includes('queued') ||
    lower.includes('waiting')
  );
}

export function WorkflowsWidget({
  activeWorkflows,
  recentWorkflows,
  stats,
  isLoading,
  error,
  onRefresh,
  onSelectWorkflow,
  onDeleteWorkflow,
  hasMoreWorkflows = false,
  isLoadingMore = false,
  onLoadMore,
}: WorkflowsWidgetProps) {
  const [searchQuery, setSearchQuery] = useState('');
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const loadMoreTriggerRef = useRef<HTMLDivElement>(null);

  // Infinite scroll: use IntersectionObserver to detect when the trigger element is visible
  const handleLoadMore = useCallback(() => {
    if (hasMoreWorkflows && !isLoadingMore && onLoadMore) {
      onLoadMore();
    }
  }, [hasMoreWorkflows, isLoadingMore, onLoadMore]);

  useEffect(() => {
    const triggerElement = loadMoreTriggerRef.current;
    const scrollContainer = scrollContainerRef.current;
    
    if (!triggerElement || !scrollContainer) return;

    const observer = new IntersectionObserver(
      (entries) => {
        const [entry] = entries;
        if (entry.isIntersecting) {
          handleLoadMore();
        }
      },
      {
        root: scrollContainer,
        rootMargin: '100px', // Start loading 100px before reaching the bottom
        threshold: 0,
      }
    );

    observer.observe(triggerElement);

    return () => {
      observer.disconnect();
    };
  }, [handleLoadMore]);

  // Merge and sort workflows: active first (sorted by createdAt desc), then recent (sorted by completedAt desc)
  const mergedWorkflows = useMemo(() => {
    // Create a map to deduplicate by runId (prefer active version if exists in both)
    const workflowMap = new Map<string, WorkflowMonitoringSummary>();
    
    // Add recent first so active can override
    recentWorkflows.forEach(wf => workflowMap.set(wf.runId, wf));
    activeWorkflows.forEach(wf => workflowMap.set(wf.runId, wf));
    
    const all = Array.from(workflowMap.values());
    
    // Separate active and recent
    const active = all.filter(isActiveWorkflow).sort((a, b) => 
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
    );
    const recent = all.filter(wf => !isActiveWorkflow(wf)).sort((a, b) => {
      const aTime = a.completedAt ? new Date(a.completedAt).getTime() : new Date(a.createdAt).getTime();
      const bTime = b.completedAt ? new Date(b.completedAt).getTime() : new Date(b.createdAt).getTime();
      return bTime - aTime;
    });
    
    return [...active, ...recent];
  }, [activeWorkflows, recentWorkflows]);

  // Filter workflows based on search query
  const workflows = useMemo(() => {
    if (!searchQuery.trim()) {
      return mergedWorkflows;
    }
    const query = searchQuery.toLowerCase();
    return mergedWorkflows.filter(
      (wf) =>
        wf.runId.toLowerCase().includes(query) ||
        wf.workflowName.toLowerCase().includes(query) ||
        wf.status.toLowerCase().includes(query)
    );
  }, [mergedWorkflows, searchQuery]);

  if (isLoading && activeWorkflows.length === 0 && recentWorkflows.length === 0) {
    return (
      <div className="workflows-widget">
        <div className="widget-header">
          <h2>Workflows</h2>
          <div className="header-center">
            <input
              type="text"
              className="search-input"
              placeholder="Search..."
              value=""
              disabled
            />
          </div>
          <div className="header-stats">
            <Skeleton variant="text" width={150} height={16} />
          </div>
        </div>
        <div className="workflow-list">
          <table className="workflow-table">
            <thead>
              <tr>
                <th>Run ID</th>
                <th>Workflow</th>
                <th>Status</th>
                <th>Started</th>
                <th>Completed</th>
                <th>Signal</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {[1, 2, 3].map((i) => (
                <tr key={i}>
                  <td><Skeleton variant="text" width={80} height={14} /></td>
                  <td><Skeleton variant="text" width={120} height={14} /></td>
                  <td><Skeleton variant="badge" /></td>
                  <td><Skeleton variant="text" width={60} height={14} /></td>
                  <td><Skeleton variant="text" width={60} height={14} /></td>
                  <td></td>
                  <td><Skeleton variant="rect" width={50} height={24} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="workflows-widget error">
        <h2>Workflows</h2>
        <p className="error-message">Failed to load: {error.message}</p>
        <button onClick={onRefresh} className="retry-button">Retry</button>
      </div>
    );
  }

  return (
    <div className="workflows-widget">
      <div className="widget-header">
        <h2>Workflows</h2>
        <div className="header-center">
          <input
            type="text"
            className="search-input"
            placeholder="Search..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>
        {stats && (
          <div className="header-stats">
            <span className="stat running" title="Active workflows">
              <span className="stat-value">{stats.active}</span>
              <span className="stat-label">active</span>
            </span>
            <span className="stat queued" title="Queued workflows">
              <span className="stat-value">{stats.queued}</span>
              <span className="stat-label">queued</span>
            </span>
          </div>
        )}
      </div>

      {workflows.length === 0 ? (
        <p className="no-data">
          {searchQuery
            ? 'No workflows match your search'
            : 'No workflows'}
        </p>
      ) : (
        <div className="workflow-list" ref={scrollContainerRef}>
          <table className="workflow-table">
            <thead>
              <tr>
                <th>Run ID</th>
                <th>Workflow</th>
                <th>Status</th>
                <th>Started</th>
                <th>Completed</th>
                <th>Signal</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {workflows.map((wf) => (
                <tr
                  key={wf.runId}
                  className={`status-${getStatusClass(wf.status)} clickable`}
                  onClick={() => onSelectWorkflow(wf.runId)}
                >
                  <td className="run-id" title={wf.runId}>
                    {wf.runId}
                  </td>
                  <td className="workflow-name">{wf.workflowName}</td>
                  <td>
                    <span className={`status-badge ${getStatusClass(wf.status)}`}>
                      {wf.status}
                    </span>
                  </td>
                  <td className="time">{formatTime(wf.createdAt)}</td>
                  <td className="time">
                    {wf.completedAt ? formatTime(wf.completedAt) : '-'}
                  </td>
                  <td className="signal">
                    {wf.hasPendingSignal && (
                      <span className="pending-signal" title="Waiting for signal">
                        Pending
                      </span>
                    )}
                  </td>
                  <td className="actions">
                    <button
                      className="action-icon-button inspect-button"
                      onClick={(e) => {
                        e.stopPropagation();
                        onSelectWorkflow(wf.runId);
                      }}
                      title="View details"
                    >
                      <InspectIcon />
                    </button>
                    {isTerminalStatus(wf.status) && onDeleteWorkflow && (
                      <button
                        className="action-icon-button delete-button"
                        onClick={(e) => {
                          e.stopPropagation();
                          onDeleteWorkflow(wf.runId);
                        }}
                        title="Delete workflow"
                      >
                        <DeleteIcon />
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          
          {/* Infinite scroll trigger element */}
          <div ref={loadMoreTriggerRef} className="infinite-scroll-trigger">
            {isLoadingMore && (
              <div className="loading-more">
                <span className="loading-spinner"></span>
                Loading more...
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

function getStatusClass(status: string): string {
  const lower = status.toLowerCase();
  if (lower.includes('running') || lower.includes('executing')) return 'running';
  if (lower.includes('completed') || lower.includes('success')) return 'completed';
  if (lower.includes('failed') || lower.includes('error')) return 'failed';
  if (lower.includes('waiting') || lower.includes('pending') || lower.includes('queued')) return 'waiting';
  if (lower.includes('cancelled') || lower.includes('canceled') || lower.includes('aborted')) return 'cancelled';
  return 'unknown';
}

function formatTime(isoDate: string): string {
  const date = new Date(isoDate);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffSeconds = Math.floor(diffMs / 1000);
  
  if (diffSeconds < 60) {
    return `${diffSeconds}s ago`;
  }
  
  const diffMinutes = Math.floor(diffSeconds / 60);
  if (diffMinutes < 60) {
    return `${diffMinutes}m ago`;
  }
  
  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) {
    return `${diffHours}h ${diffMinutes % 60}m ago`;
  }
  
  // For older items, show the date
  return date.toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}
