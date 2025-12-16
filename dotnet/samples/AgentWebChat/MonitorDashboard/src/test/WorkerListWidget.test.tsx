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
        createWorkerStatus({ id: 'worker-1', status: 'Healthy', activeWorkflows: 5 }),
        createWorkerStatus({ id: 'worker-2', status: 'Unhealthy', activeWorkflows: 0 }),
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

    it('renders worker status badges correctly', () => {
      const workers = [
        createWorkerStatus({ id: 'worker-1', status: 'Healthy' }),
        createWorkerStatus({ id: 'worker-2', status: 'Unhealthy' }),
      ];

      render(<WorkerListWidget {...defaultProps} workers={workers} />);

      expect(screen.getByText('Healthy')).toBeInTheDocument();
      expect(screen.getByText('Unhealthy')).toBeInTheDocument();
    });
  });

  describe('interactions', () => {
    it('expands worker details on row click', async () => {
      const user = userEvent.setup();
      const workers = [
        createWorkerStatus({
          id: 'worker-1',
          hostId: 'host-1',
          endpoint: 'http://localhost:5001',
        }),
      ];

      render(<WorkerListWidget {...defaultProps} workers={workers} />);

      // Click to expand
      const row = screen.getByText('worker-1').closest('tr')!;
      await user.click(row);

      // Should show details
      expect(screen.getByText('http://localhost:5001')).toBeInTheDocument();
      expect(screen.getByText('host-1')).toBeInTheDocument();
    });

    it('shows Drain button for workers', () => {
      const workers = [createWorkerStatus({ id: 'worker-1' })];

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

    it('shows default worker badge in expanded details', async () => {
      const user = userEvent.setup();
      const workers = [createWorkerStatus({ id: 'worker-1', isDefault: true })];

      render(<WorkerListWidget {...defaultProps} workers={workers} />);

      // Click to expand
      const row = screen.getByText('worker-1').closest('tr')!;
      await user.click(row);

      expect(screen.getByText('Default Worker')).toBeInTheDocument();
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
