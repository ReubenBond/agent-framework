import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { WorkflowsWidget } from '../components/WorkflowsWidget';
import { createWorkflowSummary, createWorkflows } from './factories';

describe('WorkflowsWidget', () => {
  const defaultProps = {
    activeWorkflows: [],
    recentWorkflows: [],
    stats: { active: 0, queued: 0 },
    isLoading: false,
    error: null,
    onRefresh: vi.fn(),
    onSelectWorkflow: vi.fn(),
    onDeleteWorkflow: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('rendering', () => {
    it('renders loading skeleton when isLoading with no data', () => {
      render(<WorkflowsWidget {...defaultProps} isLoading={true} />);
      
      expect(screen.getByText('Workflows')).toBeInTheDocument();
      expect(screen.getByRole('table')).toBeInTheDocument();
    });

    it('renders error state when error is provided', () => {
      const error = new Error('Failed to fetch workflows');
      render(<WorkflowsWidget {...defaultProps} error={error} />);
      
      expect(screen.getByText(/Failed to load:/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
    });

    it('renders empty state when no workflows', () => {
      render(<WorkflowsWidget {...defaultProps} />);
      
      expect(screen.getByText('No workflows')).toBeInTheDocument();
    });

    it('renders workflow list with correct data', () => {
      const activeWorkflows = [
        createWorkflowSummary({ runId: 'run-1', workflowName: 'TestWorkflow', status: 'Running' }),
      ];

      render(<WorkflowsWidget {...defaultProps} activeWorkflows={activeWorkflows} />);

      expect(screen.getByText('run-1')).toBeInTheDocument();
      expect(screen.getByText('TestWorkflow')).toBeInTheDocument();
      expect(screen.getByText('Running')).toBeInTheDocument();
    });

    it('renders each workflow row with a unique key (no React warnings)', () => {
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
      
      const activeWorkflows = createWorkflows(5, { status: 'Running' });
      render(<WorkflowsWidget {...defaultProps} activeWorkflows={activeWorkflows} />);

      expect(consoleSpy).not.toHaveBeenCalledWith(
        expect.stringContaining('Each child in a list should have a unique "key" prop')
      );

      consoleSpy.mockRestore();
    });

    it('shows pending signal indicator when hasPendingSignal is true', () => {
      const activeWorkflows = [
        createWorkflowSummary({ runId: 'run-1', status: 'WaitingForSignal', hasPendingSignal: true }),
      ];

      render(<WorkflowsWidget {...defaultProps} activeWorkflows={activeWorkflows} />);

      expect(screen.getByText('Pending')).toBeInTheDocument();
    });

    it('shows delete button only for terminal workflows', () => {
      const activeWorkflows = [createWorkflowSummary({ runId: 'run-1', status: 'Running' })];
      const recentWorkflows = [createWorkflowSummary({ runId: 'run-2', status: 'Completed' })];

      render(
        <WorkflowsWidget
          {...defaultProps}
          activeWorkflows={activeWorkflows}
          recentWorkflows={recentWorkflows}
        />
      );

      // Should have delete button only for completed workflow
      const deleteButtons = screen.getAllByTitle('Delete workflow');
      expect(deleteButtons).toHaveLength(1);
    });
  });

  describe('controlled input behavior', () => {
    it('search input is controlled (has value prop) in both loading and normal state', () => {
      // First render in loading state
      const { rerender } = render(
        <WorkflowsWidget {...defaultProps} isLoading={true} />
      );
      
      const loadingInput = screen.getByPlaceholderText('Search...');
      expect(loadingInput).toHaveValue('');
      expect(loadingInput).toBeDisabled();

      // Re-render in normal state
      rerender(<WorkflowsWidget {...defaultProps} isLoading={false} />);
      
      const normalInput = screen.getByPlaceholderText('Search...');
      expect(normalInput).toHaveValue('');
      expect(normalInput).not.toBeDisabled();
    });

    it('does not trigger controlled/uncontrolled warning when transitioning states', () => {
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

      const { rerender } = render(
        <WorkflowsWidget {...defaultProps} isLoading={true} />
      );

      // Transition from loading to loaded
      rerender(<WorkflowsWidget {...defaultProps} isLoading={false} />);

      expect(consoleSpy).not.toHaveBeenCalledWith(
        expect.stringContaining('A component is changing an uncontrolled input to be controlled')
      );

      consoleSpy.mockRestore();
    });
  });

  describe('search functionality', () => {
    it('filters workflows by search query', async () => {
      const user = userEvent.setup();
      const activeWorkflows = [
        createWorkflowSummary({ runId: 'run-1', workflowName: 'OrderWorkflow' }),
        createWorkflowSummary({ runId: 'run-2', workflowName: 'PaymentWorkflow' }),
      ];

      render(<WorkflowsWidget {...defaultProps} activeWorkflows={activeWorkflows} />);

      const searchInput = screen.getByPlaceholderText('Search...');
      await user.type(searchInput, 'Order');

      expect(screen.getByText('OrderWorkflow')).toBeInTheDocument();
      expect(screen.queryByText('PaymentWorkflow')).not.toBeInTheDocument();
    });

    it('shows empty search results message when no matches', async () => {
      const user = userEvent.setup();
      const activeWorkflows = [createWorkflowSummary({ runId: 'run-1', workflowName: 'TestWorkflow' })];

      render(<WorkflowsWidget {...defaultProps} activeWorkflows={activeWorkflows} />);

      const searchInput = screen.getByPlaceholderText('Search...');
      await user.type(searchInput, 'NonExistent');

      expect(screen.getByText("No workflows match your search")).toBeInTheDocument();
    });
  });

  describe('interactions', () => {
    it('calls onSelectWorkflow when clicking a workflow row', async () => {
      const user = userEvent.setup();
      const onSelectWorkflow = vi.fn();
      const activeWorkflows = [createWorkflowSummary({ runId: 'run-1' })];

      render(
        <WorkflowsWidget
          {...defaultProps}
          activeWorkflows={activeWorkflows}
          onSelectWorkflow={onSelectWorkflow}
        />
      );

      const row = screen.getByText('run-1').closest('tr')!;
      await user.click(row);

      expect(onSelectWorkflow).toHaveBeenCalledWith('run-1');
    });

    it('calls onDeleteWorkflow when clicking delete button', async () => {
      const user = userEvent.setup();
      const onDeleteWorkflow = vi.fn();
      const recentWorkflows = [createWorkflowSummary({ runId: 'run-1', status: 'Completed' })];

      render(
        <WorkflowsWidget
          {...defaultProps}
          recentWorkflows={recentWorkflows}
          onDeleteWorkflow={onDeleteWorkflow}
        />
      );

      const deleteButton = screen.getByTitle('Delete workflow');
      await user.click(deleteButton);

      expect(onDeleteWorkflow).toHaveBeenCalledWith('run-1');
    });

    it('calls onRefresh when retry button is clicked in error state', async () => {
      const user = userEvent.setup();
      const onRefresh = vi.fn();
      const error = new Error('Network error');

      render(<WorkflowsWidget {...defaultProps} error={error} onRefresh={onRefresh} />);

      await user.click(screen.getByRole('button', { name: /retry/i }));

      expect(onRefresh).toHaveBeenCalledTimes(1);
    });
  });

  describe('stats display', () => {
    it('displays all stat categories', () => {
      const stats = { active: 5, queued: 3 };

      render(<WorkflowsWidget {...defaultProps} stats={stats} />);

      expect(screen.getByText('5')).toBeInTheDocument();
      expect(screen.getByText('active')).toBeInTheDocument();
      expect(screen.getByText('3')).toBeInTheDocument();
      expect(screen.getByText('queued')).toBeInTheDocument();
    });
  });

  describe('workflow merging and sorting', () => {
    it('deduplicates workflows by runId', () => {
      // Same workflow in both active and recent - should only appear once
      const activeWorkflows = [createWorkflowSummary({ runId: 'run-1', status: 'Running' })];
      const recentWorkflows = [createWorkflowSummary({ runId: 'run-1', status: 'Completed' })];

      render(
        <WorkflowsWidget
          {...defaultProps}
          activeWorkflows={activeWorkflows}
          recentWorkflows={recentWorkflows}
        />
      );

      // Should only have one row with run-1
      const rows = screen.getAllByText('run-1');
      expect(rows).toHaveLength(1);
    });

    it('shows active workflows before recent workflows', () => {
      const activeWorkflows = [createWorkflowSummary({ runId: 'active-1', status: 'Running' })];
      const recentWorkflows = [createWorkflowSummary({ runId: 'recent-1', status: 'Completed' })];

      render(
        <WorkflowsWidget
          {...defaultProps}
          activeWorkflows={activeWorkflows}
          recentWorkflows={recentWorkflows}
        />
      );

      const rows = screen.getAllByRole('row');
      // First data row (index 1, after header) should be active
      expect(rows[1]).toHaveTextContent('active-1');
      // Second data row should be recent
      expect(rows[2]).toHaveTextContent('recent-1');
    });
  });
});
