import { useMemo, useCallback, memo } from 'react';
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  useNodesState,
  useEdgesState,
  BackgroundVariant,
  type NodeTypes,
  type Node,
  type Edge,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { ExecutorNode, type ExecutorNodeData, type ExecutorState } from './ExecutorNode';
import { applyWorkflowLayout } from '../../utils/workflowLayout';
import type { WorkflowRun, WorkflowStepInfo } from '../../types';
import './WorkflowVisualizer.css';

const nodeTypes: NodeTypes = {
  executor: ExecutorNode,
};

interface WorkflowVisualizerProps {
  workflow: WorkflowRun;
  className?: string;
  layoutDirection?: 'LR' | 'TB';
  showMinimap?: boolean;
  showGrid?: boolean;
}

/**
 * Convert workflow steps to React Flow nodes
 */
function convertStepsToNodes(
  steps: WorkflowStepInfo[],
  layoutDirection: 'LR' | 'TB'
): Node<ExecutorNodeData>[] {
  if (steps.length === 0) return [];

  // Group steps by executor to handle multiple invocations
  const executorSteps = new Map<string, WorkflowStepInfo[]>();
  steps.forEach((step) => {
    const key = step.executorId;
    if (!executorSteps.has(key)) {
      executorSteps.set(key, []);
    }
    executorSteps.get(key)!.push(step);
  });

  // Create nodes from unique executors
  const nodes: Node<ExecutorNodeData>[] = [];
  let isFirst = true;

  executorSteps.forEach((stepList, executorId) => {
    // Get the latest step for this executor
    const latestStep = stepList[stepList.length - 1];
    
    // Determine state based on step status
    let state: ExecutorState = 'pending';
    if (latestStep.completedAt) {
      state = 'completed';
    } else if (latestStep.startedAt) {
      state = 'running';
    }

    // Check for errors in output
    const hasError = latestStep.output?.typeName?.toLowerCase().includes('error');
    if (hasError) {
      state = 'failed';
    }

    nodes.push({
      id: executorId,
      type: 'executor',
      position: { x: 0, y: 0 }, // Will be set by layout
      data: {
        executorId,
        executorName: latestStep.executorName || executorId,
        state,
        durationMs: latestStep.durationMs,
        outputData: latestStep.output?.data,
        isStartNode: isFirst,
        layoutDirection,
      },
    });

    isFirst = false;
  });

  return nodes;
}

/**
 * Convert workflow steps to React Flow edges based on execution order
 */
function convertStepsToEdges(steps: WorkflowStepInfo[]): Edge[] {
  if (steps.length < 2) return [];

  const edges: Edge[] = [];
  const addedEdges = new Set<string>();

  // Create edges based on execution sequence
  for (let i = 0; i < steps.length - 1; i++) {
    const source = steps[i].executorId;
    const target = steps[i + 1].executorId;
    
    // Skip self-loops and duplicate edges
    if (source === target) continue;
    
    const edgeKey = `${source}-${target}`;
    if (addedEdges.has(edgeKey)) continue;
    addedEdges.add(edgeKey);

    const sourceCompleted = !!steps[i].completedAt;
    const targetCompleted = !!steps[i + 1].completedAt;

    // Determine edge style based on execution state
    let style: React.CSSProperties = {
      stroke: '#444',
      strokeWidth: 2,
    };
    let animated = false;

    if (sourceCompleted && targetCompleted) {
      // Both completed - green edge
      style = {
        stroke: '#10b981',
        strokeWidth: 2,
      };
    } else if (sourceCompleted && !targetCompleted && steps[i + 1].startedAt) {
      // Source completed, target running - animated purple edge
      style = {
        stroke: '#8b5cf6',
        strokeWidth: 3,
      };
      animated = true;
    } else if (sourceCompleted) {
      // Source completed, target not yet running - orange edge
      style = {
        stroke: '#f59e0b',
        strokeWidth: 2,
      };
    }

    edges.push({
      id: edgeKey,
      source,
      target,
      sourceHandle: 'source',
      targetHandle: 'target',
      type: 'default',
      animated,
      style,
    });
  }

  return edges;
}

export const WorkflowVisualizer = memo(function WorkflowVisualizer({
  workflow,
  className = '',
  layoutDirection = 'LR',
  showMinimap = true,
  showGrid = true,
}: WorkflowVisualizerProps) {
  // Create nodes and edges from workflow steps
  const { initialNodes, initialEdges } = useMemo(() => {
    if (!workflow.steps || workflow.steps.length === 0) {
      return { initialNodes: [], initialEdges: [] };
    }

    const nodes = convertStepsToNodes(workflow.steps, layoutDirection);
    const edges = convertStepsToEdges(workflow.steps);

    // Apply layout
    const layoutedNodes = applyWorkflowLayout(nodes, edges, layoutDirection);

    return {
      initialNodes: layoutedNodes,
      initialEdges: edges,
    };
  }, [workflow.steps, layoutDirection]);

  const [nodes, , onNodesChange] = useNodesState<Node<ExecutorNodeData>>(initialNodes);
  const [edges, , onEdgesChange] = useEdgesState(initialEdges);

  const onNodeClick = useCallback((event: React.MouseEvent, node: Node<ExecutorNodeData>) => {
    event.stopPropagation();
    // Could be extended to show node details
    console.log('Node clicked:', node.data);
  }, []);

  if (!workflow.steps || workflow.steps.length === 0) {
    return (
      <div className={`workflow-visualizer workflow-visualizer--empty ${className}`}>
        <div className="workflow-visualizer__empty-state">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
            <line x1="9" y1="9" x2="15" y2="15" />
            <line x1="15" y1="9" x2="9" y2="15" />
          </svg>
          <p>No steps recorded yet</p>
        </div>
      </div>
    );
  }

  return (
    <div className={`workflow-visualizer ${className}`}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onNodeClick={onNodeClick}
        nodeTypes={nodeTypes}
        fitView
        fitViewOptions={{ padding: 0.2 }}
        minZoom={0.1}
        maxZoom={1.5}
        defaultEdgeOptions={{
          type: 'default',
          animated: false,
          style: { stroke: '#444', strokeWidth: 2 },
        }}
        nodesDraggable={true}
        nodesConnectable={false}
        elementsSelectable={true}
        proOptions={{ hideAttribution: true }}
      >
        {showGrid && (
          <Background
            variant={BackgroundVariant.Dots}
            gap={20}
            size={1}
            color="#333"
          />
        )}
        <Controls
          position="bottom-left"
          showInteractive={false}
          className="workflow-visualizer__controls"
        />
        {showMinimap && (
          <MiniMap
            nodeColor={(node: Node) => {
              const data = node.data as ExecutorNodeData;
              switch (data?.state) {
                case 'running':
                  return '#8b5cf6';
                case 'completed':
                  return '#10b981';
                case 'failed':
                  return '#ef4444';
                case 'cancelled':
                  return '#f97316';
                default:
                  return '#444';
              }
            }}
            maskColor="rgba(0, 0, 0, 0.2)"
            position="bottom-right"
            className="workflow-visualizer__minimap"
          />
        )}
      </ReactFlow>
    </div>
  );
});
