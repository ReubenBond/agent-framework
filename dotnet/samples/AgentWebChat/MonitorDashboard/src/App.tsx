import { useState, useEffect, useCallback, useRef } from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { Sidebar } from './components';
import { DashboardPage, WorkersPage, WorkflowsPage } from './pages';
import { useMonitoringEvents } from './hooks';
import { monitoringApi } from './api';
import type {
  SystemStatus,
  WorkerStatus,
  WorkflowMonitoringSummary,
  MonitoringEvent,
} from './types';
import './App.css';

const REFRESH_INTERVAL_MS = 10000; // 10 seconds
const DEFAULT_PAGE_SIZE = 20;

export default function App() {
  // System status state
  const [systemStatus, setSystemStatus] = useState<SystemStatus | null>(null);

  // Workers state
  const [workers, setWorkers] = useState<WorkerStatus[]>([]);
  const [workersError, setWorkersError] = useState<Error | null>(null);

  // Workflows state with pagination
  const [activeWorkflows, setActiveWorkflows] = useState<WorkflowMonitoringSummary[]>([]);
  const [recentWorkflows, setRecentWorkflows] = useState<WorkflowMonitoringSummary[]>([]);
  const [workflowsError, setWorkflowsError] = useState<Error | null>(null);
  const [activeWorkflowsCursor, setActiveWorkflowsCursor] = useState<string | null>(null);
  const [recentWorkflowsCursor, setRecentWorkflowsCursor] = useState<string | null>(null);
  const [hasMoreActiveWorkflows, setHasMoreActiveWorkflows] = useState(false);
  const [hasMoreRecentWorkflows, setHasMoreRecentWorkflows] = useState(false);
  const [isLoadingMoreWorkflows, setIsLoadingMoreWorkflows] = useState(false);

  // Track initial load state (only show skeletons on first load)
  const [initialLoadComplete, setInitialLoadComplete] = useState(false);

  // Selected workflow for detail modal
  const [selectedWorkflowId, setSelectedWorkflowId] = useState<string | null>(null);

  // Theme state
  const [isLightTheme, setIsLightTheme] = useState(false);

  // SSE events state
  const [events, setEvents] = useState<MonitoringEvent[]>([]);

  // Track if initial fetch has been done
  const initialFetchDone = useRef(false);

  // Fetch functions - these update data silently in the background
  const fetchSystemStatus = useCallback(async () => {
    try {
      const status = await monitoringApi.getSystemStatus();
      setSystemStatus(status);
    } catch (e) {
      console.error('Failed to fetch system status:', e);
    }
  }, []);

  const fetchWorkers = useCallback(async () => {
    try {
      setWorkersError(null);
      const data = await monitoringApi.getWorkers();
      setWorkers(data);
    } catch (e) {
      setWorkersError(e instanceof Error ? e : new Error(String(e)));
    }
  }, []);

  const fetchWorkflows = useCallback(async () => {
    try {
      setWorkflowsError(null);
      const [activeResponse, recentResponse] = await Promise.all([
        monitoringApi.getActiveWorkflows(DEFAULT_PAGE_SIZE),
        monitoringApi.getRecentWorkflows(DEFAULT_PAGE_SIZE),
      ]);
      setActiveWorkflows(activeResponse.data);
      setRecentWorkflows(recentResponse.data);
      setActiveWorkflowsCursor(activeResponse.nextCursor ?? null);
      setRecentWorkflowsCursor(recentResponse.nextCursor ?? null);
      setHasMoreActiveWorkflows(activeResponse.hasMore);
      setHasMoreRecentWorkflows(recentResponse.hasMore);
    } catch (e) {
      setWorkflowsError(e instanceof Error ? e : new Error(String(e)));
    }
  }, []);

  const loadMoreWorkflows = useCallback(async () => {
    if (isLoadingMoreWorkflows) return;
    
    // Determine which cursor to use (prefer recent since it's more commonly paginated)
    const cursor = recentWorkflowsCursor || activeWorkflowsCursor;
    if (!cursor) return;
    
    try {
      setIsLoadingMoreWorkflows(true);
      
      // Load more from whichever has more data
      if (recentWorkflowsCursor) {
        const response = await monitoringApi.getRecentWorkflows(DEFAULT_PAGE_SIZE, recentWorkflowsCursor);
        setRecentWorkflows(prev => [...prev, ...response.data]);
        setRecentWorkflowsCursor(response.nextCursor ?? null);
        setHasMoreRecentWorkflows(response.hasMore);
      } else if (activeWorkflowsCursor) {
        const response = await monitoringApi.getActiveWorkflows(DEFAULT_PAGE_SIZE, activeWorkflowsCursor);
        setActiveWorkflows(prev => [...prev, ...response.data]);
        setActiveWorkflowsCursor(response.nextCursor ?? null);
        setHasMoreActiveWorkflows(response.hasMore);
      }
    } catch (e) {
      console.error('Failed to load more workflows:', e);
    } finally {
      setIsLoadingMoreWorkflows(false);
    }
  }, [isLoadingMoreWorkflows, recentWorkflowsCursor, activeWorkflowsCursor]);

  // Refresh all data silently in the background
  const refreshAll = useCallback(async () => {
    await Promise.all([
      fetchSystemStatus(),
      fetchWorkers(),
      fetchWorkflows(),
    ]);
  }, [fetchSystemStatus, fetchWorkers, fetchWorkflows]);

  // Handle SSE events - refresh data silently
  const handleEvent = useCallback((event: MonitoringEvent) => {
    setEvents((prev) => [...prev.slice(-999), event]); // Keep last 1000 events

    // Refresh relevant data based on event type (silently in background)
    if (event.eventType.includes('Worker')) {
      fetchWorkers();
      fetchSystemStatus();
    }
    if (event.eventType.includes('Workflow')) {
      fetchWorkflows();
      fetchSystemStatus();
    }
  }, [fetchWorkers, fetchWorkflows, fetchSystemStatus]);

  // SSE connection
  const { isConnected, reconnect } = useMonitoringEvents({
    onEvent: handleEvent,
    enabled: true,
  });

  // Initial fetch and periodic refresh
  useEffect(() => {
    if (!initialFetchDone.current) {
      initialFetchDone.current = true;
      // Initial load - then mark as complete
      refreshAll().then(() => {
        setInitialLoadComplete(true);
      });
    }

    const interval = setInterval(refreshAll, REFRESH_INTERVAL_MS);
    return () => clearInterval(interval);
  }, [refreshAll]);

  const handleSelectWorkflow = useCallback((runId: string) => {
    setSelectedWorkflowId(runId);
  }, []);

  const handleCloseModal = useCallback(() => {
    setSelectedWorkflowId(null);
  }, []);

  const handleDeleteWorkflow = useCallback(async (runId: string) => {
    if (!confirm(`Are you sure you want to delete workflow ${runId}?`)) {
      return;
    }
    try {
      await monitoringApi.deleteWorkflow(runId);
      // Refresh the workflow list after deletion
      await fetchWorkflows();
      await fetchSystemStatus();
    } catch (e) {
      console.error('Failed to delete workflow:', e);
      alert(`Failed to delete workflow: ${e instanceof Error ? e.message : String(e)}`);
    }
  }, [fetchWorkflows, fetchSystemStatus]);

  const toggleTheme = useCallback(() => {
    setIsLightTheme((prev) => !prev);
  }, []);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Ignore if typing in an input
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) {
        return;
      }

      switch (e.key.toLowerCase()) {
        case 'r':
          // Refresh all data
          refreshAll();
          break;
        case 'escape':
          // Close modal
          if (selectedWorkflowId) {
            handleCloseModal();
          }
          break;
        case 't':
          // Toggle theme
          toggleTheme();
          break;
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [refreshAll, selectedWorkflowId, handleCloseModal, toggleTheme]);

  // Extract stats for widgets
  const workerStats = systemStatus ? {
    registered: systemStatus.totalWorkers,
    healthy: systemStatus.activeWorkers,
    drained: 0, // API doesn't provide this
  } : null;

  const workflowStats = systemStatus ? {
    active: systemStatus.activeWorkflows,
    queued: systemStatus.pendingWorkflows,
  } : null;

  const uptime = systemStatus?.uptime ? formatUptime(systemStatus.uptime) : null;

  return (
    <BrowserRouter>
      <div className={`app ${isLightTheme ? 'theme-light' : ''}`}>
        <Sidebar isConnected={isConnected} isLightTheme={isLightTheme} onToggleTheme={toggleTheme} onReconnect={reconnect} />
        
        <div className="app-main">
          <main className="app-content">
            <Routes>
              <Route
                path="/"
                element={
                  <DashboardPage
                    systemStatus={systemStatus}
                    workers={workers}
                    workersError={workersError}
                    activeWorkflows={activeWorkflows}
                    recentWorkflows={recentWorkflows}
                    workflowsError={workflowsError}
                    isLoading={!initialLoadComplete}
                    onRefreshWorkers={fetchWorkers}
                    onRefreshWorkflows={fetchWorkflows}
                    selectedWorkflowId={selectedWorkflowId}
                    onSelectWorkflow={handleSelectWorkflow}
                    onDeleteWorkflow={handleDeleteWorkflow}
                    onCloseModal={handleCloseModal}
                    onWorkflowUpdated={refreshAll}
                    events={events}
                    uptime={uptime}
                    hasMoreWorkflows={hasMoreActiveWorkflows || hasMoreRecentWorkflows}
                    isLoadingMore={isLoadingMoreWorkflows}
                    onLoadMore={loadMoreWorkflows}
                  />
                }
              />
              <Route
                path="/workers"
                element={
                  <WorkersPage
                    workers={workers}
                    stats={workerStats}
                    isLoading={!initialLoadComplete}
                    error={workersError}
                    onRefresh={fetchWorkers}
                    events={events}
                  />
                }
              />
              <Route
                path="/workflows"
                element={
                  <WorkflowsPage
                    activeWorkflows={activeWorkflows}
                    recentWorkflows={recentWorkflows}
                    stats={workflowStats}
                    isLoading={!initialLoadComplete}
                    error={workflowsError}
                    onRefresh={fetchWorkflows}
                    selectedWorkflowId={selectedWorkflowId}
                    onSelectWorkflow={handleSelectWorkflow}
                    onDeleteWorkflow={handleDeleteWorkflow}
                    onCloseModal={handleCloseModal}
                    onWorkflowUpdated={refreshAll}
                    events={events}
                    hasMoreWorkflows={hasMoreActiveWorkflows || hasMoreRecentWorkflows}
                    isLoadingMore={isLoadingMoreWorkflows}
                    onLoadMore={loadMoreWorkflows}
                  />
                }
              />
            </Routes>
          </main>
        </div>
      </div>
    </BrowserRouter>
  );
}

function formatUptime(uptime: string): string {
  // Parse TimeSpan format like "1.02:03:04.5"
  const match = uptime.match(/^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})/);
  if (!match) return uptime;

  const days = match[1] ? parseInt(match[1]) : 0;
  const hours = parseInt(match[2]);
  const minutes = parseInt(match[3]);

  const parts: string[] = [];
  if (days > 0) parts.push(`${days}d`);
  if (hours > 0) parts.push(`${hours}h`);
  if (minutes > 0 || parts.length === 0) parts.push(`${minutes}m`);
  
  return parts.join(' ');
}
