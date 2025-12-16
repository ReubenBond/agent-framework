import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { WorkerListWidget } from '../components/WorkerListWidget';
import { createWorkerStatus, createWorkers } from './factories';

// Mock the API module
vi.mock('../api', () => ({
  monitoringApi: {
    drainWorker: vi.fn().mockResolvedValue(undefined),
    enableWorker: vi.fn().mockResolvedValue(undefined),
  },
}));

describe('WorkerListWidget', () => {
  const defaultProps = {
    workers: [],
    stats: { registered: 0, healthy: 0, drained: 0 },
    isLoading: false,
    error: null,
    onRefresh: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('rendering', () => {
    it('renders loading skeleton when isLoading is true', () => {
      render(<WorkerListWidget {...defaultProps} isLoading={true} />);
      
      // Should show skeleton rows
      expect(screen.getByText('Workers')).toBeInTheDocument();
      // Skeleton elements won't have specific text, but the table structure should exist
      expect(screen.getByRole('table')).toBeInTheDocument();
    });

    it('renders error state when error is provided', () => {
      const error = new Error('Failed to fetch workers');
      render(<WorkerListWidget {...defaultProps} error={error} />);
      
      expect(screen.getByText(/Failed to load:/)).toBeInTheDocument();
      expect(screen.getByText(/Failed to fetch workers/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
    });

    it('renders empty state when no workers', () => {
      render(<WorkerListWidget {...defaultProps} />);
      
      expect(screen.getByText('No workers registered')).toBeInTheDocument();
    });

    it('renders worker list with correct data', () => {
      const workers = [
        createWorkerStatus({ workerId: 'worker-1', health: 'Healthy', activeWorkflows: 5 }),
        createWorkerStatus({ workerId: 'worker-2', health: 'Unhealthy', activeWorkflows: 0 }),
      ];
      const stats = { registered: 2, healthy: 1, drained: 0 };

      render(<WorkerListWidget {...defaultProps} workers={workers} stats={stats} />);

      // Check both workers are rendered
      expect(screen.getByText('worker-1')).toBeInTheDocument();
      expect(screen.getByText('worker-2')).toBeInTheDocument();

      // Check active workflow counts
      expect(screen.getByText('5')).toBeInTheDocument();

      // Check stats display
      expect(screen.getByText('1')).toBeInTheDocument(); // healthy count
    });

    it('renders each worker row with a unique key (no React warnings)', () => {
      // This test ensures the React key warning is fixed
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
      
      const workers = createWorkers(5);
      render(<WorkerListWidget {...defaultProps} workers={workers} />);

      // Should not have any React key warnings
      expect(consoleSpy).not.toHaveBeenCalledWith(
        expect.stringContaining('Each child in a list should have a unique "key" prop')
      );

      consoleSpy.mockRestore();
    });

    it('renders draining workers with correct badge', () => {
      const workers = [createWorkerStatus({ workerId: 'worker-1', isDraining: true })];

      render(<WorkerListWidget {...defaultProps} workers={workers} />);

      expect(screen.getByText('Draining')).toBeInTheDocument();
    });
  });

  describe('interactions', () => {
    it('expands worker details on row click', async () => {
      const user = userEvent.setup();
      const workers = [
        createWorkerStatus({
          workerId: 'worker-1',
          address: 'http://localhost:5001',
          supportedWorkflows: ['workflow-a', 'workflow-b'],
        }),
      ];

      render(<WorkerListWidget {...defaultProps} workers={workers} />);

      // Click to expand
      const row = screen.getByText('worker-1').closest('tr')!;
      await user.click(row);

      // Should show details
      expect(screen.getByText('http://localhost:5001')).toBeInTheDocument();
      expect(screen.getByText('workflow-a')).toBeInTheDocument();
      expect(screen.getByText('workflow-b')).toBeInTheDocument();
    });

    it('shows Enable button for draining workers', () => {
      const workers = [createWorkerStatus({ workerId: 'worker-1', isDraining: true })];

      render(<WorkerListWidget {...defaultProps} workers={workers} />);

      expect(screen.getByRole('button', { name: /enable/i })).toBeInTheDocument();
    });

    it('shows Drain button for healthy workers', () => {
      const workers = [createWorkerStatus({ workerId: 'worker-1', isDraining: false })];

      render(<WorkerListWidget {...defaultProps} workers={workers} />);

      expect(screen.getByRole('button', { name: /drain/i })).toBeInTheDocument();
    });

    it('calls onRefresh when retry button is clicked in error state', async () => {
      const user = userEvent.setup();
      const onRefresh = vi.fn();
      const error = new Error('Network error');

      render(<WorkerListWidget {...defaultProps} error={error} onRefresh={onRefresh} />);

      await user.click(screen.getByRole('button', { name: /retry/i }));

      expect(onRefresh).toHaveBeenCalledTimes(1);
    });
  });

  describe('stats display', () => {
    it('displays healthy and total worker counts', () => {
      const stats = { registered: 10, healthy: 8, drained: 2 };

      render(<WorkerListWidget {...defaultProps} stats={stats} />);

      expect(screen.getByText('8')).toBeInTheDocument();
      expect(screen.getByText('10')).toBeInTheDocument();
    });

    it('shows drained count when greater than zero', () => {
      const stats = { registered: 10, healthy: 8, drained: 2 };

      render(<WorkerListWidget {...defaultProps} stats={stats} />);

      // Find the drained stat
      expect(screen.getByText('2')).toBeInTheDocument();
      expect(screen.getByText('drained')).toBeInTheDocument();
    });

    it('hides drained count when zero', () => {
      const stats = { registered: 10, healthy: 10, drained: 0 };

      render(<WorkerListWidget {...defaultProps} stats={stats} />);

      expect(screen.queryByText('drained')).not.toBeInTheDocument();
    });
  });
});
