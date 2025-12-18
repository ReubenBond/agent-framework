import type { WorkflowMonitoringSummary, MonitoringEvent } from '../types';
import { WorkflowsWidget, WorkflowDetailModal, EventFeedWidget } from '../components';
import './WorkflowsPage.css';

interface WorkflowsPageProps {
  activeWorkflows: WorkflowMonitoringSummary[];
  recentWorkflows: WorkflowMonitoringSummary[];
  stats: {
    active: number;
    queued: number;
  } | null;
  isLoading: boolean;
  error: Error | null;
  onRefresh: () => void;
  selectedWorkflowId: string | null;
  onSelectWorkflow: (runId: string) => void;
  onDeleteWorkflow: (runId: string) => void;
  onCloseModal: () => void;
  onWorkflowUpdated: () => void;
  events: MonitoringEvent[];
  // Pagination props
  hasMoreWorkflows?: boolean;
  isLoadingMore?: boolean;
  onLoadMore?: () => void;
}

export function WorkflowsPage({
  activeWorkflows,
  recentWorkflows,
  stats,
  isLoading,
  error,
  onRefresh,
  selectedWorkflowId,
  onSelectWorkflow,
  onDeleteWorkflow,
  onCloseModal,
  onWorkflowUpdated,
  events,
  hasMoreWorkflows = false,
  isLoadingMore = false,
  onLoadMore,
}: WorkflowsPageProps) {
  // Filter to workflow events only
  const workflowEvents = events.filter(e => e.eventType.toLowerCase().includes('workflow'));

  return (
    <div className="workflows-page">
      <header className="page-header">
        <h1>Workflows</h1>
        <p className="page-description">Monitor and manage workflow executions</p>
      </header>

      <div className="workflows-page-content">
        <div className="workflows-main">
          <WorkflowsWidget
            activeWorkflows={activeWorkflows}
            recentWorkflows={recentWorkflows}
            stats={stats}
            isLoading={isLoading}
            error={error}
            onRefresh={onRefresh}
            onSelectWorkflow={onSelectWorkflow}
            onDeleteWorkflow={onDeleteWorkflow}
            hasMoreWorkflows={hasMoreWorkflows}
            isLoadingMore={isLoadingMore}
            onLoadMore={onLoadMore}
          />
        </div>

        <div className="workflows-events">
          <EventFeedWidget
            events={workflowEvents}
            maxEvents={50}
          />
        </div>
      </div>

      {selectedWorkflowId && (
        <WorkflowDetailModal
          runId={selectedWorkflowId}
          onClose={onCloseModal}
          onWorkflowUpdated={onWorkflowUpdated}
        />
      )}
    </div>
  );
}
